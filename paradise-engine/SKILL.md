---
name: paradise-engine
description: Working in the Paradise Engine workspace — the ParadiseEngine core, the ParadiseGodotEditor and ParadiseBlenderEditor authoring hosts, and the games that consume them (ShiningPie, Pingu, ParadiseTown, immortal-cultivation, CultWithin). Use this whenever a task touches any of those repositories, the authored-component contract, the exported scene documents under data/, the Paradise.* NuGet packages, or a build that spans more than one of them. Also use it when something builds green but behaves wrong, when a change needs to reach another repo, or when you are about to publish, tag, or bump a Paradise version — this workspace has several failure modes whose symptom is a PASSING build, and they are documented here.
---

# Paradise Engine workspace

An engine, two authoring hosts, and several games that consume the engine as packages. The
architecture is not the hard part. What costs time here is a small set of failure modes that
**look like success** — a green build against the wrong assemblies, a test suite that passes
because it reads a committed file instead of regenerating it, an editor that silently exports
nothing. Most of this skill is about those.

## Layout

```
paradise-workspace/                    NOT a git repo — a directory of repos plus build wiring
├── Directory.Build.targets            uncommitted, outside every repo. The source override.
├── ParadiseEngine/                    the engine. Publishes Paradise.* packages.
├── ParadiseGodotEditor/               Godot authoring host + the Paradise.Godot.Editor addon package
├── ParadiseBlenderEditor/             Blender addon (Python) + a .NET bridge CLI
├── ShiningPie/  Pingu/  ParadiseTown/  immortal-cultivation/  CultWithin/     games
└── *-workspace/                       symlink VIEWS aggregating a game with its toolchain
```

Each repo has its own remote, history and conventions — read the local `CLAUDE.md` / `AGENTS.md`
when working inside one. **Never create a commit spanning repos.**

## The two things that produce a green build and a wrong result

### 1. Building through a symlink

The `*-workspace/` directories contain symlinks. Reach a project by a **real path**
(`../ShiningPie/…`) or `cd` in first. A path that crosses a symlink fails two ways at once: a wall
of `CS0012` (the SDK canonicalizes source paths but keeps the project path as spelled), and —
worse — the source override stops applying, because it is keyed on the project directory. You get
a passing build against the *packages* while believing you built against source.

### 2. Shadowing the source override

`paradise-workspace/Directory.Build.targets` swaps `Paradise.*` PackageReferences for
ProjectReferences into `ParadiseEngine/src/`, for these repos: immortal-cultivation,
ParadiseGodotEditor, ParadiseTown, ShiningPie, ParadiseBlenderEditor, CultWithin, Pingu.

MSBuild walks up from the project's **physical** directory and stops at the **first**
`Directory.Build.targets` it finds. Adding one inside a repo shadows the override, and the symptom
is not an error — it is a green build against published packages when you meant source.

If a repo needs its own build wiring, use **`Directory.Build.props`**: nothing declares one above
these repos, so it shadows nothing. (`ParadiseGodotEditor/Directory.Build.props` does exactly this
to keep Godot out of `obj/`.)

Always verify rather than assume:

```bash
dotnet build <proj> -getProperty:ParadiseUseEngineSource     # expect: true
```

Turn it off deliberately with `-p:ParadiseUseEngineSource=false` — which is what **CI does**, so
it is the only honest pre-push check. See `references/cross-repo.md`.

## The authored-component contract

One concept underpins every repo. Get it right and most tasks are mechanical.

**A component is a plain record with a `[Guid]`.** The GUID is its identity; `[Authored]` carries
only a `DisplayName`. A missing or malformed `[Guid]` is compile error **PAUT005**.

```csharp
[Guid("b7ab4dd8-c8da-4dc2-9e5e-192fd74deb11")]
[Authored(DisplayName = "Rigidbody")]
public sealed record RigidbodyComponentData { … }
```

**An entity carries one flat list**, engine components and game components alike — no privileged
tier, no named slots:

```json
"Components": [
  { "Id": "f2c0357e-…", "Type": "Paradise.Export.Data.RenderableComponentData", "Data": { … } }
]
```

Read one with `entity.Get<T>()`, keyed on `typeof(T).GUID` — the same attribute the record already
carries, so **no call site should ever name an id**. Write with `Set<T>` / `Entry<T>`. Get
everything at once with `AuthoredComponentRouter.Materialize`.

Two current versions, both at 3, both with `MinimumSupportedVersion == CurrentVersion`: the
**level document** (`LevelData`) and the **authoring schema**. That equality is deliberate — v2
keyed components by name and there is no way back to a GUID, so an old document is **refused on
read**, not upgraded. Regenerate it by re-exporting from its editor.

Full details, including the sharp edges: `references/contract.md`.

## Per-repo guides

Read the one you need — each has the commands and the traps for that host:

| Working on | Read |
|---|---|
| Engine, contract, packages | `references/contract.md` |
| Blender addon | `references/blender.md` |
| Godot editor | `references/godot.md` |
| Anything crossing repos, publishing, versions | `references/cross-repo.md` |

## Verification discipline

This workspace punishes assumed success. A few habits that repeatedly pay:

**Re-derive, don't re-read.** Test suites here often read a *committed* file. That proves the file
parses; it proves nothing about the tool that wrote it. When you change an editor, regenerate its
output and **diff against the previous version** — that is how a bug where an editor silently
exported entities with no components at all was caught, after the build and 136 tests passed.

**A compiled dependency hides a breaking change until runtime.** A package built against an older
contract links and compiles fine; it fails when the type is actually touched, inside the editor,
with nothing red anywhere. When bumping one Paradise version, bump everything that was built
against it.

**Prefer the check that fails loudly.** Several scripts here *skip* a step whose tool is missing
rather than failing — a wrong path silently downgrades the run and still reports green. Print tool
versions before running such a script, so a bad path dies at the check instead of quietly
narrowing the test.

**Distrust convenience readers on compressed or generated files.** `strings` on a `.blend` finds
nothing because the file is compressed; nuget.org's index endpoints disagree with each other and
with reality. Open the file with a real reader; verify a package with an actual restore.

**When a mechanism has a documented reason, find it before changing it.** `.gdignore`, the
`.props`-not-`.targets` rule, and the LFS lock on `.blend` all have their rationale written next
to them. Each exists because someone lost time to its absence.
