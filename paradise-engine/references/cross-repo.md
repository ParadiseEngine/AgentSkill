# Crossing repos: versions, publishing, CI

The repos are coupled by **published package versions**, not project references. A change in the
engine reaches everything else through a release. This file is about doing that without leaving
something subtly broken.

## Contents

- [Local builds lie about CI](#local-builds-lie-about-ci)
- [Version bumps are all-or-nothing](#version-bumps-are-all-or-nothing)
- [Publishing the engine](#publishing-the-engine)
- [nuget.org tells you three different things](#nugetorg-tells-you-three-different-things)
- [Landing a contract change](#landing-a-contract-change)
- [What CI actually covers](#what-ci-actually-covers)

## Local builds lie about CI

Locally the workspace source override compiles the engine **from source**. CI restores `Paradise.*`
**from NuGet**. So a change can build and test green on your machine while CI cannot even restore —
and an engine API you add must *ship as a package* before a dependent PR can go green. That is not
a misconfiguration; it is the design.

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

Tag-triggered. One version across every published package (24 as of 0.19.0):

```bash
git tag -a v0.17.0 -m "…" && git push origin v0.17.0
```

Under 0.x, a **minor** bump signals a breaking change — that is what this repo's history does
(0.14.0 → 0.15.0 → 0.16.0 → 0.17.0). The workflow also accepts `workflow_dispatch` with an explicit
version.

Publishing goes to **public nuget.org**, where a version can be unlisted but never deleted or
reused. Before tagging: confirm the tag is free, and that `NUGET_USER` is set (the workflow hard-
errors without it).

The publish log echoes several `::error::` lines that are just script text from guard branches that
never fired. Judge by the job conclusion and the `Your package was pushed.` confirmations, not by
grepping for "error".

**A new package is not published until it is added to the workflow's list.** `publish-nuget.yml`
packs from a hardcoded `projects=(…)` array, and its count check compares packed-vs-**listed** —
so an unlisted project passes every gate silently: green build, green tests, green publish job,
and a package that does not exist. Adding a package to the engine is therefore two edits, and the
second one has no compiler behind it. Before tagging, diff the list against the packable projects,
and pack the full list locally at the version you are about to publish:

```bash
sed -n '/projects=(/,/)/p' .github/workflows/publish-nuget.yml | grep -oE 'Paradise[.A-Za-z]+' > /tmp/listed
while read -r p; do dotnet pack "src/$p/$p.csproj" -c Release -o /tmp/nupkg -p:Version=X.Y.Z || echo "FAILED $p"; done < /tmp/listed
```

(Write the list to a file and `while read` it: **zsh does not word-split** an unquoted multi-line
variable the way bash does, so `for p in $list` iterates once with the whole thing as one word and
the loop silently does nothing useful.)

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

The dependency order is strict, because each step produces what the next consumes:

1. **Engine** — change the contract, merge, tag, publish. Wait for restore to succeed.
2. **Godot editor** — migrate, bump its engine pins, merge, then bump the **addon** version and tag
   `addon-v*`. Republish, because an addon built against the old contract fails at runtime.
3. **Blender addon** — migrate, bump the bridge pin, regenerate the vendored engine schema.
4. **Games** — bump engine pins; for Pingu also the addon pin. Re-export scenes from whichever
   editor owns them.

Scenes are **re-exported, not converted**, wherever an editor can be driven. A conversion script is
a fallback for documents whose editor cannot be run — and its output is not identical to the
editor's (a round-trip through Python changes float formatting; the editor omits nulls the script
preserves). If you use one, re-export later and take the editor's bytes as authoritative.

## What CI actually covers

Uneven, and worth knowing before trusting a green PR:

| Repo | CI |
|---|---|
| ParadiseEngine | checks `test`, `aot-publish-run` |
| ParadiseGodotEditor | `test`, `addon-nuget`, `export-smoke` (a real headless export) |
| ParadiseBlenderEditor | `unit` (ruff + pytest), `full` (Blender + conformance) |
| Pingu | `build-test` (plus a `deploy` workflow) |
| ShiningPie | **none** — no pipelines exist in the Azure DevOps project |

A PR in a repo with no CI shows "clean" because nothing ran. Run the suite locally and say so in
the PR.

ShiningPie is on **Azure DevOps**, not GitHub — use `az repos pr`. Its PRs need a merge strategy
selected or the blocking `Require a merge strategy` policy sits `rejected` and completion is
disabled:

```bash
az repos pr update --id <n> --squash true
```

`az repos pr show` does not expand labels; `az repos pr list` does. A PR that looks unlabelled may
not be.
