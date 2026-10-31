# Changelog

## Unreleased

## 0.3.1 (2026-10-31)

- Windows: `Tools~/ub.cmd` launcher, which **Copy ub Path** now copies on Windows. Not yet confirmed on Windows ([#1](https://github.com/dark-swordsman/com.darkswordsman.agent-bridge/issues/1)).
- Retry writing an answer when the file is briefly locked (e.g. by antivirus), instead of dropping it.
- README: the install URL pins the release tag by default.

## 0.3.0 (2026-10-31)

First public release.

- Read commands: `ping`, `find`, `hierarchy`, `props`, `material`, `mesh`, `overrides`, plus `ub logs`, which reads console messages straight from Editor.log.
- Write commands:
  - `set` writes any serialized property by path, including VRCFury managed references and plain assets.
  - `setmat`, `create`, `delete`, `duplicate`, `reparent`, `addcomp`, `rmcomp`.
  - `apply` and `revert` for prefab overrides.
  - `select`, `refresh`, `menu`, `save`, `open`, `play`.
- `ub mcp`: the same commands as a dependency-free stdio MCP server.
- Scene edits are one Undo step each; prefab-asset edits load, change and save the contents.
- **Tools > Agent Bridge > Copy ub Path**.
