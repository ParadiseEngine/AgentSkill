---
name: paradise-verify
description: Validate a Paradise game's engine or asset integration, including source/package boundaries and runtime evidence for affected behavior.
---

# Verify Paradise integration

Choose checks for the behavior changed and the completion the user requested. A package migration
needs package-mode evidence; an asset importer needs rebuilt output; a visual change needs an
observed render. Routine documentation edits do not require the whole integration sequence.

Use physical repository paths for Git and project builds. Workspace `.slnx` files can coordinate
repos, but passing a project path through a workspace symlink can break source overrides.

## Engine source changes

Normal ShiningPie builds deliberately use packages. For source-engine integration, run the
workspace-only `ShiningPie.Local` project from `shiningpie-workspace`; building the workspace
solution alone does not switch the game to source. Preserve ShiningPie's repository targets and
do not add engine ProjectReferences there.

Check `ParadiseUseEngineSource` and the actual engine revision before trusting a source-mode
result in repositories that opt into the parent override. Every overridden `Paradise.*` package
needs both its `ProjectReference` and removal from
`PackageReference` in the workspace `Directory.Build.targets`; a missing entry silently uses
NuGet. Compare resolved DLLs when the dependency origin is uncertain.

Build the affected projects and run their test suites with the repository's current commands.
Capture real exit codes and test totals: filtering logs can hide failures or a crashed test host.
Rebuild the executable before a smoke run; building tests alone may leave its output stale.

## Authoring or asset-pipeline changes

The asset index caches inputs, not importer code. After changing importer output, use the CLI's
supported clean/rebuild flow. If the cache must be replaced manually, verify the CLI is usable
and preserve the old output until the replacement succeeds.

Re-export through the owning editor for authoring changes and inspect the diff. Tests reading
committed exports do not exercise the exporter. Refresh only affected copied fixtures; confirm
count changes against the source prefab before updating assertions.

## Runtime changes

Use the game's current launcher and a bounded scenario exercising the changed path. Inspect
screenshots for visual behavior; compare frames for animation. Threading, input, and audio may
need a windowed run. Report what was observed, not merely that the process exited successfully.

## Published-package delivery

Use `-p:ParadiseUseEngineSource=false` for restore, build, and affected tests. For a new release,
restore into an empty `NUGET_PACKAGES` directory to prove remote availability. Do not run stale
`--no-build` binaries after a failed restore or build. An unpublished engine API is a release
blocker for the consumer; finish the requested release workflow before claiming CI compatibility.

Finish when the affected checks and requested runtime/package evidence are satisfied. Broaden
verification when failures or cross-component changes justify it, and identify remaining blockers.
