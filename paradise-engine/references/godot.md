# ParadiseGodotEditor

A Godot project at its root (`project.godot`) containing the `Paradise.Godot.Editor` addon, five
sample games, and the `Paradise.Sample.Runtime` host. The addon and the runtime tool are both
published packages; the samples exercise them.

## Contents

- [Headless export](#headless-export)
- [The .tscn stores authored data by id](#the-tscn-stores-authored-data-by-id)
- [Where the addon's sources live, and .gdignore](#where-the-addons-sources-live-and-gdignore)
- [Publishing the addon](#publishing-the-addon)
- [Building and testing](#building-and-testing)

## Headless export

Exporting is a GUI action in the editor, but it can be driven from a terminal — this is documented
in `docs/authoring.md` and it works:

```bash
PARADISE_EXPORT_SCENE=res://scenes/pool.tscn \
  godot --headless --editor --path .
```

`PARADISE_GENERATE_PRIMITIVES=1` and `PARADISE_CONVERT_DATA_GLBS=1` run the other pipeline tasks;
tasks run in that order, then Godot quits.

**Use this to verify editor changes.** The test suites read the *committed* `data/scenes/*.json`,
so they pass whether or not the editor still produces them correctly. Re-export and diff against
the previous version — that is how a change where the editor exported entities with a null `Kind`,
a null `Prefab` and **no components at all** was caught, after a green build and 136 passing tests.

## The .tscn stores authored data by id

`AuthoredEntityCore` keeps an entity's authored values as Godot properties named
`<component-id>/<Field/Path>`, plus `<component-id>/Enabled`. The id is text, deliberately — a
Godot property name is a string whatever the contract says — and becomes a `Guid` at exactly one
boundary, `ExportAuthoredComponents`.

**A scene saved under an older id scheme keys those properties to ids the current schema no longer
declares.** Nothing errors: the editor simply finds no values, and exports entities with nothing on
them. If you change how ids are spelled, the committed scenes need rekeying —
`tools/migrate_scene_authoring_guids.py` is the one-shot that did it last time (592 properties in
`pool.tscn`, 315 in `sample.tscn`).

`ExportAuthoredComponents()` already returns `{Id, Type, Data}` entries. Synthesis happens one
level down in `BakeHosts`/`BakeOne`, where a `CollisionShape3D` becomes shape data, a node's source
`.glb` becomes a mesh field, a `Light3D` becomes baked light values — *authored as a reference,
exported as a value*.

## Where the addon's sources live, and .gdignore

`Paradise.Godot.Editor/` carries a **`.gdignore`**, and it is load-bearing. That directory is the
addon's *package* project: its sources compile into `Paradise.Godot.Editor.dll` and reach Godot as
an assembly reference, never as script resources. Without the marker Godot imports ~20 `.cs` files
as scripts and mints a `.cs.uid` beside each — noise, and actively wrong for the two types whose
design depends on *not* being `res://` scripts (`plugin.cfg` points at the shims under
`addons/paradise/`, not at these).

So: **no `.uid` under `Paradise.Godot.Editor/` is correct**, and one appearing there is the bug.

`obj/` is likewise kept out of Godot's scan, by a `.gdignore` the build writes
(`Directory.Build.props`). It cannot simply be committed — `obj/` is gitignored and a fresh clone
has none until the first build. Note that file is a **`.props`, not a `.targets`**: a
`Directory.Build.targets` here would shadow the workspace source override, and the symptom is a
green build against packages rather than an error.

`.uid` files elsewhere **are** tracked, including under `data/`. A `.uid` is not an import sidecar:
an `.import` is derived and disposable, a `.uid` is the identity Godot mints once and reads back so
a re-import keeps the same id. Left untracked, every clone re-mints one.

## Publishing the addon

The addon has **its own version line** (0.13.0, 0.13.1, 0.14.0, 0.15.0 …), independent of the
engine's. A blanket bump to an engine version asks for something that does not exist.

Three places state the version and the workflow refuses to publish if they disagree:

| Place | What it is |
|---|---|
| the `addon-v<version>` tag | what you type |
| `Paradise.Godot.Editor/AddonVersion.props` | what the package and materialization targets use |
| `Paradise.Godot.Editor/addon/plugin.cfg` | what Godot shows and what ships into every game |

`addons/paradise/plugin.cfg` and `.paradise-addon-version` are **materialized by the build** and
follow on their own. A stale marker makes the payload re-materialize on every build in every
consuming game.

```bash
# after bumping the two source files and merging
git tag -a addon-v0.15.0 -m "…" && git push origin addon-v0.15.0
```

**When the engine's contract changes, the addon must be republished**, and consumers must take the
new addon. An addon built against an older `Paradise.Export` links and compiles beside a newer
engine and then fails at **runtime inside Godot**, with nothing red at build time.

## Building and testing

```bash
dotnet build ParadiseGodot.slnx -p:ParadiseUseEngineSource=true
dotnet test --project Paradise.Sample.Runtime.Tests/Paradise.Sample.Runtime.Tests.csproj
```

Test projects: `Paradise.Sample.Runtime.Tests`, `.Pool.Tests`, `.Odyssey.Tests`, `.Ui.Tests`.

CI runs `test`, `addon-nuget` (packs the addon and checks its dependency allowlist), and
`export-smoke` — a real headless export. Play-mode paths (Dawn readback, Noesis overlay) are not
reachable from CI or a terminal, so compilation plus the test suite is the ceiling on automated
verification here.
