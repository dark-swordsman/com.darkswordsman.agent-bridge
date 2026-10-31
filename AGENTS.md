# Notes for coding agents

You're working on Agent Bridge itself, not using it. Read `.github/CONTRIBUTING.md` first. It covers the layout, how to add a command, testing, and the platform rules. The essentials:

- **Target:** Unity 2022.3. No newer APIs, and no dependencies beyond Unity's editor API and the Python standard library.
- **Platforms:** don't break other platforms. Platform-specific code goes behind `#if UNITY_EDITOR_WIN` (and friends) or `sys.platform`.
- **Code style:** match the surrounding code. That means terse comments on the why, one method per command, `ArgumentException` for user errors with a message that lists what does exist, and output that is short plain text.
- **Writes:** every write goes through `Edit.From(...)` / `e.Commit(...)` and reports `old -> new`.
- **Testing:** test against a real open editor with `Tools~/ub`, on throwaway `_test` objects and assets only. Discard the scene changes afterwards (`ub open scene=… force=1`). Never edit or save a user's real scene or prefab while testing.
- **Keep in sync:** when you add or change a command, update the README, the docstring at the top of `Tools~/ub`, the `unity` MCP tool description in `Tools~/ub`, and the CHANGELOG (**Unreleased**).
- **Commit `.meta` files** for new files under `Editor/` and at the root.
