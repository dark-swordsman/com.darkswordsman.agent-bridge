# Contributing

Thanks for helping. This is a small hobby project, so the process is light.

## Workflow

Every change comes in as a pull request from a fork, including from people the maintainer knows. The repo doesn't add collaborators, so nobody else pushes branches here.

1. Fork the repo and create a branch in your fork:
   ```
   gh repo fork dark-swordsman/com.darkswordsman.agent-bridge --clone
   cd com.darkswordsman.agent-bridge
   git switch -c my-change
   ```
2. Make one focused change per pull request. A new command, a platform fix and a refactor are three PRs.
3. Push the branch to your fork and open a PR against `main` here (`gh pr create`). The template asks what you changed and what you tested it on. Leave **Allow edits by maintainers** on, so small fixes can be made on your branch directly.
4. Before starting new work, sync your fork's `main` (`gh repo sync`, or **Sync fork** on GitHub).

For anything bigger than a fix, open an issue first so we can agree on the shape.

## Ground rules

- **Target:** Unity 2022.3 (what VRChat uses). Don't use APIs newer than that.
- **Don't break other platforms.** It's developed on Linux, and Windows support is in progress ([issue #1](https://github.com/dark-swordsman/com.darkswordsman.agent-bridge/issues/1)). Put platform-specific code behind `#if UNITY_EDITOR_WIN` / `UNITY_EDITOR_LINUX` / `UNITY_EDITOR_OSX` in C#, and `sys.platform` checks in Python. Say in the PR which platforms you actually ran it on.
- **No dependencies.** The C# side uses only Unity's editor API. `Tools~/ub` is a single Python 3.7+ file using only the standard library.
- **Small output.** The point of the bridge is to keep agents' context small. Commands print short plain text, filter by default, and offer `filter=`/`limit=` rather than dumping everything.
- **Fail loudly.** User mistakes throw `ArgumentException`; the message becomes the `ERROR:` line. It should name what *does* exist, such as similar paths or available components. Never report success after doing nothing.

## Layout

| Path | What |
|---|---|
| `Editor/AgentBridge.cs` | Polls `Temp/AgentBridge/in/`, dispatches, writes `out/`. `Args` parsing. |
| `Editor/Commands.cs` | Read commands and shared helpers (path resolution, `Describe`, `Value`). |
| `Editor/WriteCommands.cs` | Write commands and the `Edit` helper (Undo, prefab-asset load/save). |
| `Tools~/ub` | CLI, `ub logs` (reads Editor.log) and `ub mcp` (stdio MCP server). |
| `Documentation~/write-commands.md` | Design of the write commands. |

## Adding a command

1. Add one entry to `Commands.All` and one method that takes `Args` and returns text. A read goes in `Commands.cs` and a write in `WriteCommands.cs`.
2. Writes go through `Edit.From(...)` and end with `e.Commit(...)`. That gives you Undo in scenes and load/save for `asset=<.prefab>`. Report what changed as `old -> new`.
3. Add a usage line to the README, add the command to the lists in `ub`'s docstring and its MCP tool description, and add a CHANGELOG entry under **Unreleased**.

## Testing

There are no automated tests yet. Before opening a PR, run the commands you touched against a real project. Use throwaway objects and assets (`_test` names), never your real work, and discard the changes afterwards. Commit first; `ub open scene=… force=1` throws away scene changes.

A quick smoke test for any change to the transport or the CLI:

```
ub ping                                   # Unity in the background, too
ub find name=<an inactive object>
ub create name=_test && ub set path=_test component=Transform m_LocalPosition=1,2,3
ub menu item=Edit/Undo                    # position goes back
ub delete path=_test
ub play enter ; ub play ; ub play exit
ub logs
python3 ub mcp   (send initialize + tools/list + one tools/call; see the README)
```

## .meta files

Unity generates a `.meta` file for every new file under `Editor/` (and for root files such as `README.md`), and packages installed from git need them committed. Folders ending in `~` (`Tools~`, `Documentation~`) and dot-folders (`.github`) are ignored by Unity and need none.

## Releases

Don't bump the version or add tags in a PR; just add your CHANGELOG entry under **Unreleased**. Merged changes go out in the next release, which the maintainer cuts following `Documentation~/releasing.md`. Versions are pre-1.0, so a minor bump may change behavior.
