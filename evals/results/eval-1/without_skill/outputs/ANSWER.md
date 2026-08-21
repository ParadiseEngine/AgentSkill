# "Builds fine here, CI can't even restore" — ShiningPie

## Short version

Your local build never restores a single `Paradise.*` package, so the `Version="…"` strings in
ShiningPie's `.csproj` files are **dead text on your machine and load-bearing in CI**. Anything
wrong with them — a version that isn't on nuget.org *yet*, a version bumped in one project but
not its sibling, a package that was never packed — is invisible locally and kills CI at
`dotnet restore`, before a single line is compiled. That is exactly the "can't even restore"
shape.

Passing all 204 tests proves your *code* is right. It proves nothing about the package graph,
because the package graph was deleted before the build started.

The pre-push check is at the bottom. The one-liner you want is:

```bash
cd /Users/quabug/proj/paradise-workspace/ShiningPie
NUGET_PACKAGES=$(mktemp -d) dotnet restore ShiningPie.slnx -p:ParadiseUseEngineSource=false
```

---

## Why the version numbers are dead text locally

`/Users/quabug/proj/paradise-workspace/Directory.Build.targets` — uncommitted, sitting outside
every repo — fires for any project whose physical directory path contains `ShiningPie`. It ends
with this line:

```xml
<PackageReference Remove="Paradise.Authoring;Paradise.Assets.Gltf;Paradise.Assets.Textures;Paradise.Audio.Wwise;Paradise.ECS;Paradise.Export;Paradise.Physics;Paradise.Rendering;Paradise.Rendering.Pbr;Paradise.Rendering.WebGPU;Paradise.Rendering.Browser;Paradise.Ui.ImGui;Paradise.Ui.Noesis;Paradise.Ui;Paradise.Ui.Noesis.Host" />
```

Every `Paradise.*` PackageReference in

- `/Users/quabug/proj/paradise-workspace/ShiningPie/ShiningPie.Core/ShiningPie.Core.csproj`
- `/Users/quabug/proj/paradise-workspace/ShiningPie/ShiningPie.Launcher/ShiningPie.Launcher.csproj`

is *removed* and replaced with a `ProjectReference` into `ParadiseEngine/src/`. NuGet is never
asked to resolve them. The version attribute is not validated, not compared against the feed, not
even parsed for meaning.

CI has no such file (it lives above every repo root, so no checkout ever sees it) and restores
those exact versions from nuget.org. ShiningPie also ships **no `NuGet.config`** — I checked the
whole repo and `origin/main`'s tree — so CI's only feed is nuget.org. There is no private mirror
to fall back on.

### The four failure modes this hides, all restore-time

