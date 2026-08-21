# Paradise Engine agent skill

A [Claude Code skill](https://docs.claude.com/en/docs/claude-code/skills) for agents working
across the Paradise Engine workspace — the engine, the Godot and Blender authoring hosts, and the
games that consume them.

> ### Read this before installing
>
> **This skill has not been shown to improve agent performance in this workspace.** It was
> benchmarked against agents given no skill at all, on three realistic tasks, and the result was a
> tie — 16 of 17 checks passed either way. See [Does it actually help?](#does-it-actually-help)
> below for the numbers and what they mean.
>
> The likely reason is a good one: these repos are densely self-documented, so a capable agent
> reading `AGENTS.md`, `CLAUDE.md` and the doc comments arrives at the same answers unaided. Use
> this skill if a shortcut through that reading is worth ~100 lines of context to you — not on the
> assumption that it makes the agent more correct.

## What it is for

**Most work in this workspace is game work** — declaring what a scene means to your game,
authoring it, reading it back at runtime — and the skill leads with that. Adding a role or a
tunable should be one record in your own code, with no editor or engine change; `references/games.md`
is the guide for doing that, and the entry point for most tasks.

The rest of the skill is about a small set of failure modes whose symptom is a **passing build**:

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
├── SKILL.md                    workspace map, the one idea, the green-build traps
└── references/
    ├── games.md                START HERE — components, tuning, data/, the authoring loop
    ├── contract.md             the contract itself, schema versions, AOT constraints
    ├── blender.md              extension packaging, ID-property storage, test layers, LFS locking
    ├── godot.md                headless export, .tscn id keying, .gdignore, addon publishing
    └── cross-repo.md           version bumps, publishing, nuget propagation, per-repo CI coverage
```

`SKILL.md` is loaded whenever the skill triggers; the references are read on demand, so the detail
costs nothing until it is needed.

## Does it actually help?

Honest answer: **not measurably, on the tasks tested so far.**

Three game-developer tasks were run twice each — once with this skill loaded, once with no skill —
against the real repos under read-only constraints, and graded on 17 predefined checks.

| Task | with skill | no skill |
|---|---|---|
| Add a new authored component and read it back | 6/7 | **7/7** |
| Diagnose "builds green locally, red in CI" | 5/5 | 5/5 |
| Diagnose "config value ignored, unknown component" | **5/5** | 4/5 |
| **Total** | **16/17** | **16/17** |

Token cost was within ~4% on the one cleanly comparable pair. Only **2 of 17 checks discriminated
at all**, and they cancelled out:

- the skill won one — the unaided agent never connected the runtime error message to the generated
  registry lookup that emits it;
- the unaided agent won one — the skill-loaded run minted valid GUIDs but never said where they
  came from.

### What the unaided runs knew that this skill did not

Worth recording, because it is the clearest measure of the skill's gaps. Each of these was found by
an agent working without it:

- the Blender exporter never emits `Parent` — all 194 entities in the shipped export have
  `"Parent": null` — so parent/child scene shapes are not available to a game
- an authored array becomes a host-reference list; a list of typed rows is not supported
- export entry order is name-sorted, which matters because exports are committed and diffed

### Two things the evaluation corrected in the skill

Both were outright errors that had shipped here:

1. **Tuning config.** `games.md` presented `data/<game>/config.json` as *the* pattern. Pingu
   deliberately abandoned it and moved every tunable onto the component that owns it. Now
   documented as two patterns with a table of which game uses which.
2. **Cold-restore and mixed pins.** `cross-repo.md` claimed `dotnet restore --no-cache` reproduces
   CI and that a mixed pin fails with `NU1102`. Neither is true, and the real behaviour is worse:
   `--no-cache` still resolves from `~/.nuget/packages`, and `Paradise.Export 0.17.0` beside
   `Paradise.ECS 0.14.1` restores **clean and silent** because no dependency edge connects them.
   Corrected against actual restores.

### Caveats on the benchmark itself

State them so the number is not over-read:

- **n = 3.** Three tasks, one run each. Nowhere near enough to detect a small effect.
- One with-skill run was **re-run under a tool-call budget** after the first attempt stalled, so its
  cost figure is not comparable to its baseline.
- One task was built on a **false premise** (that Pingu has a `config.json`). Both arms caught the
  error, which makes it a poor discriminator.
- Two checks were initially mis-specified and regraded by hand — `init` accessors were flagged on a
  runtime DTO, where they are perfectly legal; `PAUT003` binds only `[Authored]` records.

The evaluation inputs, outputs and grades live in `evals/`.

## Keeping it true

The value here is accuracy about a specific workspace, which means it goes stale in specific ways.
Worth re-checking when things move:

- schema versions (`LevelData.CurrentSchemaVersion`, `AuthoringSchemaDocument.CurrentVersion`)
- the repo list in the source override's condition
- which repos have CI, and what those jobs are called
- the published version lines for `Paradise.*` and `Paradise.Godot.Editor`
- which editor authors which game, and where each game's scene document lives

Anything here stated as a fact was checked against the repos when written — though the evaluation
above found two claims that were wrong anyway, both in places where the documented behaviour
sounded plausible and nobody had run the command. Prefer verifying to trusting this file. If you
change a mechanism the skill documents — especially one with a written rationale, like `.gdignore` or the
`.props`-not-`.targets` rule — update the skill in the same change.
