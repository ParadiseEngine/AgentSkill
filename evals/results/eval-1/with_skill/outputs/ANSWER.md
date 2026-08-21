# ShiningPie: green locally, red in CI

## Short version

Your local build is not the build CI runs.

`paradise-workspace/Directory.Build.targets` (uncommitted, sitting **above** every repo) swaps
ShiningPie's `Paradise.*` **PackageReferences for ProjectReferences into `ParadiseEngine/src/`**.
So on your machine ShiningPie compiles against the engine **working tree at HEAD**, and the
`Version="…"` numbers in the csproj files are never consulted at all — they are dead text.

CI has no such file. It restores `Paradise.*` from nuget.org at exactly the pinned versions.

That gap is by design, not a misconfiguration, and it is stated as a hard rule in
`/Users/quabug/proj/paradise-workspace/ShiningPie/AGENTS.md:24`:

> **Published artifacts only.** Engine code arrives exclusively as `Paradise.*` NuGet packages.
> NEVER add a ProjectReference into a ParadiseEngine or ParadiseGodot checkout. CI builds against
> the published packages, so an engine API this game needs must SHIP first.

Consequence: **204/204 green locally proves nothing about CI** until you re-run it with the
override off.

## What actually happened here — this is your own repo, reproduced

The branch is `feat/authored-guid-ids` in
`/Users/quabug/proj/paradise-workspace/ShiningPie`, four commits ahead of `main`:

```
3d231c2 Consume Paradise.* 0.17.0
aa46483 Read components from the list, and re-export the scene at schema 3
dab6ff5 Migrate the .blend's authored components to GUID ids
94b2ee4 Identify authored components by [Guid], not by a name
```

The first three moved the game onto the GUID-keyed authoring contract (engine PRs #146–#154,
schema/document **version 3**). The pins stayed at **0.14.1** — the release *before* that
contract changed. Locally invisible, because the override meant the pins were never read.

You already found this: `3d231c2` is the fix, and its message says so —

> 11 pins, all previously 0.14.1 — the release before the contract changed under them. Until now
> every build here used the workspace source override, so CI, which restores from NuGet, could
> not have gone green.

I reproduced both states from a copy of the repo placed **outside** the workspace (so no
`Directory.Build.targets` is on the MSBuild walk-up path — exactly what CI sees), restoring into
a throwaway packages folder:

| State | Result |
|---|---|
| pins at **0.14.1** (pre-`3d231c2`) | restore OK, **build FAILED — 13 × CS7036**, `AuthoredAttribute(string componentId)` |
| pins at **0.17.0** (current HEAD) | restore OK, build clean, **`dotnet test` → 204/204 passed** |

So the current tip **is** CI-safe. Verified end to end, not assumed.

## One correction worth having

Your description was "CI can't even restore." For *this* failure that is not quite what CI would
have printed — restore at 0.14.1 succeeds; the packages exist. The failure is at **compile**:

```
error CS7036: There is no argument given that corresponds to the required parameter
'componentId' of 'AuthoredAttribute.AuthoredAttribute(string)'
```

…in `ShiningPie.Core/Authoring/SceneComponents.cs` and `ShiningPie.Core/Config/TuningComponents.cs`.
That is the classic shape of this bug: **your code moved to the new contract; the pin did not.**

A genuine *restore* failure is a different mistake with a different signature — pinning a version
that is not published (yet):

```
error NU1102: Unable to find package Paradise.ECS with version (>= 0.18.0)
  - Found 27 version(s) in nuget.org [ Nearest version: 0.17.0 ]
```

Both come from the same root cause, so the same check catches both. Just know which you are
looking at:

- **NU1101 / NU1102 at restore** → the version you pinned is not on nuget.org. Either you bumped
  ahead of a release, or the engine tag has not finished publishing/indexing.
- **CS**** at compile** → the version restored fine, but your code is written against a newer
  engine contract than the one you pinned.

(Note: engine `HEAD` is currently `5f412d0`, which is *exactly* the `v0.17.0` tag, and the tree is
clean. That is the only reason the pin bump was sufficient. The moment the engine picks up a
commit past its last tag, source-built-green stops implying package-built-green again.)

## The check to run before you push

Two things must both be true: **the override is off**, and **the packages come from the feed, not
from your warm cache.**

### 1. The check itself

```bash
cd /Users/quabug/proj/paradise-workspace/ShiningPie     # cd in — never reach in through the symlink

dotnet build ShiningPie.slnx \
  -p:ParadiseUseEngineSource=false \
  -p:RestorePackagesPath=/tmp/ci-pkgs \
  --no-incremental

dotnet test --project ShiningPie.Tests/ShiningPie.Tests.csproj \
  -p:ParadiseUseEngineSource=false \
  -p:RestorePackagesPath=/tmp/ci-pkgs
```

