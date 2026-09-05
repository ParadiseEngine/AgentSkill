---
name: paradise-verify
description: Verify a Paradise game (ShiningPie and siblings) end to end after engine or asset changes — build against engine source, rebuild assets clean, refresh test fixtures, run tests, run headless, then prove the package-mode build. Use before claiming a cross-repo change works.
---

# Verify a Paradise game against engine source and packages

Run from the game's workspace view (e.g. `paradise-workspace/shiningpie-workspace/`). Reach
projects by REAL paths (`../ShiningPie/…`) or `cd` into the repo; never through a workspace symlink.

## 1. Is the source override actually on?

```bash
cd ../ShiningPie && dotnet build ShiningPie.Game/ShiningPie.Game.csproj -getProperty:ParadiseUseEngineSource   # → true
shasum -a 256 ShiningPie.Launcher/bin/Debug/net10.0/Paradise.<Pkg>.dll ../ParadiseEngine/src/Paradise.<Pkg>/bin/Debug/net10.0/Paradise.<Pkg>.dll
```

Every `Paradise.*` package the game references must appear TWICE in
`paradise-workspace/Directory.Build.targets`: a `ProjectReference` line and the
`PackageReference Remove` list. A missing one silently stays on NuGet even with the override on.
Also list it in the workspace `.slnx`.

## 2. Build and test against source

```bash
dotnet build ShiningPie.Workspace.slnx 2>&1 | grep -E " error |Build succeeded" | sort -u
dotnet test --project ../ShiningPie/ShiningPie.Tests/ShiningPie.Tests.csproj --no-build 2>&1 | grep -E "^\s+(failed|succeeded|total)"
```

Read `total`: a crashed test host (Noesis view created in a test) reports a small total with a
non-zero exit and looks green.

## 3. Rebuild assets CLEAN when the pipeline changed

The build index caches by inputs, not by importer code. After changing what an importer writes:

```bash
rm -rf ../ShiningPie/build   # only after the CLI is known to build
cd ../ParadiseEngine && dotnet run --project src/Paradise.Cli --no-build -- assets build --project /abs/path/ShiningPie
```

Then refresh the fixtures the tests read (they are copies, not built by the tests):

```bash
cp build/levels/{shiningpie,test,triggers}.toml ShiningPie.Tests/Fixtures/levels/
cp build/Models/Prim_{Cube,Sphere}.{toml,mesh} build/Models/Prim_*.Prim_*.material ShiningPie.Tests/Fixtures/Models/
```

Object-count pins in `AuthoredSceneTests`/`SceneWorldTests` move with the fixture; check the prefab
is the source of a count change before editing a pin.

## 4. Run it

```bash
dotnet run --project ShiningPie.Launcher --no-build -- --headless --frames 120 --hold W --screenshot /tmp/walk.png
```

Expect `Authored scene: … · N variants · M meshes loaded`, `Drawing N instances`, and one
`Skinning variant …` line per skinned variant; then LOOK at the screenshot. Two captures differing
only in `--frames` prove animation. Threading and audio bugs need a windowed run.

## 5. Prove the package build (what CI sees)

```bash
dotnet restore ShiningPie.slnx -p:ParadiseUseEngineSource=false     # NU1102 = version not indexed yet
dotnet build   ShiningPie.slnx -p:ParadiseUseEngineSource=false
dotnet test --project ShiningPie.Tests/ShiningPie.Tests.csproj --no-build -p:ParadiseUseEngineSource=false
```

A `--no-build` test after a failed restore runs STALE binaries; check the restore succeeded first.
An engine API used locally must be in a published package (see `paradise-release`) before the
game's PR can go green.
