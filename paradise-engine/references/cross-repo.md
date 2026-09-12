# Crossing repos: versions, publishing, CI

The repos are coupled by **published package versions**, not project references. A change in the
engine reaches everything else through a release. This file is about doing that without leaving
something subtly broken.

## Contents

- [Local builds lie about CI](#local-builds-lie-about-ci)
- [Move every Paradise.* pin together](#move-every-paradise-pin-together)
- [Publishing the engine](#publishing-the-engine)
- [nuget.org tells you three different things](#nugetorg-tells-you-three-different-things)
- [Landing a contract change](#landing-a-contract-change)
- [What CI actually covers](#what-ci-actually-covers)

## Local builds lie about CI

Local source-engine validation and package delivery prove different things. Repositories that
opt into the parent workspace override can compile the engine from source. ShiningPie instead
uses packages in normal builds; `shiningpie-workspace/ShiningPie.Local` is its explicit local
source path. CI restores `Paradise.*` from NuGet, so a successful source run cannot prove that a
new API is available to the consumer's package build.

Before pushing anything that touches a Paradise version, check the way CI will:

```bash
dotnet build <solution> -p:ParadiseUseEngineSource=false
dotnet test  --project <tests>  -p:ParadiseUseEngineSource=false
```

## Move every Paradise.* pin together

Bump the whole set at once. The reason is not that NuGet forces you to — it is that NuGet mostly
**will not**, and the half it misses fails silently.

Only some of these packages depend on each other. `Paradise.Export 0.17.0` requires
`Paradise.Authoring >= 0.17.0`, so pinning those two apart is caught at restore:

```
error NU1605: Detected package downgrade: Paradise.Authoring from 0.17.0 to 0.14.1
```

But `Paradise.Export` declares **no** dependency on `Paradise.ECS`. Pinning `Export 0.17.0`
beside `ECS 0.14.1` restores clean, resolves ECS at **0.14.1**, and emits nothing at all — a
genuinely mismatched set behind a green build. Both behaviours were verified by restore, not
inferred from the docs.

So the discipline is yours to keep, because only a subset of these mistakes announce themselves.

`NU1102` is a *different* failure — "no such version is published" — which is the mistake of
bumping ahead of a release, not of bumping unevenly.

Practical consequence: a repo whose pins have drifted apart (say `Paradise.Export` at 0.14.0 but
`Paradise.Ui` still at 0.6.2) moves **everything** when it moves at all, picking up unrelated
change in the packages it was not tracking. Expect that, and say so in the PR — it is where to look
first if something unrelated-looking breaks.

`Paradise.Godot.Editor` is **not** an engine package. It has its own version line and must be
bumped separately (see `godot.md`).

## Publishing the engine

Tag-triggered. The workflow derives one package version from the release tag. Do not bump the
default `<Version>` or open a version-bump PR just to release:

```bash
git tag -a v0.17.0 -m "…" && git push origin v0.17.0
```

Choose the requested version with the repository's current release policy.

Publishing goes to **public nuget.org**, where a version can be unlisted but never deleted or
reused. Before tagging: confirm the tag is free, and the publishing identity is configured (`NUGET_USER` or the workflow's repository-owner fallback).

The publish log echoes several `::error::` lines that are just script text from guard branches that
never fired. Judge by the job conclusion and the `Your package was pushed.` confirmations, not by
grepping for "error".

**A new package is not published until it is added to the workflow's list.** `publish-nuget.yml`
packs from a hardcoded `projects=(…)` array, and its count check compares packed-vs-**listed** —
so an unlisted project passes every gate silently: green build, green tests, green publish job,
and a package that does not exist. Adding a package to the engine is therefore two edits, and the
second one has no compiler behind it. When adding packages, compare the list against the new packable projects and verify the
resulting packages. Use the current workflow for the exact packing arguments.

## nuget.org tells you three different things

After a push, the flatcontainer index, the search index, and blob storage converge at different
rates. Any single endpoint can be wrong for ten minutes or more, and they disagree with each other.

The only check that reflects what CI will do is a restore that cannot reach your local cache:

```bash
dotnet restore -p:RestorePackagesPath=$(mktemp -d)    # scratch project pinning the new version
```

**`--no-cache` is not enough**, and that is the trap: it bypasses the HTTP cache but still resolves
out of `~/.nuget/packages`. A set already sitting in that folder restores in well under a second
without touching the network, so a version you just "verified" may not be on nuget.org at all.
Pointing `RestorePackagesPath` (or `NUGET_PACKAGES`) at an empty directory is what forces a real
download.

A package can be fetchable (`nupkg` returns 200) while its *dependency* is not yet indexed — the
restore fails with `NU1102` naming the dependency, not the package you asked for. That is lag, not
a failed publish. Confirm the push in the workflow log before assuming anything is wrong.

Do not bump consumers to a version that does not yet restore; you will just push a red CI.

## Landing a contract change

For affected consumers included in the requested delivery, follow their dependency order:

1. **Engine** — change the contract, merge, tag, publish. Wait for restore to succeed.
2. **Godot editor** — migrate, bump its engine pins, merge, then bump the **addon** version and tag
   `addon-v*`. Republish, because an addon built against the old contract fails at runtime.
3. **Blender addon** — migrate the pure-Python `paradise_assets` document readers/writers where
   the contract changed. There is no .NET bridge or vendored engine schema. Rebuild the game's
   launcher to refresh its merged `.editor/authoring-schema.json`; engine components come from
   that game's actual dependencies. The CLI owns asset compilation and sidecar identities.
4. **Games** — bump engine pins and any affected Godot addon pin. Refresh authoring schemas and
   affected documents through the owning editor, then rebuild runtime assets through the CLI.

Validate through the owning authoring path. For the current Blender addon, `assets/*.prefab` is
canonical and `.editor/blend/*.blend` is a disposable view. Preserve untouched document values;
the Python canonical writer must match the engine writer byte-for-byte. For document-format
changes, refresh parity fixtures from the engine's generated fixtures and run
`paradise assets prefab-check` on a real project. Rebuild runtime assets through the CLI. Consult
the current Godot guide for games using its exporter instead.

## What CI actually covers

Inspect the current workflow definitions, branch policies, and checks on the exact PR head.
Historical check names and pipeline availability change; an empty check list does not prove a
successful run. Report local evidence separately from remote CI evidence.

ShiningPie is on **Azure DevOps**, not GitHub — use `az repos pr`. Its PRs need a merge strategy
selected or the blocking `Require a merge strategy` policy sits `rejected` and completion is
disabled:

```bash
az repos pr update --id <n> --squash true
```

`az repos pr show` does not expand labels; `az repos pr list` does. A PR that looks unlabelled may
not be.
