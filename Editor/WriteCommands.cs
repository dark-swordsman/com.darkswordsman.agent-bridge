using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace DarkSwordsman.AgentBridge
{
    // Write commands. Spec: Documentation~/write-commands.md.
    // Scene edits go through Undo and only dirty the scene (`save` writes it). Edits with asset=<.prefab>
    // load the prefab contents, change them and save the asset right away. Each command reports what changed.
    internal static partial class Commands
    {
        private static readonly HashSet<string> SetReserved = new HashSet<string> { "path", "asset", "component", "index", "sub" };
        private static readonly HashSet<string> SetMatReserved = new HashSet<string> { "path", "asset", "slot" };
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // set path=|asset= component=<type, VRCFury feature or GameObject> [index=] <propertyPath>=<value> ...
        // set asset=<non-prefab asset> [sub=<sub-asset name>] <propertyPath>=<value> ...
        private static string Set(Args a)
        {
            var asset = a.Get("asset");
            if (asset != null && !IsPrefab(asset))
            {
                var sub = a.Get("sub");
                var obj = sub == null
                    ? AssetDatabase.LoadMainAssetAtPath(asset)
                    : AssetDatabase.LoadAllAssetsAtPath(asset).FirstOrDefault(o => o != null && o.name == sub);
                if (obj == null) throw new ArgumentException(sub == null ? $"no asset at {asset}" : $"no sub-asset '{sub}' in {asset}");
                if (obj is GameObject) throw new ArgumentException($"{asset} is a model; only .prefab assets can be edited");
                Undo.IncrementCurrentGroup();
                var result = SetProps(obj, a, null);
                AssetDatabase.SaveAssetIfDirty(obj);
                return result + $"saved {asset}\n";
            }

            using (var e = Edit.From(a, "set"))
            {
                var comp = a.Require("component");
                Object target = comp == "GameObject" ? (Object)e.Object : PickComponent(e.Object, comp, a.Get("index"));
                return e.Commit(SetProps(target, a, e));
            }
        }

        private static string SetProps(Object target, Args a, Edit e)
        {
            var so = new SerializedObject(target);
            var undo = e == null || !e.IsAsset;
            var sb = new StringBuilder();
            foreach (var kv in a.Pairs.Where(kv => !SetReserved.Contains(kv.Key)))
            {
                try
                {
                    var p = so.FindProperty(kv.Key) ?? throw new ArgumentException($"no property '{kv.Key}' on {Describe(target)}.{Similar(so, kv.Key)}");
                    var before = Value(p) ?? "(…)";
                    Assign(p, kv.Value, e);
                    // Applied one at a time so a size change is visible to the element paths that follow it.
                    if (undo) so.ApplyModifiedProperties();
                    else so.ApplyModifiedPropertiesWithoutUndo();
                    so.Update();
                    sb.AppendLine($"{kv.Key}: {before} -> {Value(so.FindProperty(kv.Key)) ?? "(…)"}");
                }
                catch (Exception ex) when (sb.Length > 0 && (ex is ArgumentException || ex is FormatException))
                {
                    // Earlier properties are already applied (undoable in a scene; an asset edit isn't saved).
                    throw new ArgumentException($"{ex.Message}\nalready applied before the failure:\n{sb}");
                }
            }
            if (sb.Length == 0) throw new ArgumentException("nothing to set: pass <propertyPath>=<value> pairs (see `props` for paths)");
            return sb.ToString();
        }

        private static string Similar(SerializedObject so, string key)
        {
            var last = key.Substring(key.LastIndexOf('.') + 1);
            var hits = new List<string>();
            var it = so.GetIterator();
            while (it.Next(true) && hits.Count < 10)
                if (it.propertyPath.IndexOf(last, StringComparison.OrdinalIgnoreCase) >= 0) hits.Add(it.propertyPath);
            return hits.Count == 0 ? " Use `props` to list paths." : " Similar: " + string.Join(", ", hits);
        }

        private static void Assign(SerializedProperty p, string v, Edit e)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer: p.longValue = long.Parse(v, Inv); break;
                case SerializedPropertyType.ArraySize:
                case SerializedPropertyType.LayerMask: p.intValue = int.Parse(v, Inv); break;
                case SerializedPropertyType.Boolean: p.boolValue = ParseBool(v); break;
                case SerializedPropertyType.Float: p.doubleValue = double.Parse(v, Inv); break;
                case SerializedPropertyType.String: p.stringValue = v.Length >= 2 && v[0] == '"' && v[v.Length - 1] == '"' ? v.Substring(1, v.Length - 2) : v; break;
                case SerializedPropertyType.Enum:
                    var names = p.enumNames;
                    var i = Array.FindIndex(names, n => n.Equals(v, StringComparison.OrdinalIgnoreCase));
                    if (i < 0) i = Array.FindIndex(p.enumDisplayNames, n => n.Equals(v, StringComparison.OrdinalIgnoreCase));
                    if (i < 0 && !int.TryParse(v, out i)) throw new ArgumentException($"'{v}' is not one of: {string.Join(", ", names)}");
                    p.enumValueIndex = i;
                    break;
                case SerializedPropertyType.Color: p.colorValue = ParseColor(v); break;
                case SerializedPropertyType.Vector2: var v2 = Numbers(v, 2); p.vector2Value = new Vector2(v2[0], v2[1]); break;
                case SerializedPropertyType.Vector3: var v3 = Numbers(v, 3); p.vector3Value = new Vector3(v3[0], v3[1], v3[2]); break;
                case SerializedPropertyType.Vector4: var v4 = Numbers(v, 4); p.vector4Value = new Vector4(v4[0], v4[1], v4[2], v4[3]); break;
                case SerializedPropertyType.Vector2Int: var i2 = Numbers(v, 2); p.vector2IntValue = new Vector2Int((int)i2[0], (int)i2[1]); break;
                case SerializedPropertyType.Vector3Int: var i3 = Numbers(v, 3); p.vector3IntValue = new Vector3Int((int)i3[0], (int)i3[1], (int)i3[2]); break;
                case SerializedPropertyType.Quaternion: var q = Numbers(v, 3); p.quaternionValue = Quaternion.Euler(q[0], q[1], q[2]); break;
                case SerializedPropertyType.Rect: var r = Numbers(v, 4); p.rectValue = new Rect(r[0], r[1], r[2], r[3]); break;
                case SerializedPropertyType.ObjectReference: p.objectReferenceValue = ResolveRef(v, e, PPtrType(p)); break;
                case SerializedPropertyType.ManagedReference:
                    if (v == "null") p.managedReferenceValue = null;
                    else if (v.StartsWith("new:")) p.managedReferenceValue = Activator.CreateInstance(ManagedType(p, v.Substring(4)));
                    else throw new ArgumentException("managed references take null or new:<TypeName>");
                    break;
                default: throw new ArgumentException($"{p.propertyPath} is a {p.propertyType}; set its children instead");
            }
        }

        private static bool ParseBool(string v)
        {
            switch (v.ToLowerInvariant())
            {
                case "1": case "true": case "yes": case "on": return true;
                case "0": case "false": case "no": case "off": return false;
                default: throw new ArgumentException($"'{v}' is not a boolean");
            }
        }

        // Accepts "1,0,0", "(1.00, 0.00, 0.00)", "RGBA(1, 0, 0, 1)", "euler(0, 90, 0)": whatever `props` printed.
        private static float[] Numbers(string v, int count)
        {
            var n = Regex.Matches(v, @"-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?").Cast<Match>().Select(m => float.Parse(m.Value, Inv)).ToArray();
            if (n.Length < count) throw new ArgumentException($"expected {count} numbers, got '{v}'");
            return n;
        }

        private static Color ParseColor(string v)
        {
            if (v.StartsWith("#")) return ColorUtility.TryParseHtmlString(v, out var c) ? c : throw new ArgumentException($"bad color '{v}'");
            var n = Numbers(v, 3);
            return new Color(n[0], n[1], n[2], n.Length > 3 ? n[3] : 1f);
        }

        // "PPtr<$SkinnedMeshRenderer>" -> "SkinnedMeshRenderer"
        private static string PPtrType(SerializedProperty p)
        {
            var m = Regex.Match(p.type, @"PPtr<\$?(\w+)>");
            return m.Success ? m.Groups[1].Value : "Object";
        }

        private static Type ManagedType(SerializedProperty p, string name)
        {
            var field = p.managedReferenceFieldTypename.Split(' '); // "Assembly Namespace.Outer/Inner"
            var baseType = Type.GetType($"{field[1].Replace('/', '+')}, {field[0]}") ?? throw new ArgumentException($"can't resolve field type {p.managedReferenceFieldTypename}");
            var hits = TypeCache.GetTypesDerivedFrom(baseType).Append(baseType)
                .Where(t => !t.IsAbstract && (t.Name == name || t.FullName == name)).Distinct().ToList();
            if (hits.Count == 1) return hits[0];
            if (hits.Count == 0) throw new ArgumentException($"no type '{name}' fits {baseType.Name}. Options: {string.Join(", ", TypeCache.GetTypesDerivedFrom(baseType).Where(t => !t.IsAbstract).Select(t => t.Name).OrderBy(n => n).Take(60))}");
            throw new ArgumentException($"'{name}' is ambiguous: {string.Join(", ", hits.Select(t => t.FullName))}");
        }

        // null | Assets/x.mat | Assets/x.fbx :: Sub | Root/Child | ./Child (asset mode), each optionally ending in " (Type)".
        private static Object ResolveRef(string v, Edit e, string expected)
        {
            if (v == "null") return null;
            var m = Regex.Match(v, @"^(.*) \(([\w.]+)\)$");
            var type = m.Success ? m.Groups[2].Value : expected;
            if (m.Success) v = m.Groups[1].Value;

            if (v.StartsWith("Assets/") || v.StartsWith("Packages/"))
            {
                var parts = v.Split(new[] { " :: " }, 2, StringSplitOptions.None);
                if (parts.Length > 1 && parts[1].StartsWith("./"))
                {
                    if (e != null && e.IsAsset && e.AssetPath == parts[0]) return PickRef(e.FindInAsset(parts[1]), type, v);
                    var root = AssetDatabase.LoadAssetAtPath<GameObject>(parts[0]) ?? throw new ArgumentException($"no prefab at {parts[0]}");
                    var inner = parts[1] == "./." ? root.transform : root.transform.Find(parts[1].Substring(2));
                    return PickRef(inner != null ? inner.gameObject : throw new ArgumentException($"'{parts[1]}' not found inside {parts[0]}"), type, v);
                }
                var all = AssetDatabase.LoadAllAssetsAtPath(parts[0]).Where(o => o != null).ToList();
                if (all.Count == 0) throw new ArgumentException($"no asset at {parts[0]}");
                var main = AssetDatabase.LoadMainAssetAtPath(parts[0]);
                var pool = parts.Length > 1 ? all.Where(o => o.name == parts[1]).ToList() : new List<Object> { main }.Concat(all).ToList();
                var hit = pool.FirstOrDefault(o => IsType(o.GetType(), type))
                          ?? (pool.FirstOrDefault() is GameObject go ? go.GetComponents<Component>().FirstOrDefault(c => IsType(c.GetType(), type)) : null);
                return hit ?? throw new ArgumentException($"no {type} at {v}");
            }

            return PickRef(e != null && e.IsAsset ? e.FindInAsset(v) : ResolveScenePath(v), type, v);
        }

        private static Object PickRef(GameObject target, string type, string v)
        {
            if (type == "GameObject" || type == "Object") return target;
            return target.GetComponents<Component>().FirstOrDefault(c => c != null && (IsType(c.GetType(), type) || Matches(c, type)))
                   ?? throw new ArgumentException($"no {type} on {v}. Has: {string.Join(", ", target.GetComponents<Component>().Select(Label))}");
        }

        private static bool IsType(Type t, string name)
        {
            if (name == "Object") return true;
            for (; t != null; t = t.BaseType)
                if (t.Name == name) return true;
            return false;
        }

        // setmat asset=<.mat> | path=<renderer> [slot=0]; then _Prop=<value> ...
        private static string SetMaterial(Args a)
        {
            var mat = ResolveMaterial(a);
            var asset = AssetDatabase.GetAssetPath(mat);
            if (!asset.EndsWith(".mat")) throw new ArgumentException($"{mat.name} lives in {asset}, not a .mat; extract it first");
            var sh = mat.shader;
            var sb = new StringBuilder();
            if (sh.name.StartsWith("Hidden/Locked")) sb.AppendLine("WARNING: shader is locked; locked properties won't change the render until you unlock it");
            var pairs = a.Pairs.Where(kv => !SetMatReserved.Contains(kv.Key)).ToList();
            var missing = pairs.Where(kv => sh.FindPropertyIndex(kv.Key) < 0).Select(kv => kv.Key).ToList();
            if (missing.Count > 0) throw new ArgumentException($"{sh.name} has no property {string.Join(", ", missing)}");
            Undo.RecordObject(mat, "ub setmat");
            var any = false;
            foreach (var kv in pairs)
            {
                var type = sh.GetPropertyType(sh.FindPropertyIndex(kv.Key));
                var before = MatValue(mat, kv.Key, type);
                switch (type)
                {
                    case ShaderPropertyType.Color: mat.SetColor(kv.Key, ParseColor(kv.Value)); break;
                    case ShaderPropertyType.Vector: var n = Numbers(kv.Value, 1); mat.SetVector(kv.Key, new Vector4(n[0], n.ElementAtOrDefault(1), n.ElementAtOrDefault(2), n.ElementAtOrDefault(3))); break;
                    case ShaderPropertyType.Texture: mat.SetTexture(kv.Key, kv.Value == "null" ? null : AssetDatabase.LoadAssetAtPath<Texture>(kv.Value) ?? throw new ArgumentException($"no texture at {kv.Value}")); break;
                    case ShaderPropertyType.Int: mat.SetInteger(kv.Key, int.Parse(kv.Value, Inv)); break;
                    default: mat.SetFloat(kv.Key, float.Parse(kv.Value, Inv)); break;
                }
                sb.AppendLine($"{kv.Key}: {before} -> {MatValue(mat, kv.Key, type)}");
                any = true;
            }
            if (!any) throw new ArgumentException("nothing to set: pass _Property=<value> pairs (see `material` for names)");
            AssetDatabase.SaveAssetIfDirty(mat);
            return sb.AppendLine($"saved {asset}").ToString();
        }

        // create [path=<parent>] | asset= [path=]; name= [prefab=<asset>]
        private static string Create(Args a)
        {
            using (var e = Edit.From(a, "create", pathOptional: true))
            {
                var parent = e.Object != null ? e.Object.transform : e.DefaultParent;
                GameObject go;
                var prefab = a.Get("prefab");
                if (prefab != null)
                {
                    var src = AssetDatabase.LoadAssetAtPath<GameObject>(prefab) ?? throw new ArgumentException($"no prefab or model at {prefab}");
                    go = (GameObject)(parent != null ? PrefabUtility.InstantiatePrefab(src, parent) : PrefabUtility.InstantiatePrefab(src));
                    if (a.Get("name") is string n) go.name = n;
                }
                else
                {
                    go = new GameObject(a.Require("name"));
                    if (parent != null) go.transform.SetParent(parent, false);
                }
                if (!e.IsAsset) Undo.RegisterCreatedObjectUndo(go, "ub create");
                return e.Commit($"created {e.PathOf(go.transform)}\n");
            }
        }

        // delete path=|asset=
        private static string Delete(Args a)
        {
            using (var e = Edit.From(a, "delete"))
            {
                if (e.IsAsset && e.Object == e.Root) throw new ArgumentException("can't delete a prefab's root");
                var path = e.PathOf(e.Object.transform);
                if (e.IsAsset) Object.DestroyImmediate(e.Object);
                else Undo.DestroyObjectImmediate(e.Object);
                return e.Commit($"deleted {path}\n");
            }
        }

        // duplicate path=|asset= [name=]
        private static string Duplicate(Args a)
        {
            using (var e = Edit.From(a, "duplicate"))
            {
                var src = e.Object;
                if (e.IsAsset && src == e.Root) throw new ArgumentException("can't duplicate a prefab's root");
                GameObject dup;
                var note = "";
                if (e.IsAsset)
                {
                    dup = Object.Instantiate(src, src.transform.parent);
                    dup.name = GameObjectUtility.GetUniqueNameForSibling(src.transform.parent, src.name);
                    dup.transform.SetSiblingIndex(src.transform.GetSiblingIndex() + 1);
                    if (PrefabUtility.IsPartOfPrefabInstance(src)) note = " (prefab link not kept in asset mode)";
                }
                else
                {
                    // Same path as Edit > Duplicate: keeps prefab links and registers undo.
                    var previous = Selection.objects;
                    Selection.activeGameObject = src;
                    Unsupported.DuplicateGameObjectsUsingPasteboard();
                    dup = Selection.activeGameObject;
                    Selection.objects = previous;
                    if (dup == null || dup == src) throw new InvalidOperationException("duplicate produced nothing");
                }
                if (a.Get("name") is string n)
                {
                    if (!e.IsAsset) Undo.RecordObject(dup, "ub duplicate");
                    dup.name = n;
                }
                return e.Commit($"duplicated to {e.PathOf(dup.transform)}{note}\n");
            }
        }

        // reparent path=|asset= to=<new parent, empty for root> [keepworld=1] [sibling=<n>]
        private static string Reparent(Args a)
        {
            using (var e = Edit.From(a, "reparent"))
            {
                var t = e.Object.transform;
                if (e.IsAsset && e.Object == e.Root) throw new ArgumentException("can't reparent a prefab's root");
                var to = a.Get("to") ?? throw new ArgumentException("missing argument 'to' (empty for the root)");
                var parent = to == "" ? e.DefaultParent : (e.IsAsset ? e.FindInAsset(to) : ResolveScenePath(to)).transform;
                if (parent != null && parent.IsChildOf(t)) throw new ArgumentException("can't parent an object under itself");
                var keepWorld = a.Bool("keepworld", true);
                if (e.IsAsset) t.SetParent(parent, keepWorld);
                else
                {
                    var (pos, rot, scale) = (t.localPosition, t.localRotation, t.localScale);
                    Undo.SetTransformParent(t, parent, "ub reparent"); // keeps world position
                    if (!keepWorld)
                    {
                        Undo.RecordObject(t, "ub reparent");
                        t.localPosition = pos;
                        t.localRotation = rot;
                        t.localScale = scale;
                    }
                }
                if (a.Get("sibling") != null)
                {
                    if (!e.IsAsset && t.parent != null) Undo.RecordObject(t.parent, "ub reparent");
                    t.SetSiblingIndex(a.Int("sibling", 0));
                }
                return e.Commit($"moved to {e.PathOf(t)} (sibling {t.GetSiblingIndex()})\n");
            }
        }

        // addcomp path=|asset= type=<Component type> [feature=<VRCFury feature>]
        private static string AddComponent(Args a)
        {
            using (var e = Edit.From(a, "addcomp"))
            {
                var name = a.Require("type");
                var hits = TypeCache.GetTypesDerivedFrom<Component>().Where(t => !t.IsAbstract && (t.Name == name || t.FullName == name)).ToList();
                if (hits.Count == 0) throw new ArgumentException($"no Component type '{name}'");
                if (hits.Count > 1) throw new ArgumentException($"'{name}' is ambiguous: {string.Join(", ", hits.Select(t => t.FullName))}");
                var c = e.IsAsset ? e.Object.AddComponent(hits[0]) : Undo.AddComponent(e.Object, hits[0]);
                if (c == null) throw new InvalidOperationException($"Unity refused to add {name} (duplicate of a single-instance component?)");
                if (a.Get("feature") is string feature)
                {
                    var so = new SerializedObject(c);
                    var content = so.FindProperty("content") ?? throw new ArgumentException($"{name} has no 'content' field for feature=");
                    Assign(content, "new:" + feature, e);
                    so.ApplyModifiedPropertiesWithoutUndo(); // the component was just created inside this undo group
                }
                return e.Commit($"added {Label(c)} to {e.PathOf(e.Object.transform)}\n");
            }
        }

        // rmcomp path=|asset= component= [index=]
        private static string RemoveComponent(Args a)
        {
            using (var e = Edit.From(a, "rmcomp"))
            {
                var c = PickComponent(e.Object, a.Require("component"), a.Get("index"));
                if (c is Transform) throw new ArgumentException("can't remove a Transform");
                var label = Label(c);
                if (e.IsAsset) Object.DestroyImmediate(c);
                else Undo.DestroyObjectImmediate(c);
                if (c != null) throw new InvalidOperationException($"Unity kept {label}; another component probably requires it (see `ub logs`)");
                return e.Commit($"removed {label} from {e.PathOf(e.Object.transform)}\n");
            }
        }

        // apply|revert path=|asset= [component= [index=]] [prop=] [to=<asset>]
        private static string ApplyOrRevert(Args a, bool apply)
        {
            using (var e = Edit.From(a, apply ? "apply" : "revert"))
            {
                var go = e.Object;
                var addedGo = PrefabUtility.IsAddedGameObjectOverride(go); // an added object isn't "part of" the instance
                if (!addedGo && !PrefabUtility.IsPartOfPrefabInstance(go)) throw new ArgumentException($"{go.name} is not part of a prefab instance");
                var mode = e.IsAsset ? InteractionMode.AutomatedAction : InteractionMode.UserAction;
                var to = a.Get("to");
                // Captured first: go itself may be gone after a revert. An added object's instance is its parent's.
                var outermost = PrefabUtility.GetOutermostPrefabInstanceRoot(addedGo ? go.transform.parent.gameObject : go);
                var asset = to ?? AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(outermost)); // like Unity's Overrides dropdown
                var comp = a.Get("component");
                var prop = a.Get("prop");
                var path = e.PathOf(go.transform);
                string scope;

                if (addedGo)
                {
                    if (apply) PrefabUtility.ApplyAddedGameObject(go, asset, mode);
                    else PrefabUtility.RevertAddedGameObject(go, mode);
                    scope = "added GameObject";
                }
                else if (prop != null)
                {
                    Object target = comp == null || comp == "GameObject" ? (Object)go : PickComponent(go, comp, a.Get("index"));
                    var p = new SerializedObject(target).FindProperty(prop) ?? throw new ArgumentException($"no property '{prop}' on {Describe(target)}");
                    if (!p.prefabOverride) throw new ArgumentException($"{prop} has no override");
                    if (apply) PrefabUtility.ApplyPropertyOverride(p, asset, mode);
                    else PrefabUtility.RevertPropertyOverride(p, mode);
                    scope = $"property {prop}";
                }
                else if (comp != null)
                {
                    var c = PickComponent(go, comp, a.Get("index"));
                    var added = PrefabUtility.IsAddedComponentOverride(c);
                    if (added && apply) PrefabUtility.ApplyAddedComponent(c, asset, mode);
                    else if (added) PrefabUtility.RevertAddedComponent(c, mode);
                    else if (apply) PrefabUtility.ApplyObjectOverride(c, asset, mode);
                    else PrefabUtility.RevertObjectOverride(c, mode);
                    scope = (added ? "added component " : "component ") + Label(c);
                }
                else if (PrefabUtility.IsOutermostPrefabInstanceRoot(go) && to == null)
                {
                    if (apply) PrefabUtility.ApplyPrefabInstance(go, mode);
                    else PrefabUtility.RevertPrefabInstance(go, mode);
                    scope = "whole instance";
                }
                else
                {
                    // The object and its components: property overrides, then added components.
                    var mine = PrefabUtility.GetObjectOverrides(outermost)
                        .Where(o => o.instanceObject == go || (o.instanceObject is Component oc && oc.gameObject == go)).ToList();
                    var addedComps = PrefabUtility.GetAddedComponents(outermost).Where(ac => ac.instanceComponent.gameObject == go).ToList();
                    if (mine.Count == 0 && addedComps.Count == 0) throw new ArgumentException($"{go.name} has no overrides of its own (see `overrides`)");
                    foreach (var o in mine)
                        if (apply) o.Apply(asset, mode);
                        else o.Revert(mode);
                    foreach (var ac in addedComps)
                        if (apply) ac.Apply(asset, mode);
                        else ac.Revert(mode);
                    scope = $"{mine.Count} overridden and {addedComps.Count} added objects";
                }
                var remaining = PrefabUtility.GetPropertyModifications(outermost)?.Count(m => !PrefabUtility.IsDefaultOverride(m)) ?? 0;
                var line = apply ? $"applied {scope} of {path} to {asset}" : $"reverted {scope} of {path}";
                return e.Commit($"{line} ({remaining} property overrides left on the instance)\n");
            }
        }

        // menu item=<menu path>
        private static string RunMenu(Args a)
        {
            var item = a.Require("item");
            if (!EditorApplication.ExecuteMenuItem(item)) throw new ArgumentException($"menu item '{item}' doesn't exist or is disabled (some need a selection: use `select` first)");
            return $"ran {item}; check `ub logs` for its output\n";
        }

        // save [scene=<path>]
        private static string Save(Args a)
        {
            var sb = new StringBuilder();
            var only = a.Get("scene");
            if (only != null)
            {
                var s = SceneManager.GetSceneByPath(only);
                if (!s.IsValid() || !s.isLoaded) throw new ArgumentException($"{only} is not open");
                if (!EditorSceneManager.SaveScene(s)) throw new InvalidOperationException($"saving {only} failed");
                sb.AppendLine($"saved {only}");
            }
            else
            {
                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage != null && stage.scene.isDirty)
                {
                    PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, stage.assetPath, out var ok);
                    if (!ok) throw new InvalidOperationException($"saving prefab stage {stage.assetPath} failed");
                    stage.ClearDirtiness();
                    sb.AppendLine($"saved {stage.assetPath} (prefab stage)");
                }
                for (var i = 0; i < SceneManager.sceneCount; i++)
                {
                    var s = SceneManager.GetSceneAt(i);
                    if (!s.isLoaded || !s.isDirty) continue;
                    if (string.IsNullOrEmpty(s.path)) { sb.AppendLine("skipped an untitled scene (save it once by hand)"); continue; }
                    if (!EditorSceneManager.SaveScene(s)) throw new InvalidOperationException($"saving {s.path} failed");
                    sb.AppendLine($"saved {s.path}");
                }
            }
            AssetDatabase.SaveAssets();
            return sb.Length == 0 ? "nothing dirty; assets flushed\n" : sb.ToString();
        }

        // open scene=<path> [additive=1] [force=1]
        private static string Open(Args a)
        {
            var path = a.Require("scene");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) throw new ArgumentException($"no scene at {path}");
            var additive = a.Bool("additive", false);
            if (!a.Bool("force", false))
            {
                var dirty = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Where(s => s.isDirty).Select(s => s.path == "" ? "(untitled)" : s.path).ToList();
                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage != null && stage.scene.isDirty) dirty.Add(stage.assetPath + " (prefab stage)");
                if (dirty.Count > 0 && !additive) throw new ArgumentException($"unsaved changes in {string.Join(", ", dirty)}; `save` first or pass force=1 to discard them");
            }
            StageUtility.GoToMainStage();
            var opened = EditorSceneManager.OpenScene(path, additive ? OpenSceneMode.Additive : OpenSceneMode.Single);
            return $"opened {opened.path}{(additive ? " (additive)" : "")}\n";
        }

        // play [action=status|enter|exit|pause|resume] — enter and exit take effect after this answer is written.
        private static string Play(Args a)
        {
            var action = a.Get("action", "status");
            switch (action)
            {
                case "status": break;
                case "enter":
                    if (EditorApplication.isPlaying) return "already playing\n";
                    EditorApplication.isPlaying = true;
                    return "entering play mode; scripts may reload, then `ub play` shows playing=True\n";
                case "exit":
                    if (!EditorApplication.isPlaying) return "not playing\n";
                    EditorApplication.isPlaying = false;
                    return "leaving play mode; scene changes made while playing are discarded\n";
                case "pause": EditorApplication.isPaused = true; break;
                case "resume": EditorApplication.isPaused = false; break;
                default: throw new ArgumentException($"unknown action '{action}': status, enter, exit, pause, resume");
            }
            return $"playing={EditorApplication.isPlaying} paused={EditorApplication.isPaused} changing={EditorApplication.isPlayingOrWillChangePlaymode != EditorApplication.isPlaying}\n";
        }

        // --- helpers ---

        private static bool IsPrefab(string asset) => asset.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);

        private static Component PickComponent(GameObject go, string name, string index)
        {
            var hits = go.GetComponents<Component>().Where(c => Matches(c, name)).ToList();
            if (hits.Count == 0) throw new ArgumentException($"no {name} on {go.name}. Has: {string.Join(", ", go.GetComponents<Component>().Select(Label))}");
            if (index != null) return hits.ElementAtOrDefault(int.Parse(index)) ?? throw new ArgumentException($"index {index} out of range ({hits.Count} matches)");
            if (hits.Count > 1) throw new ArgumentException($"{hits.Count} components match '{name}' on {go.name}: {string.Join(", ", hits.Select((c, i) => $"[{i}] {Label(c)}"))}; pass index=");
            return hits[0];
        }

        // The object being written, in the open scene (with Undo) or inside a prefab asset's loaded contents.
        private sealed class Edit : IDisposable
        {
            public GameObject Object;
            public GameObject Root; // prefab contents root in asset mode
            public bool IsAsset => Root != null;
            private string _asset;
            public string AssetPath => _asset;
            private Scene _scene;
            private int _group;

            public static Edit From(Args a, string action, bool pathOptional = false)
            {
                var e = new Edit();
                var asset = a.Get("asset");
                if (asset == null)
                {
                    if (!(pathOptional && a.Get("path") == null)) e.Object = ResolveScenePath(a.Require("path"));
                    var stage = PrefabStageUtility.GetCurrentPrefabStage();
                    e._scene = e.Object != null ? e.Object.scene : stage != null ? stage.scene : SceneManager.GetActiveScene();
                    Undo.IncrementCurrentGroup();
                    Undo.SetCurrentGroupName("ub " + action);
                    e._group = Undo.GetCurrentGroup();
                    return e;
                }
                if (!IsPrefab(asset)) throw new ArgumentException($"{asset} is not a .prefab; only prefab assets can be edited this way");
                if (AssetDatabase.LoadAssetAtPath<GameObject>(asset) == null) throw new ArgumentException($"no prefab at {asset}");
                e._asset = asset;
                e.Root = PrefabUtility.LoadPrefabContents(asset);
                try
                {
                    e.Object = e.FindInAsset(a.Get("path", ""));
                }
                catch
                {
                    e.Dispose();
                    throw;
                }
                return e;
            }

            // Where a parentless create or `to=` lands: the prefab root in asset mode or an open prefab stage, else the scene root.
            public Transform DefaultParent => IsAsset ? Root.transform : PrefabStageUtility.GetCurrentPrefabStage()?.prefabContentsRoot.transform;

            public GameObject FindInAsset(string path)
            {
                if (path.StartsWith("./")) path = path.Substring(2);
                if (path == "" || path == ".") return Root;
                var t = Root.transform.Find(path);
                return t != null ? t.gameObject : throw new ArgumentException($"'{path}' not found inside {_asset}");
            }

            public string PathOf(Transform t) => IsAsset ? RelPath(Root.transform, t) : ScenePath(t);

            public string Commit(string result)
            {
                if (IsAsset)
                {
                    PrefabUtility.SaveAsPrefabAsset(Root, _asset, out var ok);
                    if (!ok) throw new InvalidOperationException($"saving {_asset} failed; see `ub logs`");
                    return result + $"saved {_asset}\n";
                }
                Undo.CollapseUndoOperations(_group);
                if (EditorApplication.isPlaying) return result + "WARNING: play mode; scene changes are discarded when it ends\n";
                if (_scene.IsValid() && !EditorSceneManager.IsPreviewScene(_scene)) EditorSceneManager.MarkSceneDirty(_scene);
                return result;
            }

            public void Dispose()
            {
                if (Root != null) PrefabUtility.UnloadPrefabContents(Root);
                Root = null;
            }
        }
    }
}
