# Releasing

Work happens on `dev`: push to it freely, and contributors' PRs target it. `main` is the default branch and always matches the latest release. It only changes through a PR from `dev` (a ruleset enforces this), so the repo's front page, and a git URL without a `#tag`, show released code. A release is a `vX.Y.Z` tag on `main`.

## When to release

- Let changes collect on `dev` under `## Unreleased` in CHANGELOG.md. Don't release every push.
- Cut a release when someone needs what's there, or when a batch of work is done.
- Patch (`0.3.x`): fixes and small additions that don't change how existing commands behave.
- Minor (`0.x.0`): new commands, or changes to existing commands, the request format or the CLI's flags.
- Tags are permanent. A ruleset blocks updating or deleting `v*` tags. If a release is wrong, fix it in the next patch rather than moving the tag.
- A docs-only fix can go to `main` between releases with the same `dev` → `main` PR, but only while `dev` holds nothing else unreleased. Otherwise it waits for the next release.

## Steps

1. On `dev`, in CHANGELOG.md: rename `## Unreleased` to `## X.Y.Z (YYYY-MM-DD)` and add an empty `## Unreleased` above it.
2. package.json: set `"version"` to `X.Y.Z`.
3. README.md: change the `#vX.Y.Z` pin in the install URL.
4. Commit as `Release X.Y.Z` and push `dev`.
5. Open the release PR and merge it with a merge commit (the ruleset allows only that method, so `dev` and `main` don't diverge):
   ```
   gh pr create --base main --head dev --title "Release X.Y.Z" --body "See CHANGELOG.md."
   gh pr merge --merge
   ```
6. Tag the merge commit on `main` and push the tag:
   ```
   git fetch origin
   git tag -a vX.Y.Z origin/main -m "Agent Bridge X.Y.Z: <one-line summary>"
   git push origin vX.Y.Z
   ```
7. Create the GitHub Release, with the version's CHANGELOG section as the notes:
   ```
   gh release create vX.Y.Z --verify-tag --title "Agent Bridge X.Y.Z" --notes-file <notes>
   ```
8. Fast-forward `dev` to `main`, so it includes the merge commit:
   ```
   git switch dev && git merge --ff-only origin/main && git push origin dev
   ```