| What you did | Local result | CI result |
|---|---|---|
| Bumped to a version the engine hasn't published | green, 204/204 | `NU1102 Unable to find package Paradise.X with version (>= 0.18.0)` |
| Bumped `Launcher.csproj` but not `Core.csproj` | green | `NU1605` downgrade — `Paradise.Export 0.17.0` *requires* `Paradise.Authoring 0.17.0`, so a stale sibling pin conflicts |
| Referenced a new `Paradise.*` package nobody packed | green (or a missing `ProjectReference` you'd notice) | `NU1101 Unable to find package` |
| Pushed within minutes of the engine's publish job going green | green | `NU1102` on *some* packages and not others |

That last row is the most likely culprit for what you actually hit, and it is already written up
in your own `~/.claude/lessons.md`:

> **nuget.org indexing lags the publish workflow by minutes, and per-package.** A green publish
> job does NOT mean `dotnet restore` can see the version, and packages from the same run appear
> at different times — so a downstream CI run can fail on `NU1102` for one package while another
> from the same push already resolves.

The timeline today fits it exactly: engine `v0.17.0` was tagged at **13:38**, and the 0.17.0
packages only landed in your local cache at **13:56**. That is an ~18-minute window in which
ShiningPie's `Consume Paradise.* 0.17.0` commit was pushable, correct, and un-restorable.

---

## The state right now: the tree is clean

I reproduced CI's restore exactly — copied only the three `.csproj`, the `.slnx` and `global.json`
into `/tmp` (so `Directory.Build.targets` is out of scope, i.e. package mode), pointed
`NUGET_PACKAGES` at an empty directory to force real feed resolution, and ran `dotnet restore`:

```
Restored ShiningPie.Core.csproj      (in 3.96 sec)
Restored ShiningPie.Tests.csproj     (in 15.68 sec)
Restored ShiningPie.Launcher.csproj  (in 32.4 sec)
```

Clean. Corroborating facts:

- Every direct reference resolves on nuget.org: all eleven `Paradise.* 0.17.0`, plus
  `DotRecast.Detour 2026.1.3`, `TUnit 1.57.0`, `ppy.SDL3-CS 2026.320.0`.
- Every transitive one too: `Paradise.Assets.Textures 0.17.0`, `Noesis.GUI 4.0.0-beta18`,
  `WebGPUSharp 0.5.2`, `DotRecast.Core/Recast 2026.1.3`.
- There is no unshipped engine API in play: `ParadiseEngine` tag `v0.17.0` **is** `main` HEAD
  (`5f412d0`), both dated 2026-08-21 13:38. Nothing on `main` postdates the tag.
- `feat/authored-guid-ids` is already merged — `origin/main` carries
  `f10079e Merge pull request 30 from feat/authored-guid-ids into main`.

So whatever CI hit, the fix has since landed. Re-run the failed job and it should be green now.

### One thing worth knowing about your last local build

Your most recent local restore was **package mode, not source mode**. The evidence:

```
b683ec3e…  ShiningPie/ShiningPie.Launcher/bin/Debug/net10.0/Paradise.ECS.dll
e2ee384f…  ParadiseEngine/src/Paradise.ECS/bin/Debug/net10.0/Paradise.ECS.dll
b683ec3e…  ~/.nuget/packages/paradise.ecs/0.17.0/lib/net10.0/Paradise.ECS.dll
```

The game's engine DLL is byte-identical to the **package**, not to the source build. And
`ShiningPie.Core/obj/project.assets.json` lists `Paradise.ECS/0.17.0` as a *package*, with zero
Paradise project references.

That is fine here — the tag and `main` are the same commit, so the two builds are equivalent — but
it is the second half of the trap and you should know which mode you are in on purpose rather than
by accident. The inverse mistake is the dangerous one: reaching a project through the
`shiningpie-workspace/` symlink (`dotnet build ShiningPie/ShiningPie.Core/…` from that directory)
silently drops the override, so you get a green **package** build while believing you validated
against engine source. Use `../ShiningPie/…` or `cd` in first.

---

## The check to run before you push

### 1. The cheap gate (~10 s, no build) — catches NU1102/NU1101

Verifies every version in every csproj is actually indexed on nuget.org *right now*, which is the
one thing a source-override build can never tell you:

```bash
cd /Users/quabug/proj/paradise-workspace/ShiningPie
grep -rhoE 'Include="[^"]+" Version="[^"]+"' */*.csproj \
| sed -E 's/Include="([^"]+)" Version="([^"]+)"/\1 \2/' | sort -u \
| while read -r id ver; do
    lc=$(printf '%s' "$id" | tr '[:upper:]' '[:lower:]')
    if curl -s --max-time 15 "https://api.nuget.org/v3-flatcontainer/$lc/index.json" | grep -q "\"$ver\""; then
      printf 'OK    %-28s %s\n' "$id" "$ver"
    else
      printf 'MISS  %-28s %s   <-- CI restore will fail (NU1102)\n' "$id" "$ver"
    fi
  done
```

Current output — all fourteen `OK`:

```
OK    DotRecast.Detour             2026.1.3
OK    Paradise.Assets.Gltf         0.17.0
OK    Paradise.Audio.Wwise         0.17.0
OK    Paradise.Authoring           0.17.0
OK    Paradise.ECS                 0.17.0
OK    Paradise.Export              0.17.0
OK    Paradise.Rendering           0.17.0
OK    Paradise.Rendering.Pbr       0.17.0
OK    Paradise.Rendering.WebGPU    0.17.0
OK    Paradise.Ui                  0.17.0
OK    Paradise.Ui.Noesis           0.17.0
OK    Paradise.Ui.Noesis.Host      0.17.0
OK    ppy.SDL3-CS                  2026.320.0
OK    TUnit                        1.57.0
```

### 2. The real reproduction (~1 min) — catches everything, including NU1605

```bash
cd /Users/quabug/proj/paradise-workspace/ShiningPie
NUGET_PACKAGES=$(mktemp -d) dotnet restore ShiningPie.slnx -p:ParadiseUseEngineSource=false
```

Both halves are required, and this is the part people get wrong:

- **`-p:ParadiseUseEngineSource=false`** turns the source override off so the `Paradise.*`
  PackageReferences survive and NuGet actually has to resolve them.
- **`NUGET_PACKAGES=$(mktemp -d)`** forces a cold cache. Without it your `~/.nuget/packages`
  answers from disk, and a version that is in your cache but not on the feed restores happily —
  which is precisely the version CI cannot find. The flag alone gives you a false green.

To go all the way to a CI-equivalent build and test run:

```bash
cd /Users/quabug/proj/paradise-workspace/ShiningPie
COLD=$(mktemp -d)
NUGET_PACKAGES=$COLD dotnet build ShiningPie.slnx -p:ParadiseUseEngineSource=false \
  && NUGET_PACKAGES=$COLD dotnet test --project ShiningPie.Tests/ShiningPie.Tests.csproj \
       -p:ParadiseUseEngineSource=false
rm -rf "$COLD"
```

Pass the flag to **every** `dotnet` invocation — it is not sticky, and a `test` without it
re-restores in source mode and undoes the point of the exercise.

Two caveats on the build/test step (the restore step has neither):

- It rewrites `bin/`+`obj/` in package mode. Re-run your normal source-override build afterwards
  if you want source artifacts back.
- Per your own ShiningPie lessons: if `data/` was produced against **unshipped** engine features,
  a package-mode *build* can legitimately fail (e.g. `Ktx2Transcoder.TranscodeToBc` on newer KTX2
  sidecars) even though CI would be fine on freshly-exported data. A red package-mode *restore* is
  always a real CI failure; a red package-mode build sometimes isn't.

### 3. Ordering rule for engine-coupled work

`ShiningPie/AGENTS.md` states it as a hard rule ("an engine API this game needs must SHIP first"),
but the sequencing is what bites:

1. Land the `ParadiseEngine` PR on `main`.
2. Tag and let the publish job go green.
3. **Poll the flat-container index until every consumed id shows the new version** — step 1 above
   does exactly this. "Publish job green" is not "restorable"; the packages index at different
   times.
4. Only then push ShiningPie's version bump.

And bump **all** `Paradise.*` versions in `ShiningPie.Core` and `ShiningPie.Launcher` in the same
commit. A split bump is the classic `NU1605` that only CI ever sees, because `Paradise.Export`
carries a hard `Paradise.Authoring` dependency at its own version.

### 4. Know which mode you built in

```bash
cd /Users/quabug/proj/paradise-workspace/ShiningPie
dotnet build ShiningPie.Core/ShiningPie.Core.csproj -getProperty:ParadiseUseEngineSource
```

Or prove it from the artifact — if the game's DLL matches the package hash you were in package
mode, if it matches the engine's `bin/` you were on source:

```bash
cd /Users/quabug/proj/paradise-workspace
shasum -a 256 ShiningPie/ShiningPie.Launcher/bin/Debug/net10.0/Paradise.ECS.dll \
              ParadiseEngine/src/Paradise.ECS/bin/Debug/net10.0/Paradise.ECS.dll \
              ~/.nuget/packages/paradise.ecs/0.17.0/lib/net10.0/Paradise.ECS.dll
```

---

## Two side notes

- **There is no pipeline definition in the repo.** No `azure-pipelines.yml`, no `.azuredevops/`,
  nothing on `origin/main`'s tree. The ShiningPie pipeline is defined in the Azure DevOps UI, so
  you cannot read (or review, or version) its steps from the checkout. Worth moving into the repo
  — among other things `AGENTS.md` notes the checkout step needs `lfs: true` or the LFS binaries
  arrive as pointer files, and right now that setting is invisible to anyone reading the repo.
- **Consider a `packages.lock.json`.** `RestorePackagesWithLockFile` would make the resolved graph
  a committed artifact and turn every one of these silent divergences into a diff you can see in
  review, rather than something only CI discovers.