Expect: restore succeeds, build clean, `204/204 passed`.

**Delete `/tmp/ci-pkgs` first each time** (`rm -rf /tmp/ci-pkgs`) — that is the half most people
skip, and it is the half that matters.

### 2. Why `RestorePackagesPath`, and why `--no-cache` is not enough

`--no-cache` only bypasses NuGet's **HTTP** cache. It still resolves happily out of
`~/.nuget/packages`. I confirmed that directly: a restore of the 0.17.0 set with `--no-cache`
alone completed in **74 ms** — it never touched the network. Redirecting the *global packages
folder* is what actually forces a real download, and it is the difference between "would CI
restore this?" and "did my laptop already have it?".

This matters most in the exact situation you will be in after an engine release: you tag
`v0.18.0`, the workflow pushes, your cache may hold a locally packed or partially-indexed copy,
and the local check goes green while CI gets NU1102 because the feed indexes are still
converging. `references/cross-repo.md` flags this — the flatcontainer index, the search index and
blob storage disagree with each other for ten minutes or more after a push.

### 3. Confirm the override was actually off

The override fails **silently** when it does not apply, so verify rather than trust the flag:

```bash
cd /Users/quabug/proj/paradise-workspace/ShiningPie
dotnet build ShiningPie.Core/ShiningPie.Core.csproj -getProperty:ParadiseUseEngineSource
```

- `true`  → normal local dev build, compiling engine source.
- `false` → you passed the flag; this is the CI-shaped build.
- *empty* → nothing declared it (what my out-of-workspace copy printed). Also CI-shaped.

### 4. The path rule, which silently defeats all of the above

**Never reach a project through a `*-workspace/` symlink.**

```bash
dotnet build ../ShiningPie/ShiningPie.slnx        # OK — real path
cd ShiningPie && dotnet build ShiningPie.slnx     # OK — cd'ing in resolves to the physical path
dotnet build ShiningPie/ShiningPie.slnx           # BROKEN — from shiningpie-workspace/
```

Crossing a symlink gives a wall of `CS0012` *and* silently stops the override applying, because it
is keyed on the project directory. You get a green build against **packages** while believing you
built against **source** — which will make this whole check give you a meaningless pass.

## Making it a habit

- Run the CI-shaped build **whenever a commit touches a `Paradise.*` version, an `[Authored]`
  record, or anything the engine contract owns.** For pure gameplay edits it is optional; for
  those three it is the only thing standing between you and a red PR.
- Put the verification in the commit message the way `3d231c2` already does — "Verified with
  `ParadiseUseEngineSource=false`, against the published packages: builds clean and 204/204 tests
  pass." That is a good habit; keep it.
- **Bumps are all-or-nothing.** The `Paradise.*` set inter-references at matching versions, so
  move all 11 pins together. Do not hand-fix one package: NuGet will silently unify a mixed pin
  upward (I checked — `Paradise.Export 0.17.0` + `Paradise.ECS 0.14.1` restores without a word),
  so a partial bump hides rather than reports the inconsistency.
- **Ordering, when the engine has to change first:** engine → tag → publish → *wait for a real
  `dotnet restore` to succeed* → only then bump the game's pins. Do not bump to a version that
  does not yet restore; you will just push a red CI.

## Two adjacent things I noticed while checking

1. **ShiningPie has no committed pipeline.** There is no `azure-pipelines.yml` or any `.yml`
   anywhere in the repo, which matches the documented state ("no pipelines exist in the Azure
   DevOps project"). If you have just added one server-side, be aware of a *second*, unrelated
   failure mode waiting for it: the binary assets are **Git LFS** (`.blend`, `.glb`, `.ktx2`,
   `.bin`, textures, audio), and `AGENTS.md:96` notes Azure Pipelines needs **`lfs: true` on the
   checkout step** or the agent gets pointer text where meshes should be. That failure looks like
   an asset-load crash, not a restore error.

2. **`data/authoring-schema.json` is in sync.** Building `ShiningPie.Core` re-dumps it, and the
   freshly generated file is **byte-identical** to the committed one. Worth knowing because it is
   committed export output that the Blender addon reads — if it had drifted, CI green would still
   have left the editor authoring against a stale schema.

---

*Everything above was verified against a copy of the repo in `/tmp`. No file under
`/Users/quabug/proj/paradise-workspace` was created, modified, or deleted; all three repos report
0 changes.*
