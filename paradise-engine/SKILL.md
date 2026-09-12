---
name: paradise-engine
description: Guide Paradise engine/game integration, authored scene contracts, and Godot or Blender host changes. Use for those architecture or cross-repo boundaries, not unrelated edits in a Paradise repo.
---

# Paradise engine and game integration

The workspace contains independent engine, editor, and game repositories. Work in each physical
repository and follow its local guidance; `*-workspace` directories are symlink views, not Git roots.
Keep commits scoped to their repository. Game-specific authored data normally belongs in the game.

Read only the reference needed for the change:

| Task | Reference |
|---|---|
| Game scenes, components, tuning, or exported data | [games.md](references/games.md) |
| Simulation ownership, snapshots, threading, or input | [runtime.md](references/runtime.md) |
| Authored-component identity, schema, or engine contracts | [contract.md](references/contract.md) |
| Blender addon or .blend authoring | [blender.md](references/blender.md) |
| Godot addon or scene export | [godot.md](references/godot.md) |
| Package pins, publication, or cross-repo delivery | [cross-repo.md](references/cross-repo.md) |
| Source/package build mismatch or false-positive validation | [build-boundaries.md](references/build-boundaries.md) |

References record concrete integration constraints. Verify current project paths, schema versions,
package pins, and CI configuration before relying on historical examples. Preserve documented
reasons for `.gdignore`, source-override imports, and LFS locks when changing those mechanisms.
