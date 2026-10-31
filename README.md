# Agent Bridge

A small Unity editor package that lets coding agents (Claude Code, Codex, Cursor, …) read and edit the open editor. Any agent with a shell can use the `ub` CLI, and there's an optional MCP server for clients without one. Answers are short plain text with a capped preview; the full output stays on disk to grep.

It's a general Unity tool, built and tested on **Unity 2022.3** for **VRChat** projects. Integrations for VRCFury and Poiyomi light up when those packages are present and stay out of the way otherwise.

## ⚠️ Risks: read this first

**This is vibe coded slop, use at your own risk.** More concretely:

- **Edits happen without confirmation.** `ub` changes the open editor directly. Scene edits can be undone (one Undo step per command) and stay unsaved until `ub save`. **Prefab, asset and material edits save to disk immediately.** Commit to git before you let an agent loose.
- **`menu` runs any menu item,** which in practice means arbitrary editor code, including from other packages.
- **No authentication.** Anything that can write a file into `<project>/Temp/AgentBridge/in/` can drive the editor while it's open. That's no more than local file access already allows, but know it's there. Nothing listens on the network.
- **Your agent's permission settings are the only gate.** Keep per-command approval on (for the shell or the MCP tool) until you trust what it does.
- Tested on Linux with Unity 2022.3. **Windows is written for but not yet confirmed**; see [issue #1](https://github.com/dark-swordsman/com.darkswordsman.agent-bridge/issues/1).

## 🤔 Why not just a Unity MCP server?

MCP results are large and stay in the agent's context for the rest of the session. A scene hierarchy can be 50 KB+, and a Poiyomi material has hundreds of properties. Existing tools also often can't see inactive objects, can't read or write a single serialized property, and fail across script reloads. The bridge answers narrow questions, caps what it prints, and waits out recompiles instead of failing.

## 📦 Install

Pick one:

- **From git:** in Package Manager, use **+ > Add package from git URL…** with `https://github.com/dark-swordsman/com.darkswordsman.agent-bridge.git#v0.3.1`. The `#v0.3.1` pins a release; without it you get `main`, which may hold unreleased changes.
- **From disk:** clone the repo somewhere outside your project, then use **+ > Add package from disk…** and pick its `package.json`.

The CLI is `Tools~/ub`, written in Python 3.7+ with no dependencies. **Tools > Agent Bridge > Copy ub Path** copies its location; with a git install it lives under `Library/PackageCache/`. Run it from anywhere inside the project, or pass `--project DIR`. On Windows, run `Tools~\ub.cmd` (the menu item copies that path there), which starts the script with `py` or `python`.

## 🔍 Reads

Each command's general form comes first, with its examples indented below it. `<x>` is a value you fill in, `[key=value]` is optional and shows the default, and `a | b` means one or the other. `<target>` is `path=<scene path>`, or `asset=<prefab> [path=<child path>]` to work inside a prefab asset. `<type>` is a component type or a VRCFury feature name.

```
ub ping                                                  # Unity state, open scenes, selection

ub find name=<substring> [component=<type>] [limit=200]  # scene paths, inactive objects included
    ub find name=Hoodie

ub hierarchy <target> [depth=2] [components=1]
    ub hierarchy path=Avatar depth=1
    ub hierarchy asset="Assets/Avatar/Avatar.prefab" depth=1

ub props <target> [component=<type>] [index=<n>] [filter=<substring>] [limit=400]
    ub props path=Avatar/Hoodie component=Toggle         # VRCFury features match by feature type
    ub props path=Avatar/Body component=SkinnedMeshRenderer filter=m_Materials

ub props asset=<non-prefab asset> [sub=<sub-asset name>] [filter=<substring>] [limit=400]  # plain assets work too
    ub props asset="Assets/Avatar/Parameters.asset"

ub material (asset=<.mat> | <target> [slot=0]) [prefix=<property prefix>] [keywords=0]
    ub material asset="Assets/Avatar/Body.mat" prefix=_Color
    ub material path=Avatar/Body slot=0 prefix=_UDIM

ub mesh <target> [shapes=<substring>]                    # blendshapes, UV tiles they move, bones
    ub mesh path=Avatar/Hoodie shapes=Hide

ub overrides <target> [filter=<substring>] [limit=200] [defaults=0]  # prefab instance overrides
    ub overrides path=Avatar

ub logs [-g <regex>] [--all] [--stack] [--raw]           # console messages since the last `ub logs`
    ub logs
    ub logs -g "error|exception"
```

## ✏️ Writes

The full design is in `Documentation~/write-commands.md`.

```
ub set <target> component=<type> [index=<n>] <propertyPath>=<value> ...
    ub set path=Avatar/Hoodie component=Transform m_LocalPosition=0,0.1,0
    ub set path=Avatar/Hoodie component=GameObject m_IsActive=false
    ub set path=Avatar/Hoodie component=Toggle content.name="Clothing/Hoodie" content.saved=true
    ub set path=Avatar/Hoodie component=Toggle content.state.actions.Array.size=1 "content.state.actions.Array.data[0]=new:BlendShapeAction"
    ub set asset="Assets/Avatar/Avatar.prefab" path=Body component=SkinnedMeshRenderer "m_Materials.Array.data[0]=Assets/Avatar/Body.mat"

ub set asset=<non-prefab asset> [sub=<sub-asset name>] <propertyPath>=<value> ...
    ub set asset="Assets/Avatar/Parameters.asset" "parameters.Array.data[3].saved=false"

ub setmat (asset=<.mat> | <target> [slot=0]) <_Property>=<value> ...
    ub setmat asset="Assets/Avatar/Body.mat" _Color="#ff8080" _Cutoff=0.5

ub create [<target>] name=<name> [prefab=<prefab>]       # <target> is the parent; none means the scene root
    ub create path=Avatar name=Props                     # or prefab=Assets/x.prefab for a linked instance

ub duplicate <target> [name=<name>]
    ub duplicate path=Avatar/Hoodie name="Hoodie 2"

ub reparent <target> to=<new parent path> [keepworld=1] [sibling=<n>]  # empty to= moves it to the root
    ub reparent path=Avatar/Props to=Avatar/Armature/Hips  # keeps world position unless keepworld=0

ub addcomp <target> type=<component type> [feature=<VRCFury feature>]
    ub addcomp path=Avatar/Props type=VRCFury feature=Toggle

ub rmcomp <target> component=<type> [index=<n>]
    ub rmcomp path=Avatar/Props component=BoxCollider

ub delete <target>
    ub delete path=Avatar/Props

ub revert <target> [component=<type> [index=<n>]] [prop=<propertyPath>] [to=<prefab>]
    ub revert path=Avatar/Body component=SkinnedMeshRenderer "prop=m_Materials.Array.data[0]"

ub apply <target> [component=<type> [index=<n>]] [prop=<propertyPath>] [to=<prefab>]
    ub apply path=Avatar/Body                            # that object's and its components' overrides

ub select path=<scene path>                              # works on inactive objects
    ub select path=Avatar

ub menu item=<menu path>                                 # waits up to 600 s
    ub menu item="Tools/VRCFury/Build an Editor Test Copy"

ub refresh                                               # AssetDatabase.Refresh

ub save [scene=<scene path>]                             # dirty scenes, the prefab stage, and assets
    ub save

ub open scene=<scene path> [additive=0] [force=0]        # refuses with unsaved changes unless force=1
    ub open scene="Assets/Scenes/Main.unity"

ub play [enter|exit|pause|resume]                        # alone, shows play mode status
    ub play enter
```

- `set` takes `<propertyPath>=<value>` pairs and applies them in order. It accepts the same paths and value formats that `props` prints, so you can read a value and write it back. `component=GameObject` targets the GameObject itself. On a non-prefab asset (`.asset`, `.controller`, …), `set` edits the asset itself, and `sub=` picks a sub-asset by name.
- Scene edits are undoable, one Undo step per command (`ub menu item=Edit/Undo` works). They only dirty the scene until `save`. Edits with `asset=<.prefab>` or on plain assets, and `setmat`, save the asset immediately, so commit first.
- Each write prints what changed (`old -> new`). Errors list what does exist: similar property paths, the components on the object, the types that fit a managed reference.

## 🔌 MCP

For clients without a shell, or if you prefer MCP tools, `ub mcp` serves the same commands as an MCP server over stdio. It has no dependencies; it's the same Python file. With Claude Code:

```
claude mcp add unity -- python3 /path/to/Tools~/ub mcp --project /path/to/YourProject
```

On Windows, use `py` instead of `python3`, since `python3` there often opens the Microsoft Store.

There are two tools:
- `unity(command, args, max, timeout)` runs any command above. For example, `{"command": "set", "args": {"path": "Avatar/Hoodie", "component": "Transform", "m_LocalPosition": "0,0.1,0"}}`.
- `unity_logs(grep, stack, all, raw)` reads the console.

It deliberately exposes one generic tool rather than one per command, because every tool's schema costs context in every session. Answers are capped the same way as the CLI's. The risks above apply equally; your client's tool-approval setting is the gate.

## ⚙️ General

- `path=` is a scene path whose first segment is a root object. With `asset=`, it's a path inside that prefab (empty means the root).
- Quote any argument containing `[ ]` or spaces. zsh treats brackets as a glob pattern and fails.
- `--max N` caps the printed preview (6000 characters by default). The full answer stays in `Temp/AgentBridge/out/`, so it can be grepped.
- `--timeout S` defaults to 30 (600 for `menu`). Requests wait while Unity compiles or imports. A request nobody picked up is removed when it times out.
- Exit codes:
  - 0: success
  - 1: `ERROR: …` answer
  - 2: usage error
  - 3: timeout

## 📝 Notes

- How it works: `ub` writes `Temp/AgentBridge/in/<id>.req`, a command line followed by `key=value` lines. `Editor/AgentBridge.cs` polls that folder every 0.25 s from `EditorApplication.update` and writes `out/<id>.txt`. There's no network and no open port.
- **Unfocused editor:** on Linux, the update loop keeps running while Unity is in the background. On Windows, Unity may throttle it. If requests hang until you click into Unity, that's why. A Win32 timer calling `EditorApplication.QueuePlayerLoopUpdate()` (as mcp-unity's `McpBackgroundTick.cs` does) would fix it. Reports welcome.
- `logs` reads Editor.log directly and never talks to Unity. The log is in `~/.config/unity3d/` on Linux, `%LOCALAPPDATA%\Unity\Editor\` on Windows and `~/Library/Logs/Unity/` on macOS; override with `UB_EDITOR_LOG`.
  - By default it prints only console messages (the line before each `(Filename: …)` marker) and compiler `error CS`/`warning CS` lines, and filters out mcp-unity's request echo.
  - `--stack` adds stack frames, `--raw` prints every log line, and `--all` starts from the top of the log rather than the last `ub logs`.
- `overrides` hides default overrides (root name and position) unless `defaults=1`.

## 🧩 Integrations

These activate only when the package is present:

- **VRCFury:** `component=Toggle` (or any feature type) matches VRCFury components by feature. `addcomp type=VRCFury feature=Toggle` creates one, and `set` reaches into feature content, including `new:<ActionType>` for action lists. This reads VRCFury's serialized `content` field; if a VRCFury update renames it, feature matching stops and plain type names keep working.
- **Poiyomi:** `material` flags locked shaders, `setmat` warns before editing one, and `mesh` names UV tiles the way Poiyomi's UDIM discard does.

An extension point, so other packages can add their own commands without editing the core, is planned; see [issue #2](https://github.com/dark-swordsman/com.darkswordsman.agent-bridge/issues/2).

## 🛠️ Extending

Add a read command in `Editor/Commands.cs`, or a write command in `Editor/WriteCommands.cs`. Each needs one entry in `Commands.All` and one method that takes `Args` and returns text. Write commands go through the `Edit` helper, which handles Undo, prefab-asset loading and saving. Throw `ArgumentException` for user errors; the message becomes the `ERROR:` line.

## 🤝 Contributing

Fork the repo and open a PR against `main`. See `.github/CONTRIBUTING.md`; coding agents should also read `AGENTS.md`.

## 📄 License

MIT. See `LICENSE.md`.
