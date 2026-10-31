# Releasing

`main` is the working branch: push to it freely. A release is a `vX.Y.Z` tag, and users only get one by installing or updating with that tag in the git URL. The README's install URL pins the latest release, so unreleased work on `main` doesn't reach new installs.

## When to release

- Let changes collect under `## Unreleased` in CHANGELOG.md. Don't tag every push.
- Cut a release when someone needs what's there, or when a batch of work is done.
- Patch (`0.3.x`): fixes and small additions that don't change how existing commands behave.
- Minor (`0.x.0`): new commands, or changes to existing commands, the request format or the CLI's flags.
- Tags are permanent. A ruleset blocks updating or deleting `v*` tags. If a release is wrong, fix it in the next patch rather than moving the tag.

## Steps

1. CHANGELOG.md: rename `## Unreleased` to `## X.Y.Z (YYYY-MM-DD)` and add an empty `## Unreleased` above it.
2. package.json: set `"version"` to `X.Y.Z`.
3. README.md: change the `#vX.Y.Z` pin in the install URL.
4. Commit as `Release X.Y.Z` and push `main`.
5. Tag the release commit and push the tag:
   ```
   git tag -a vX.Y.Z -m "Agent Bridge X.Y.Z: <one-line summary>"
   git push origin vX.Y.Z
   ```
6. Create the GitHub Release, with the version's CHANGELOG section as the notes:
   ```
   gh release create vX.Y.Z --verify-tag --title "Agent Bridge X.Y.Z" --notes-file <notes>
   ```
