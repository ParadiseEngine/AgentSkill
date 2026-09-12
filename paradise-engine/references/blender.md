# ParadiseBlenderEditor

The current extension is the pure-Python `paradise_assets` addon. It edits canonical
`assets/*.prefab` documents in a game repository; `.editor/blend/*.blend` files are disposable
views. The engine `paradise` CLI compiles runtime assets into `build/` and owns sidecar identity
creation. The former `paradise_blender` exporter and .NET bridge were removed.

## Source of guidance

Resolve the physical ParadiseBlenderEditor repository in the user's workspace. Its `CLAUDE.md`
routes to the current `CONVENTIONS.md`, `docs/addon-ui.md`, and `docs/document-contracts.md`.
Use the relevant section for the change instead of treating old exporter examples as current API.

- Coordinates and serialization: convert matrices before decomposition; preserve untouched
  authored numbers. The Python canonical TOML writer must match the C# writer byte-for-byte.
- Load/save and prefab overrides: payloads come from the re-read document. Apply only fields the
  author edited, retaining unknown components and untouched values. Child overrides and deletion
  carriers require the current resolver/save rules.
- Schemas: the game's launcher merges its referenced components into
  `.editor/authoring-schema.json` using `ParadiseAuthoringScanReferences`. There is one live game
  schema, including engine components; do not vendor an engine schema or add a fallback that can
  override it. Rebuild the launcher when the schema is missing or stale.
- Identity and extraction: the CLI watcher alone mints `<asset>.meta`. The addon writes a file
  and waits for its identity. Generated model prefabs are initial authored documents, not mirrors
  to overwrite or delete when source models change.
- Python boundaries: `document/`, `edits.py`, and module-level `paradise_assets/__init__.py` must
  stay free of `bpy` imports so document logic remains importable outside Blender.
- UI: panel draw does not log, walk the asset tree, or write ID properties. Use the existing
  caches and explicit synchronization paths.

## Validation

Choose the checks for the behavior changed, using current repository commands:

```bash
.venv/bin/python -m pytest tests/unit -q
blender --background --factory-startup --python tests/integration/test_axis_parity.py
./tools/run_tests.sh
```

Project-dependent Blender integration uses `PARADISE_ASSETS_PROJECT` and may skip when the
project is unavailable. Inspect executed/skipped tests and exit status before claiming coverage.

For a document-format change, update Python parsing and the canonical writer in lockstep with
`Paradise.Assets.Documents`. Refresh parity fixtures from the engine's fixture source, then run
`paradise assets prefab-check` over a real game project. Exercise Blender load/edit/save when the
change affects its authoring behavior; a unit test alone cannot establish that UI path.

For installation or extension packaging, follow the current repository's tooling and manifest.
`python3 tools/install_addon.py` installs the development symlink; preserve Blender's lifecycle
and registration cleanup contracts when changing addon loading.
