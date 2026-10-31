using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace DarkSwordsman.AgentBridge
{
    // Each command takes Args and returns plain text. Objects are addressed either by a scene path
    // (path=Root/Child, inactive objects included) or by a prefab asset plus an optional path inside
    // it (asset=Assets/x.prefab path=Child/Sub). Everything in this file is read-only except refresh and
    // select; the write commands are in WriteCommands.cs.
    internal static partial class Commands
    {
        internal static readonly Dictionary<string, Func<Args, string>> All = new Dictionary<string, Func<Args, string>>
        {
            ["ping"] = Ping,
            ["find"] = Find,
            ["hierarchy"] = Hierarchy,
            ["props"] = Props,
            ["material"] = MaterialInfo,
            ["mesh"] = MeshInfo,
            ["overrides"] = Overrides,
            ["refresh"] = a => { AssetDatabase.Refresh(); return "refreshed"; },
            ["select"] = Select,
            // writes, in WriteCommands.cs
            ["set"] = Set,
            ["setmat"] = SetMaterial,
            ["create"] = Create,
            ["delete"] = Delete,
            ["duplicate"] = Duplicate,
            ["reparent"] = Reparent,
            ["addcomp"] = AddComponent,
            ["rmcomp"] = RemoveComponent,
            ["apply"] = a => ApplyOrRevert(a, apply: true),
            ["revert"] = a => ApplyOrRevert(a, apply: false),
            ["menu"] = RunMenu,
            ["save"] = Save,
            ["open"] = Open,
            ["play"] = Play,
        };

        private static readonly HashSet<string> NoiseProps = new HashSet<string>
        {
            "m_ObjectHideFlags", "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset", "m_GameObject", "m_Script", "m_EditorHideFlags", "m_EditorClassIdentifier",
        };

        // ping
        private static string Ping(Args a)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"unity={Application.unityVersion} project={System.IO.Path.GetDirectoryName(Application.dataPath)}");
            sb.AppendLine($"compiling={EditorApplication.isCompiling} updating={EditorApplication.isUpdating} playing={EditorApplication.isPlaying} paused={EditorApplication.isPaused}");
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                sb.AppendLine($"scene: {s.path} loaded={s.isLoaded} dirty={s.isDirty}{(s == SceneManager.GetActiveScene() ? " (active)" : "")}");
            }
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null) sb.AppendLine($"prefab stage: {stage.assetPath}");
            sb.AppendLine("selection: " + (Selection.activeGameObject == null ? "none" : ScenePath(Selection.activeGameObject.transform)));
            return sb.ToString();
        }

        // find name=<substring> [component=<type or VRCFury feature>] [limit=200]
        private static string Find(Args a)
        {
            var name = a.Get("name", "");
            var comp = a.Get("component");
            var limit = a.Int("limit", 200);
            var hits = SceneRoots().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .Where(t => t.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                .Where(t => comp == null || t.GetComponents<Component>().Any(c => Matches(c, comp)))
                .ToList();
            var sb = new StringBuilder();
            foreach (var t in hits.Take(limit))
                sb.AppendLine((t.gameObject.activeInHierarchy ? "" : "[off] ") + ScenePath(t));
            if (hits.Count > limit) sb.AppendLine($"… {hits.Count - limit} more (raise limit=)");
            return hits.Count == 0 ? "no matches" : sb.ToString();
        }

        // hierarchy path=|asset= [depth=2] [components=1]
        private static string Hierarchy(Args a)
        {
            using (var t = Target.From(a))
            {
                var depth = a.Int("depth", 2);
                var comps = a.Bool("components", true);
                var sb = new StringBuilder();
                void Walk(Transform tr, int d)
                {
                    var line = new string(' ', d * 2) + (tr.gameObject.activeSelf ? "" : "[off] ") + tr.name;
                    if (comps)
                    {
                        var list = tr.GetComponents<Component>().Where(c => !(c is Transform)).Select(Label).ToList();
                        if (list.Count > 0) line += "  {" + string.Join(", ", list) + "}";
                    }
                    if (d == depth && tr.childCount > 0) line += $"  (+{tr.childCount} children)";
                    sb.AppendLine(line);
                    if (d < depth)
                        foreach (Transform c in tr) Walk(c, d + 1);
                }
                Walk(t.Object.transform, 0);
                return sb.ToString();
            }
        }

        // props path=|asset= [component=<type or VRCFury feature>] [index=<n>] [filter=<substring of property path>] [limit=400]
        // props asset=<non-prefab asset> [sub=<sub-asset name>] [filter=] [limit=] — the asset itself (.asset, .controller, …)
        private static string Props(Args a)
        {
            var asset = a.Get("asset");
            if (asset != null && !(AssetDatabase.LoadMainAssetAtPath(asset) is GameObject))
            {
                var sub = a.Get("sub");
                var obj = sub == null
                    ? AssetDatabase.LoadMainAssetAtPath(asset)
                    : AssetDatabase.LoadAllAssetsAtPath(asset).FirstOrDefault(o => o != null && o.name == sub);
                if (obj == null) throw new ArgumentException(sub == null ? $"no asset at {asset}" : $"no sub-asset '{sub}' in {asset}");
                return PropsOf(new Object[] { obj }, a.Get("filter"), a.Int("limit", 400));
            }
            using (var t = Target.From(a))
            {
                var comp = a.Get("component");
                var comps = t.Object.GetComponents<Component>().Where(c => c != null && (comp == null || Matches(c, comp))).ToList();
                var index = a.Get("index");
                if (index != null) comps = comps.Skip(int.Parse(index)).Take(1).ToList();
                if (comps.Count == 0) return $"no matching component on {t.Object.name}. Has: {string.Join(", ", t.Object.GetComponents<Component>().Where(c => c != null).Select(Label))}";
                return PropsOf(comps.Cast<Object>().ToList(), a.Get("filter"), a.Int("limit", 400));
            }
        }

        private static string PropsOf(IList<Object> comps, string filter, int limit)
        {
            var sb = new StringBuilder();
            var lines = 0;
            for (var i = 0; i < comps.Count; i++)
            {
                sb.AppendLine($"## [{i}] " + (comps[i] is Component c ? Label(c) : $"{comps[i].name} ({comps[i].GetType().Name})"));
                var so = new SerializedObject(comps[i]);
                var it = so.GetIterator();
                var enter = true;
                while (it.Next(enter))
                {
                    enter = it.propertyType != SerializedPropertyType.String && it.propertyType != SerializedPropertyType.ObjectReference && !NoiseProps.Contains(it.name);
                    if (NoiseProps.Contains(it.name)) continue;
                    if (filter != null && it.propertyPath.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var v = Value(it);
                    if (v == null) continue;
                    if (++lines > limit)
                    {
                        sb.AppendLine($"… stopped at limit={limit}; narrow with filter= or component=");
                        return sb.ToString();
                    }
                    sb.AppendLine($"  {it.propertyPath} = {v}");
                }
            }
            return sb.ToString();
        }

        // material asset=<.mat> | path=<renderer object> [slot=0]; [prefix=_UDIM] [keywords=0]
        private static string MaterialInfo(Args a)
        {
            var mat = ResolveMaterial(a);
            var prefix = a.Get("prefix", "");
            var sb = new StringBuilder();
            var sh = mat.shader;
            sb.AppendLine($"{AssetDatabase.GetAssetPath(mat)} shader={sh.name} locked={(sh.name.StartsWith("Hidden/Locked") ? "yes" : "no")} queue={mat.renderQueue}");
            for (var i = 0; i < sh.GetPropertyCount(); i++)
            {
                var n = sh.GetPropertyName(i);
                if (!n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                var v = MatValue(mat, n, sh.GetPropertyType(i));
                sb.AppendLine($"  {n} = {v}");
            }
            if (a.Bool("keywords", false)) sb.AppendLine("keywords: " + string.Join(" ", mat.shaderKeywords));
            return sb.ToString();
        }

        private static Material ResolveMaterial(Args a)
        {
            var asset = a.Get("asset");
            if (asset != null && asset.EndsWith(".mat"))
                return AssetDatabase.LoadAssetAtPath<Material>(asset) ?? throw new ArgumentException($"no material at {asset}");
            using (var t = Target.From(a))
            {
                var r = t.Object.GetComponent<Renderer>() ?? throw new ArgumentException($"{t.Object.name} has no Renderer");
                var slot = a.Int("slot", 0);
                return r.sharedMaterials.ElementAtOrDefault(slot) ?? throw new ArgumentException($"slot {slot} is empty");
            }
        }

        private static string MatValue(Material mat, string n, ShaderPropertyType type)
        {
            switch (type)
            {
                case ShaderPropertyType.Color: return mat.GetColor(n).ToString();
                case ShaderPropertyType.Vector: return mat.GetVector(n).ToString();
                case ShaderPropertyType.Texture: var tex = mat.GetTexture(n); return tex == null ? "none" : AssetDatabase.GetAssetPath(tex);
                case ShaderPropertyType.Int: return mat.GetInteger(n).ToString(CultureInfo.InvariantCulture);
                default: return mat.GetFloat(n).ToString("0.####", CultureInfo.InvariantCulture);
            }
        }

        // mesh path=|asset= [shapes=<substring>] — counts, UV tiles on the channel Poiyomi UDIM discard reads, blendshapes with the tiles they move, bones.
        private static string MeshInfo(Args a)
        {
            using (var t = Target.From(a))
            {
                var smr = t.Object.GetComponent<SkinnedMeshRenderer>() ?? throw new ArgumentException($"{t.Object.name} has no SkinnedMeshRenderer");
                var mesh = smr.sharedMesh ?? throw new ArgumentException("renderer has no mesh");
                var shapeFilter = a.Get("shapes");
                var sb = new StringBuilder();

                // Poiyomi names discard tiles Row<v>_<u> and reads the UV channel in _UDIMDiscardUV.
                var udimMat = smr.sharedMaterials.FirstOrDefault(m => m != null && m.HasProperty("_UDIMDiscardUV"));
                var channel = udimMat != null ? (int)udimMat.GetFloat("_UDIMDiscardUV") : 0;
                var uvs = new List<Vector2>();
                mesh.GetUVs(channel, uvs);
                var tileOf = uvs.Count == mesh.vertexCount ? uvs.Select(uv => new Vector2Int(Mathf.FloorToInt(uv.x), Mathf.FloorToInt(uv.y))).ToArray() : new Vector2Int[mesh.vertexCount];
                string Tile(Vector2Int k) => $"Row{k.y}_{k.x}";

                sb.AppendLine($"{mesh.name} ({AssetDatabase.GetAssetPath(mesh)}) vertices={mesh.vertexCount} triangles={mesh.triangles.Length / 3} submeshes={mesh.subMeshCount} blendshapes={mesh.blendShapeCount}");
                for (var i = 0; i < mesh.subMeshCount; i++)
                {
                    var m = smr.sharedMaterials.ElementAtOrDefault(i);
                    sb.AppendLine($"  submesh {i}: triangles={mesh.GetSubMesh(i).indexCount / 3} material={(m == null ? "null" : AssetDatabase.GetAssetPath(m))}");
                }
                sb.AppendLine($"UV{channel} tiles: " + string.Join(", ", tileOf.GroupBy(k => k).OrderBy(g => g.Key.y).ThenBy(g => g.Key.x).Select(g => $"{Tile(g.Key)}:{g.Count()}")));

                sb.AppendLine("blendshapes (weight, moved vertices per tile):");
                var dv = new Vector3[mesh.vertexCount];
                for (var s = 0; s < mesh.blendShapeCount; s++)
                {
                    var name = mesh.GetBlendShapeName(s);
                    if (shapeFilter != null && name.IndexOf(shapeFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    mesh.GetBlendShapeFrameVertices(s, mesh.GetBlendShapeFrameCount(s) - 1, dv, null, null);
                    var moved = new Dictionary<Vector2Int, int>();
                    for (var v = 0; v < dv.Length; v++)
                        if (dv[v].sqrMagnitude > 1e-10f) moved[tileOf[v]] = moved.TryGetValue(tileOf[v], out var n) ? n + 1 : 1;
                    sb.AppendLine($"  [{s}] {name} w={smr.GetBlendShapeWeight(s):0.##} " + (moved.Count == 0 ? "(no deltas)" : string.Join(", ", moved.OrderBy(k => k.Key.y).ThenBy(k => k.Key.x).Select(k => $"{Tile(k.Key)}:{k.Value}"))));
                }

                var weighted = new HashSet<int>();
                foreach (var w in mesh.boneWeights)
                {
                    if (w.weight0 > 0) weighted.Add(w.boneIndex0);
                    if (w.weight1 > 0) weighted.Add(w.boneIndex1);
                    if (w.weight2 > 0) weighted.Add(w.boneIndex2);
                    if (w.weight3 > 0) weighted.Add(w.boneIndex3);
                }
                var bones = smr.bones;
                sb.AppendLine($"bones={bones.Length} root={(smr.rootBone == null ? "null" : RelPath(t.Object.transform.root, smr.rootBone))} first={(bones.FirstOrDefault(b => b != null) is Transform fb ? RelPath(t.Object.transform.root, fb) : "null")}");
                sb.AppendLine("  weighted: " + string.Join(", ", bones.Where((b, i) => weighted.Contains(i)).Select(b => b == null ? "null" : b.name)));
                sb.AppendLine($"localBounds center={smr.localBounds.center} size={smr.localBounds.size} anchor={(smr.probeAnchor == null ? "null" : smr.probeAnchor.name)}");
                return sb.ToString();
            }
        }

        // overrides path=|asset= [filter=<substring of property path or target>] [limit=200] [defaults=0] — on a prefab instance root (outermost or nested).
        private static string Overrides(Args a)
        {
            using (var t = Target.From(a, loadContents: true))
            {
                var go = t.Object;
                if (!PrefabUtility.IsAnyPrefabInstanceRoot(go)) return $"ERROR: {go.name} is not a prefab instance root";
                var filter = a.Get("filter");
                var limit = a.Int("limit", 200);
                var sb = new StringBuilder();
                sb.AppendLine($"source: {AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(go))}");

                foreach (var r in PrefabUtility.GetRemovedGameObjects(go))
                    sb.AppendLine($"removed GameObject: {r.assetGameObject?.name} (under {r.parentOfRemovedGameObjectInInstance?.name})");
                foreach (var ad in PrefabUtility.GetAddedGameObjects(go))
                    sb.AppendLine($"added GameObject: {RelPath(go.transform, ad.instanceGameObject.transform)}");
                foreach (var rc in PrefabUtility.GetRemovedComponents(go))
                    sb.AppendLine($"removed component: {rc.assetComponent?.GetType().Name} on {RelPath(go.transform, rc.containingInstanceGameObject.transform)}");
                foreach (var ac in PrefabUtility.GetAddedComponents(go))
                    sb.AppendLine($"added component: {Label(ac.instanceComponent)} on {RelPath(go.transform, ac.instanceComponent.transform)}");

                var all = PrefabUtility.GetPropertyModifications(go) ?? new PropertyModification[0];
                // Default overrides (the root's name, position and rotation) are on every instance; hidden unless defaults=1.
                var mods = a.Bool("defaults", false) ? all : all.Where(m => !PrefabUtility.IsDefaultOverride(m)).ToArray();
                var shown = mods.Select(m => (target: m.target == null ? "null" : $"{m.target.name} ({m.target.GetType().Name})", m))
                    .Where(x => filter == null || x.m.propertyPath.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 || x.target.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
                sb.AppendLine($"property modifications: {mods.Length} total, {shown.Count} shown" + (all.Length > mods.Length ? $" ({all.Length - mods.Length} default overrides hidden; defaults=1 shows them)" : ""));
                foreach (var g in shown.Take(limit).GroupBy(x => x.target))
                {
                    sb.AppendLine($"  {g.Key}");
                    foreach (var x in g)
                        sb.AppendLine($"    {x.m.propertyPath} = {(x.m.objectReference != null ? Describe(x.m.objectReference) : x.m.value)}");
                }
                if (shown.Count > limit) sb.AppendLine($"… {shown.Count - limit} more (filter= or limit=)");
                return sb.ToString();
            }
        }

        // select path=<scene path> — works on inactive objects, for menu items that act on the selection.
        private static string Select(Args a)
        {
            var go = ResolveScenePath(a.Require("path"));
            Selection.activeGameObject = go;
            return "selected " + ScenePath(go.transform);
        }

        // --- helpers ---

        private sealed class Target : IDisposable
        {
            public GameObject Object;
            private GameObject _contentsRoot;

            public static Target From(Args a, bool loadContents = false)
            {
                var asset = a.Get("asset");
                var path = a.Get("path", "");
                if (asset == null) return new Target { Object = ResolveScenePath(a.Require("path")) };

                var t = new Target();
                GameObject root;
                if (loadContents)
                    root = t._contentsRoot = PrefabUtility.LoadPrefabContents(asset);
                else
                    root = AssetDatabase.LoadAssetAtPath<GameObject>(asset) ?? throw new ArgumentException($"no prefab or model at {asset}");
                var tr = path == "" || path == "." ? root.transform : root.transform.Find(path);
                if (tr == null)
                {
                    t.Dispose();
                    throw new ArgumentException($"'{path}' not found inside {asset}");
                }
                t.Object = tr.gameObject;
                return t;
            }

            public void Dispose()
            {
                if (_contentsRoot != null) PrefabUtility.UnloadPrefabContents(_contentsRoot);
                _contentsRoot = null;
            }
        }

        private static IEnumerable<GameObject> SceneRoots()
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null) return new[] { stage.prefabContentsRoot };
            return Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Where(s => s.isLoaded).SelectMany(s => s.GetRootGameObjects());
        }

        private static GameObject ResolveScenePath(string path)
        {
            var parts = path.Split(new[] { '/' }, 2);
            var roots = SceneRoots().Where(r => r.name == parts[0]).ToList();
            if (roots.Count == 0) throw new ArgumentException($"no root object named '{parts[0]}'. Roots: {string.Join(", ", SceneRoots().Select(r => r.name))}");
            foreach (var r in roots)
            {
                var tr = parts.Length == 1 ? r.transform : r.transform.Find(parts[1]);
                if (tr != null) return tr.gameObject;
            }
            throw new ArgumentException($"'{path}' not found (inactive objects are included)");
        }

        private static bool Matches(Component c, string name)
        {
            if (c == null) return false;
            var type = c.GetType().Name;
            if (type == name) return true;
            return type == "VRCFury" && FeatureType(c) == name;
        }

        private static string FeatureType(Component c)
        {
            var p = new SerializedObject(c).FindProperty("content");
            if (p == null || p.propertyType != SerializedPropertyType.ManagedReference) return null;
            var full = p.managedReferenceFullTypename;
            return string.IsNullOrEmpty(full) ? null : full.Substring(full.LastIndexOf('.') + 1);
        }

        // "SkinnedMeshRenderer", or "VRCFury:Toggle(Clothing/Sweatshirt)" for VRCFury features.
        private static string Label(Component c)
        {
            if (c == null) return "MissingScript";
            var type = c.GetType().Name;
            if (type != "VRCFury") return type;
            var name = new SerializedObject(c).FindProperty("content.name");
            return $"VRCFury:{FeatureType(c)}" + (name != null && name.propertyType == SerializedPropertyType.String && name.stringValue != "" ? $"({name.stringValue})" : "");
        }

        private static string Value(SerializedProperty p)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer: return p.longValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Boolean: return p.boolValue ? "true" : "false";
                case SerializedPropertyType.Float: return p.floatValue.ToString("0.####", CultureInfo.InvariantCulture);
                case SerializedPropertyType.String: return "\"" + p.stringValue + "\"";
                case SerializedPropertyType.Enum: return p.enumValueIndex >= 0 && p.enumValueIndex < p.enumDisplayNames.Length ? p.enumDisplayNames[p.enumValueIndex] : p.intValue.ToString();
                case SerializedPropertyType.ObjectReference: return p.objectReferenceValue == null ? "null" : Describe(p.objectReferenceValue);
                case SerializedPropertyType.ManagedReference:
                    var full = p.managedReferenceFullTypename;
                    return string.IsNullOrEmpty(full) ? "null (managed)" : "[" + full.Substring(full.LastIndexOf('.') + 1) + "]";
                case SerializedPropertyType.ArraySize: return p.intValue.ToString();
                case SerializedPropertyType.Color: return p.colorValue.ToString();
                case SerializedPropertyType.Vector2: return p.vector2Value.ToString();
                case SerializedPropertyType.Vector3: return p.vector3Value.ToString();
                case SerializedPropertyType.Vector4: return p.vector4Value.ToString();
                case SerializedPropertyType.Quaternion: return "euler" + p.quaternionValue.eulerAngles;
                case SerializedPropertyType.Rect: return p.rectValue.ToString();
                case SerializedPropertyType.Bounds: return p.boundsValue.ToString();
                case SerializedPropertyType.LayerMask: return p.intValue.ToString();
                case SerializedPropertyType.AnimationCurve: return "(curve)";
                case SerializedPropertyType.Gradient: return "(gradient)";
                default: return null; // generic containers: their children are printed instead
            }
        }

        private static string Describe(Object o)
        {
            if (o is GameObject go && go.scene.IsValid()) return ScenePath(go.transform) + " (GameObject)";
            if (o is Component c && c.gameObject.scene.IsValid()) return ScenePath(c.transform) + $" ({c.GetType().Name})";
            var path = AssetDatabase.GetAssetPath(o);
            if (string.IsNullOrEmpty(path)) return $"{o.name} ({o.GetType().Name})";
            var inPrefab = o is GameObject g ? g.transform : (o as Component)?.transform;
            if (inPrefab != null && inPrefab.parent != null) return $"{path} :: ./{RelPath(inPrefab.root, inPrefab)} ({o.GetType().Name})";
            return AssetDatabase.IsSubAsset(o) ? $"{path} :: {o.name} ({o.GetType().Name})" : $"{path} ({o.GetType().Name})";
        }

        private static string ScenePath(Transform t) => RelPath(null, t);

        private static string RelPath(Transform root, Transform t)
        {
            if (t == null) return "null";
            if (t == root) return ".";
            var parts = new List<string>();
            for (var cur = t; cur != null && cur != root; cur = cur.parent) parts.Add(cur.name);
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
