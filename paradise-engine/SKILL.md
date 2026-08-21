---
name: paradise-engine
description: Working in the Paradise Engine workspace — building the games (ShiningPie, Pingu, ParadiseTown, immortal-cultivation, CultWithin) and the engine and authoring hosts they run on. Use this whenever a task touches a Paradise game's scene, authored components, tuning config, or data/ export; the ParadiseEngine core; the ParadiseGodotEditor or ParadiseBlenderEditor authoring hosts; or a Paradise.* package version. Also use it when something builds green but behaves wrong, when a change needs to reach another repo, or before publishing, tagging, or bumping a Paradise version — this workspace has several failure modes whose symptom is a PASSING build, and they are documented here.
---

# Paradise Engine workspace

An engine, two authoring hosts, and the games built on them. **Most work here is game work**, and
the design intends that: adding a role, a marker, or a tunable should be one record in your own
code — no editor change, no engine change, no id written down anywhere new.

Start at `references/games.md` unless you are specifically working *on* the engine or an editor.

## Layout

```
paradise-workspace/                    NOT a git repo — a directory of repos plus build wiring
├── Directory.Build.targets            uncommitted, outside every repo. The source override.
├── ParadiseEngine/                    the engine. Publishes Paradise.* packages.
├── ParadiseGodotEditor/               Godot authoring host + the Paradise.Godot.Editor addon
├── ParadiseBlenderEditor/             Blender addon (Python) + a .NET bridge CLI
├── ShiningPie/ Pingu/ ParadiseTown/ immortal-cultivation/ CultWithin/     the games
└── *-workspace/                       symlink VIEWS aggregating a game with its toolchain
```

Each repo has its own remote, history and conventions — read the local `CLAUDE.md` / `AGENTS.md`
when working inside one. **Never create a commit spanning repos.**

## Where to look

| Working on | Read |
|---|---|
| **A game** — scenes, components, tuning, data/ | **`references/games.md`** ← start here |
| The contract itself, or an engine component | `references/contract.md` |
| The Blender addon | `references/blender.md` |
| The Godot editor | `references/godot.md` |
| Publishing, versions, anything crossing repos | `references/cross-repo.md` |

## The one idea underneath everything

**A component is a plain record with a `[Guid]`.** The GUID is its identity; `[Authored]` carries
only a `DisplayName`. A missing or malformed `[Guid]` is compile error **PAUT005**.

```csharp
[Guid("e58e43ea-fa67-4f64-a6df-9f40beafcbfe")]
[Authored(DisplayName = "Player (Red)")]
public sealed record PlayerMarker;
```

**An entity carries one flat list** — your game's components and the engine's alike, no privileged
tier:

```json
"Components": [
  { "Id": "f2c0357e-…", "Type": "Paradise.Export.Data.RenderableComponentData", "Data": { … } },
  { "Id": "e58e43ea-…", "Type": "ShiningPie.Authoring.PlayerMarker",            "Data": {} }
]
```

Games read them with `AuthoredComponentRouter.Materialize` and pattern-match. Engine-side
one-offs use `entity.Get<T>()`, keyed on `typeof(T).GUID` — the same attribute the record already
carries, so **no call site should ever name an id**.

Both the level document and the authoring schema are at version **3**, and both set
`MinimumSupportedVersion == CurrentVersion` deliberately: v2 keyed components by name, and there is
no way back to a GUID, so an old document is **refused on read** rather than upgraded. Re-export it.

## Failure modes whose symptom is a passing build

This workspace punishes assumed success. These are the ones that cost real time.

**Building through a symlink.** The `*-workspace/` views contain symlinks. Reach a project by a
real path (`../ShiningPie/…`) or `cd` in first. Crossing one gives a wall of `CS0012` *and* —
worse — silently stops the source override applying, so you get a green build against the
*packages* while believing you built against source.

**Shadowing the source override.** `paradise-workspace/Directory.Build.targets` swaps `Paradise.*`
PackageReferences for ProjectReferences into engine source. MSBuild stops at the **first**
`Directory.Build.targets` it finds walking up, so adding one inside a repo shadows it — again with
a green build against packages rather than an error. If a repo needs build wiring, use
`Directory.Build.props`: nothing declares one above these repos. Verify rather than assume:

```bash
dotnet build <proj> -getProperty:ParadiseUseEngineSource      # expect: true
dotnet build <proj> -p:ParadiseUseEngineSource=false          # what CI does
```

**Tests that read a committed file.** Suites here bind against the *shipped* export. That proves
the file parses; it proves nothing about the tool that wrote it. After changing anything about
authoring, **re-export and diff against the previous version** — that is how a bug where an editor
silently exported entities with no components at all was caught, after a green build and 136
passing tests.

**A compiled dependency hiding a breaking change.** A package built against an older contract links
and compiles fine, then fails when the type is actually touched — inside the editor, at runtime,
with nothing red anywhere. Bump everything that was built against a contract you changed.

**Scripts that skip rather than fail.** Several here skip a layer whose tool is missing and still
report green, so a wrong path quietly narrows the test run. Print tool versions before invoking
them, so a bad path dies at the check.

**Convenience readers that lie.** `strings` on a `.blend` finds nothing because the file is
compressed — that reads as proof of absence. nuget.org's index endpoints disagree with each other
and with reality. Open the file with a real reader; verify a package with an actual restore.

## When a mechanism has a written reason, read it first

`.gdignore`, the `.props`-not-`.targets` rule, and the LFS lock on `.blend` all carry their
rationale next to them. Each exists because someone lost time to its absence.
