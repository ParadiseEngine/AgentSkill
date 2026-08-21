# ParadiseBlenderEditor

A Blender **extension** (not a legacy addon) in Python, plus a small .NET bridge CLI. It
reimplements the engine's export contract — ~2,600 lines of Python against the engine's C# — so
the conformance layer below is not optional politeness, it is the only thing standing between that
copy and silent drift.

## Contents

- [Running the tests](#running-the-tests)
- [The extension is an extension](#the-extension-is-an-extension)
- [How authored values are stored](#how-authored-values-are-stored)
- [Derived vs authored components](#derived-vs-authored-components)
- [The vendored engine schema](#the-vendored-engine-schema)
- [Editing a .blend](#editing-a-blend)

## Running the tests

```bash
.venv/bin/python -m pytest tests/unit -q                 # fast, no Blender
.venv/bin/python -m ruff check .
BLENDER=/path/to/blender ./tools/run_tests.sh            # all three layers
```

Three layers, each catching what the others cannot:

| Layer | Needs | Catches |
|---|---|---|
| unit | nothing | contract math against itself |
| integration | Blender | axis convention vs Blender's *own* glTF exporter, live protocol, authoring paths |
| conformance | .NET | the vendored schema and every exported document against the real `Paradise.Export` |

**`run_tests.sh` SKIPS a layer whose tool is missing rather than failing.** A wrong `BLENDER` or
`PYTHON` quietly downgrades the run and still reports green. Print both versions before invoking
it.

`PYTHON` must be an absolute **path**, not a command name: the script guards with
`[ -x "$PYTHON" ] || PYTHON=python3`, and `[ -x python ]` is false for a bare name — so
`PYTHON=python` silently substitutes whatever `python3` resolves to, which may not be the
interpreter pytest lives in.

CI runs both layers (`.github/workflows/ci.yml`); `toktx` is absent there, so KTX sidecar
externalization self-skips.

## The extension is an extension

`blender_manifest.toml` lives **inside** `paradise_blender/`, beside `__init__.py`, because that is
what an extension is — the manifest next to the package root. At the repo root it is never part of
what Blender loads, and `extension build` refuses the repo root outright.

```bash
python3 tools/install_addon.py                    # symlink for development
mkdir -p dist && blender --command extension build \
    --source-dir paradise_blender --output-dir dist
```

Manifest strings (`tagline`, each `permissions` entry) are capped at **64 characters**. Exceeding
it is a `FATAL_ERROR` and no zip is produced — not a warning.

`[permissions]` is **disclosure only**. Blender parses and displays it; nothing enforces it. There
is no sandbox, and the subprocess and socket calls would run identically with no permissions block
at all.

`blender_version_min` has teeth in one direction: a floor above the running Blender **refuses to
enable** rather than degrading. Keep it at the oldest version actually exercised.

## How authored values are stored

Blender ID properties, one per flattened schema field:

```
obj["paradise_components"]              # enabled component ids, full canonical GUIDs
obj["paradise:<token>/<Field/Path>"]    # one value per field
```

ID properties are used rather than a `PropertyGroup` because the schema changes on every game
rebuild, mid-session, and a `PropertyGroup`'s fields are class-level and registered once.

**`<token>` is not the raw GUID.** Blender caps an ID property *name* at 63 characters; a canonical
GUID is 36, leaving 17 for the field path — less than a real field name. Ids are base64url'd to 22
characters (`key_token`). base64**url** specifically, because the alphabet must not contain `/`,
which separates the id from the field path. The token never leaves the `.blend`; the wire keeps the
canonical GUID.

Build keys through `value_key` / `value_key_prefix` — never by hand. A scan that constructs the
prefix independently fell out of step the moment the id stopped being the literal thing inside the
key.

## Derived vs authored components

This host does two different things, and confusing them causes real bugs:

- **Synthesized** from Blender data: renderable (mesh datablock), collider and interactable
  (pointer collections), light (lamp datablock). The exporter mints these entries.
- **Authored** through the schema-driven panel: agent, rigidbody, audio-emitter, particle-emitter,
  and every game component.

An authored component **replaces** the derived entry for its id rather than appending. With named
slots that was assignment and free; with a list the obvious append yields two entries for one
component — and an authored Dynamic rigidbody landing *behind* the derived Static one reads as
static. In practice only the rigidbody reaches that path.

Host-owned detection is **schema-driven**: a component-level `authoredBy` (light, sprite-animation)
or an array field whose `items.authoredBy` names a host object (collider). Only renderable and
interactable are Blender-only policy, because the schema has no signal for them.

`contract/authoring_router.py` still owns two things: spreading identity onto the entity, and
`normalize()`. Nothing on the *reading* side calls `ValidateAndNormalize` — the methods exist in
C# but no runtime path invokes them — so clamping an emitter's frame count or an audio
attenuation has always been the editor's job. Payloads ride verbatim otherwise.

## The vendored engine schema

`paradise_blender/contract/engine_authoring_schema.json` is a copy of a constant compiled into
`Paradise.Export`, because Python cannot load a C# assembly. Regenerate it:

```bash
dotnet build tools/ParadiseBlenderBridge -p:ParadiseUseEngineSource=true
dotnet run --project tools/ParadiseBlenderBridge -- engine-schema \
    > paradise_blender/contract/engine_authoring_schema.json
```

The `-p:ParadiseUseEngineSource=true` matters: the bridge pins a published `Paradise.Export`, and
an older one prints an older schema. `run_tests.sh` fails on drift — that gate is what would catch
the addon shipping a v3 schema while its bridge packs a v2-printing package.

`contract/component_ids.py` is a hand transcription of `ParadiseComponentIds.cs`, holding only the
ids this host names for a *specific* reason. It deliberately has no "all engine ids" set — see
`contract.md`. A unit test asserts every constant still appears in the vendored schema.

## Editing a .blend

`authoring/shiningpie.blend` is **Git LFS lockable** and sits read-only on disk by design. A
`.blend` cannot be merged: if two people edit at once, the second push wins and the first author's
work is gone with no conflict to resolve.

```bash
git lfs lock authoring/shiningpie.blend       # becomes writable
# …edit, save (the addon re-exports data/ on save)
git add -A && git commit && git push
git lfs unlock authoring/shiningpie.blend     # needs a clean status
```

Do not `chmod` around the read-only bit — it is a coordination mechanism with other authors, not
an accident.

Headless export, for regenerating `data/` without opening the GUI:

```python
import bpy, paradise_blender
paradise_blender.register()
from paradise_blender.export.scene import export_scene
bpy.ops.wm.open_mainfile(filepath="…/shiningpie.blend")
export_scene(bpy.context.scene)
```

**Inspecting a `.blend` with `strings` does not work** — the file is compressed, so a grep for
stored keys finds nothing and looks like proof of absence. Open it with Blender.
