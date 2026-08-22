# Building a game on Paradise

Most work in this workspace is this: declaring what a scene *means* to your game, authoring it in
an editor, and reading it back at runtime. The engine and both editors exist to serve this loop,
and you can do nearly all of it without touching either.

## Contents

- [The loop](#the-loop)
- [Which editor authors your game](#which-editor-authors-your-game)
- [Declaring what a scene means](#declaring-what-a-scene-means)
- [Reading components back](#reading-components-back)
- [Tuning: config.json as an authored document](#tuning-configjson-as-an-authored-document)
- [The data directory](#the-data-directory)
- [Developing against engine source](#developing-against-engine-source)
- [When you need an engine change](#when-you-need-an-engine-change)

Structuring what *runs* the scene — the sim boundary, worlds, threads, snapshots, input layering —
is `runtime.md`.

## The loop

1. **Declare** a component — a record with a `[Guid]`, in your game's assembly.
2. **Rebuild.** The generator dumps `data/authoring-schema.json`, and the editor picks it up
   live — no editor code, no registration, no restart.
3. **Author** it in the editor: attach the component to an object, fill the fields.
4. **Export** — the editor writes `data/scenes/<name>.json`.
5. **Read it back** at runtime and turn it into gameplay.

The important property: **adding a role or a tunable is one record in your own code.** If a task
seems to require editing an editor, or naming a GUID somewhere new, stop — that is a signal the
design is being worked against rather than with.

## Which editor authors your game

| Game | Authored in | Scene lives at |
|---|---|---|
| ShiningPie | **Blender** (`authoring/shiningpie.blend`) | `data/scenes/shiningpie.json` |
| Pingu | **Godot** (`project.godot`, addon only — no play mode) | `data/scenes/pool.json` |
| ParadiseTown | **Godot** | `data/scenes/town.json` |
| immortal-cultivation | **Godot** | `data/scenes/cultivation.json` |
| CultWithin | neither — its own `data/levels/*.level.json` schema | — |

Both editors write the *same* document, so the contract below is identical either way. What
differs is how you drive them: see `blender.md` or `godot.md` for the editor you actually use.

Run a standalone game with its launcher:

```bash
cd ShiningPie && dotnet run --project ShiningPie.Launcher
cd Pingu      && dotnet run --project Pingu.Launcher
```

## Declaring what a scene means

Your game's vocabulary is a set of plain records. Opt the assembly into a generated registry once:

```csharp
[assembly: AuthoredRegistry]      // emits <YourNamespace>.AuthoredComponents
```

Then declare components. A **marker** carries no data — the point is that the object *is* that
thing:

```csharp
[Guid("e58e43ea-fa67-4f64-a6df-9f40beafcbfe")]
[Authored(DisplayName = "Player (Red)")]
public sealed record PlayerMarker;
```

A component with data is the same plus properties, which become the fields an author edits:

```csharp
[Guid("8cc2bcba-f31d-42ce-bc21-e77819d809fd")]
[Authored(DisplayName = "Anchor (named place)")]
public sealed record AnchorMarker
{
    [AuthorDoc("Which place of the zone plan this entity is. Each place may appear once.")]
    public AnchorPlace Place { get; set; } = AnchorPlace.HutDoor;
}
```

Things worth knowing while writing these:

- **Generate the GUID with `uuidgen`.** Never hand-type one, never continue a pattern you see.
  A missing or malformed `[Guid]` is compile error **PAUT005**.
- **No constants class.** `typeof(PlayerMarker).GUID` *is* the id — a second copy is one rename
  from drifting. Tests and messages should use `typeof(T).GUID` and `nameof(T)`.
- **Every property needs a plain setter.** The generated reader constructs the record then assigns
  what the payload names, so `init` is a build error (**PAUT003**). Absent members keep their
  initializer, which is why defaults should be real values.
- **An enum beats a free string** where the set is closed — the editor shows a dropdown and a typo
  becomes unrepresentable.
- **Semantic attributes, not editor hints.** `[Meters]`, `[Radians]`, `[Seconds]`, `[Kilograms]`,
  `[AuthorRange]`, `[AuthorDoc]`, `[AuthorVisibleWhen]`, `[AuthorAssetKinds]` say what a value
  *means*; each editor maps that to its own widget. A definition that names Godot's vocabulary
  makes Blender inherit Godot's vocabulary forever.
- **`[AuthorRange]` is advisory.** It reaches the schema and nothing else — no clamp happens at
  load. Your own validation stays the enforcement.
- **Keep field names short.** Blender caps a stored property name at 63 characters and spends 32
  on the prefix and id, so roughly 31 remain for the field path. `FollowYawSmoothingSeconds` (25)
  fits; much longer will not.

## Reading components back

Materialize once per entity and pattern-match. This is the game-side idiom — `Get<T>()` exists but
is for one-off lookups of engine components:

```csharp
var unresolved = new List<AuthoredComponentData>();
IReadOnlyList<object> components =
    AuthoredComponentRouter.Materialize(entity, AuthoredComponents.Default, unresolved);

foreach (var component in components)
{
    var role = component switch
    {
        PlayerMarker => ActorRoleKind.Player,
        CarMarker    => ActorRoleKind.Car,
        EnemyMarker  => ActorRoleKind.Enemy,
        _            => (ActorRoleKind?)null,
    };
    …
}
```

**Do something with `unresolved`.** It collects payloads this build cannot read — a scene authored
against a newer version of your game, or a component you deleted. Silently dropping them is how
authored data goes missing without a word. Report them, and name `Type ?? Id`: a bare GUID tells
whoever hits the error nothing, which is exactly why the contract carries the CLR name beside it.

Materialize also means a component nobody wrote an accessor for still surfaces, rather than being
authored, exported, and never read.

## Tuning: two patterns, and which game uses which

Balance numbers can live in one of two places, and the games here deliberately differ. Check
before assuming:

| Game | Tuning lives in |
|---|---|
| ShiningPie, ParadiseTown, immortal-cultivation | a **config document** — `data/<game>/config.json` |
| Pingu | **on the component that owns it**, authored in the scene — there is no config file |

Pingu's `GameConfig` says why it moved: *"Every tunable moved onto the component that owns it — a
penguin's body and swim numbers live on `pingu.penguin` … 'One place to tune' survives; it moved
from data/pingu/config.json to scenes/penguin.tscn."* Both are legitimate; the component-owned
form keeps the number next to the thing it describes, the document form keeps every number in one
hand-editable file.

If a task mentions a config file, confirm the game actually has one — a stale copy can survive
under a gitignored staging directory (Pingu's `wwwroot/data/`) and be read by nothing.

### The config-document form

The same `{Id, Data}` shape, in a file that stays hand-editable:

```json
{
  "// note": "comment keys are allowed and ignored",
  "Components": [
    { "Id": "f0233852-…", "Data": { "MaxSpeed": 6.5, "Acceleration": 45.0 } }
  ]
}
```

Declare the tuning groups as `[Authored]` records exactly like scene components, and read them
through the same generated registry. The payoff is that an editor can show them with their units,
ranges and prose instead of someone hand-editing JSON blind.

The split is location, not mechanism: **the scene says what the world IS, config says how it
PLAYS.** Both reach an editor through the same schema dump — which is also why the component-owned
form works: it is the same records, authored somewhere else.

Ids here are GUIDs too. Parse rather than string-compare when reading, and refuse a non-GUID id
where it is written — a hand-edited config is the one place that happens, and "not a GUID" is a
different problem from "unknown component", worth a different message.

## The data directory

`data/` is **export output** — the editor rewrites it wholesale — but specific artifacts are
committed because the game and its tests need them without an editor: `data/scenes/`,
`data/Models/` (GLBs and their `.ktx2` sidecars, which must travel together), `data/materials/`,
`data/audio/`, and the config documents.

Not tracked: `.import` sidecars and extracted PNGs, which are regenerated on import.

Because the export is committed, **a scene change is a code review artifact.** Re-export, look at
the diff, and expect it to be readable: entries carry an `Id`, a `Type` and a `Data`, and an entity
that authors nothing has an empty array.

Tests generally bind against the *shipped* export, so a scene edit that breaks entity binding fails
in the test suite rather than at play time. That is the intent — but it also means the tests prove
the committed file parses, not that the editor still writes it correctly. After changing anything
about authoring, re-export and diff.

## Developing against engine source

The `*-workspace/` directories aggregate a game with the engine and its editor so you can build the
whole stack from source:

```bash
cd shiningpie-workspace && dotnet build ShiningPie.Workspace.slnx
```

Two rules make this safe, and both fail *silently* when broken — see `SKILL.md`:

- Reach projects by a **real path** (`../ShiningPie/…`) or `cd` in first, never through a symlink.
- Never add a `Directory.Build.targets` to a game repo; it shadows the source override.

Remember CI restores from NuGet while you are building from source. Before pushing anything that
touches a version, verify the way CI will:

```bash
dotnet build -p:ParadiseUseEngineSource=false
dotnet test --project <tests> -p:ParadiseUseEngineSource=false
```

## When you need an engine change

Most game work does not. If you find yourself wanting one, check first whether the thing you need
is already expressible as a component — the whole point of the design is that a game's vocabulary
is the game's business.

When it genuinely is an engine change, it reaches you as a **published package**, not a local
edit: change the engine, publish, then bump your pins. Version bumps are all-or-nothing across the
`Paradise.*` set, and a Godot-authored game must also take a matching `Paradise.Godot.Editor`.
See `cross-repo.md` for the ordering, and do not add a ProjectReference into engine source to a
committed csproj — that is what the workspace override exists to avoid.
