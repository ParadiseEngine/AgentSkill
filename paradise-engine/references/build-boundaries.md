# Build and validation boundaries

Use these checks when the change crosses the corresponding build or artifact boundary.

**Building through a symlink.** The `*-workspace/` views contain symlinks. Reach a project by a
real path (`../ShiningPie/…`) or `cd` in first. Crossing one gives a wall of `CS0012` *and* —
worse — silently stops the source override applying, so you get a green build against the
*packages* while believing you built against source.

**Confirm the intended build mode.** The parent workspace `Directory.Build.targets` can swap
`Paradise.*` PackageReferences for engine ProjectReferences. MSBuild stops at the first targets
file it finds walking up, so repository targets can change that behavior. Read the repository's
documented choice before treating a package build as a defect or changing its imports.

ShiningPie deliberately disables the parent source substitution and consumes published packages.
For a source-engine run, use the workspace-only `shiningpie-workspace/ShiningPie.Local` project;
it reuses sibling sources and isolates outputs. From `shiningpie-workspace`:

```bash
dotnet run --project ShiningPie.Local -- --scene build/levels/neon_city_scene.toml --profile
```

Building `ShiningPie.Workspace.slnx` alone does not establish that the game uses source. For a
repository that opts into the parent override, inspect `ParadiseUseEngineSource` and resolved
references against that repository's expected mode. Package delivery still needs a build with
`-p:ParadiseUseEngineSource=false`; do not add engine ProjectReferences to ShiningPie's projects.

**Tests that read a committed file.** Parsing a shipped fixture does not exercise its writer.
For changes to authoring or serialization behavior, regenerate the affected artifact through
the current pipeline and compare it with the previous version. This catches missing components
that a fixture-only test can miss.

**A compiled dependency hiding a breaking change.** A package built against an older contract links
and compiles fine, then fails when the type is actually touched inside the editor or runtime.
Identify consumers built against the changed contract and verify compatibility. Update those
included in the task, and report required downstream releases outside its scope.

**Scripts that skip rather than fail.** Several here skip a layer whose tool is missing and still
report green, so a wrong path quietly narrows the test run. Inspect executed and skipped checks;
verify tool availability or versions when diagnosing a missing validation layer.

**Convenience readers that lie.** `strings` on a `.blend` finds nothing because the file is
compressed — that reads as proof of absence. nuget.org's index endpoints disagree with each other
and with reality. Open the file with a real reader; verify a package with an actual restore.
