# Paradise Engine agent skill

A [Claude Code skill](https://docs.claude.com/en/docs/claude-code/skills) for agents working
across the Paradise Engine workspace — the engine, the Godot and Blender authoring hosts, and the
games that consume them.

## What it is for

The architecture of this workspace is not the hard part. What costs time is a small set of failure
modes whose symptom is a **passing build**:

- building through a symlink, which silently links against packages instead of engine source
- shadowing the workspace source override with a `Directory.Build.targets`, same symptom
- a test suite that passes because it reads a committed file rather than regenerating it
- a package built against an older contract, which links fine and fails at runtime
- a script that *skips* a layer whose tool is missing and still reports green

The skill front-loads those, then hands off to per-repo references for the mechanics.

## Install

Copy `paradise-engine/` into a skills directory Claude Code reads:

```bash
# user-wide
cp -r paradise-engine ~/.claude/skills/

# or per-project
cp -r paradise-engine <your-project>/.claude/skills/
```

Then start a session in the workspace and ask for something that touches it — the skill is
triggered by its description, not invoked by name.

## Layout

```
paradise-engine/
├── SKILL.md                    triggering, workspace map, the green-build traps, contract summary
└── references/
    ├── contract.md             the authored-component contract, schema versions, AOT constraints
    ├── blender.md              extension packaging, ID-property storage, test layers, LFS locking
    ├── godot.md                headless export, .tscn id keying, .gdignore, addon publishing
    └── cross-repo.md           version bumps, publishing, nuget propagation, per-repo CI coverage
```

`SKILL.md` is loaded whenever the skill triggers; the references are read on demand, so the detail
costs nothing until it is needed.

## Keeping it true

The value here is accuracy about a specific workspace, which means it goes stale in specific ways.
Worth re-checking when things move:

- schema versions (`LevelData.CurrentSchemaVersion`, `AuthoringSchemaDocument.CurrentVersion`)
- the repo list in the source override's condition
- which repos have CI, and what those jobs are called
- the published version lines for `Paradise.*` and `Paradise.Godot.Editor`

Anything here stated as a fact was verified against the repos when written. If you change a
mechanism the skill documents — especially one with a written rationale, like `.gdignore` or the
`.props`-not-`.targets` rule — update the skill in the same change.
