---
name: paradise-release
description: Release a ParadiseEngine version — merge the PR, tag vX.Y.Z, wait for the NuGet publish, poll nuget.org indexing, then bump ParadiseVersion in the consuming games (ShiningPie, ParadiseTown, Pingu, …) and their README CLI line. Use when an engine change must reach a game's CI.
---

# Release ParadiseEngine and bump a consumer

The version lives once, in `ParadiseEngine/src/Directory.Build.props` `<Version>`. The PR that
changes the engine's public surface bumps it; the tag makes the packages.

## 1. Merge

```bash
cd ParadiseEngine && gh pr checks <N>          # wait until nothing is pending
gh pr merge <N> --squash --delete-branch
git checkout main && git pull
```

Pass the PR NUMBER explicitly; never let a deferred command infer it from the current branch.
Never merge the base of a stacked PR with `--delete-branch` (it closes the downstream PRs).

## 2. Tag and publish

```bash
grep -n "<Version>" src/Directory.Build.props   # the tag must match it
git tag -a vX.Y.Z <merge-sha> -m "Paradise X.Y.Z: <one line>" && git push origin vX.Y.Z
until [ "$(gh run list --workflow publish-nuget.yml --limit 1 --json status --jq '.[0].status')" = "completed" ]; do sleep 20; done
gh run list --workflow publish-nuget.yml --limit 1 --json conclusion --jq '.[0].conclusion'
```

A new `src/` project ships only if it is in `.github/workflows/publish-nuget.yml`'s package list.
If pack failed before anything was pushed, move the tag onto the fix; confirm nothing shipped via
the flat-container index first.

## 3. Wait for nuget.org to index

Background on why the three nuget.org endpoints disagree: `paradise-engine/references/cross-repo.md`.

A green publish is not availability. Poll every package the consumer needs, per package:

```bash
curl -sL https://api.nuget.org/v3-flatcontainer/paradise.ecs/index.json | grep -c '"X.Y.Z"'
```

Minutes is normal; do not re-push. `dotnet restore -p:ParadiseUseEngineSource=false` failing with
`NU1102` means "not indexed yet", not a broken pin.

## 4. Bump the consumer

- `Directory.Packages.props`: `<ParadiseVersion>X.Y.Z</ParadiseVersion>` and extend the comment
  above it with one sentence on what this version brings.
- README's `dotnet tool install --global Paradise.Cli --version X.Y.Z`.
- Verify in package mode (see `paradise-verify` step 5), then commit and push; update the PR body
  (Azure DevOps for ShiningPie: `az repos pr update --id N --organization https://dev.azure.com/ShiningPie --description "$(cat body.md)"`).

## 5. Record

Update the pipeline-state memory note: version published, PRs open, pins pending.
