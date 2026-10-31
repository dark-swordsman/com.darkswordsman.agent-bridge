# Spec: write commands (0.2.0)

Goal: the bridge covers the edits agents actually make on avatar projects, so mcp-unity becomes optional. Same transport, same `key=value` arguments, same plain-text answers.

## Principles

- **One generic setter instead of one tool per field.** `set` writes any serialized property by path, so it covers transforms, VRCFury features, and avatar descriptor fields alike. `props` output can be fed back to it: values print in a format `set` accepts.
- **Two targets, one behavior.** `path=` edits the open scene (or the open prefab stage). `asset=` (optionally plus `path=` inside it) edits a prefab asset: the bridge loads its contents, edits them, saves them, and unloads them.
- **Scene edits are undoable** (Ctrl+Z in Unity) and only mark the scene dirty. Nothing is saved until `save`. Prefab asset edits save immediately and can't be undone with Ctrl+Z, which matches what Unity does with `LoadPrefabContents`.
- **Every write reports what changed**, e.g. `content.name: "Old" -> "New"`, so the agent doesn't need a follow-up read.
- **Fail loudly.** A missing object, component, or property is an `ERROR:` that lists what does exist. Nothing reports success after doing nothing.

## Commands

| Command | Arguments | Does |
|---|---|---|
| `set` | target, `component=`, `[index=]`, then `<propertyPath>=<value>` … | Sets serialized properties, in the order given. Every key that isn't `path`, `asset`, `component` or `index` is a property path. `component=GameObject` targets the GameObject itself (`m_IsActive`, `m_Name`, `m_Layer`). |
| `setmat` | `asset=<.mat>` or `path=<renderer>` `[slot=]`, then `_Prop=<value>` … | Sets shader properties through the Material API. It warns when the shader is locked (Poiyomi), because locked properties don't change the render. |
| `create` | `[path=<parent>]` or `asset=` + `[path=]`, `name=`, `[prefab=<asset>]` | Creates an empty GameObject, or a linked prefab instance, under the parent (scene root if no parent). |
| `delete` | target | Deletes the object. |
| `duplicate` | target `[name=]` | Duplicates the object next to the original. Prefab links are kept. |
| `reparent` | target `to=<new parent path or "">` `[keepworld=0]` `[sibling=<n>]` | Moves the object under a new parent (`to=` empty moves it to the scene or prefab root). Keeps the world position unless `keepworld=0`. |
| `addcomp` | target `type=<Component type>` `[feature=<VRCFury feature>]` | Adds a component. `feature=` also sets the new VRCFury component's `content` to that feature. |
| `rmcomp` | target `component=` `[index=]` | Removes a component. |
| `apply` / `revert` | target `[component=]` `[prop=]` `[to=<asset>]` | Prefab overrides. On an instance root with no other arguments, the whole instance. On any other object, the overrides of that object and its components; with `component=`, that component (including an added component); with `prop=`, that one property. An added GameObject is applied or reverted as a whole. `apply` writes to the outermost prefab asset (as Unity's Overrides dropdown does) unless `to=` names another one in the chain. |
| `menu` | `item=<menu path>` | Runs a menu item. Fails if the item doesn't exist or is disabled. The CLI's default timeout for `menu` is 600 s. |
| `save` | `[scene=<path>]` | Saves dirty open scenes (or just that scene), the open prefab stage, and dirty assets. |
| `open` | `scene=<path>` `[additive=1]` `[force=1]` | Opens a scene. It refuses while a scene has unsaved changes, unless `force=1` (which discards them). |

`set` and `props` also work on a non-prefab asset (`asset=x.asset`, `.controller`, …): they edit or read the asset itself, and `sub=` picks a sub-asset by name. These edits save immediately.

"target" means `path=` (a scene path) or `asset=` with an optional `path=` inside the asset, as in the read commands.

## Values

Parsed by the property's type:

- Numbers, `true`/`false`/`1`/`0`, and strings, raw. Write `""` for an empty string.
- Enums: name, display name, or index.
- Colors and vectors: any list of numbers, so `1,0,0,1`, `(1, 0, 0)` and `RGBA(1.000, 0.000, 0.000, 1.000)` all work. A color can also be `#RRGGBB` or `#RRGGBBAA`. Quaternions take Euler angles (`euler(0, 90, 0)` or `0,90,0`).
- Object references:
  - `null`.
  - An asset path, `Assets/x.mat`.
  - A sub-asset, `Assets/x.fbx :: Body`.
  - A scene path, `Root/Child`.
  - In asset mode, a path inside the prefab, `./Child`.
  - Any of these can end with ` (Type)` to pick a component or sub-asset of that type. That's the format `props` prints.
- Managed references (VRCFury): `new:<TypeName>` creates a fresh instance of the type with that name that fits the field. Use `null` to clear one.
- Arrays: set `foo.Array.size` first, then the elements `foo.Array.data[i]…`. Properties are applied in the order given, so both can go in one call.

## Out of scope for 0.2.0

- `eval`: compiling a snippet means a domain reload per call. A one-shot script plus `menu` already covers this in two calls.
- Reverting individual removed components or GameObjects: revert the whole instance instead.
- Creating assets (materials, controllers) or editing animator controllers.

## Publishing checklist

- [x] Settle the name (kept `com.darkswordsman.agent-bridge`) and repo; add a LICENSE (MIT) and a CHANGELOG.
- [x] Add **Tools > Agent Bridge > Copy ub Path**, for git installs under `Library/PackageCache/`.
- [ ] Have someone confirm Windows, especially polling while Unity is unfocused.
- [x] Make the repo public.
