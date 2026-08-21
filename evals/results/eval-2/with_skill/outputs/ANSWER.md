# "It's not a component this build knows" — debugging a camera tuning group in Pingu

## The headline, before any of the ordered checks

Two facts about the repo as it stands today change what you are looking for:

1. **Pingu has no `config.json` any more.** `Pingu/Pingu.Core/Config/GameConfig.cs` says so in its
   own doc comment: *"There is no config file any more. Every tunable moved onto the component that
   owns it … it moved from `data/pingu/config.json` to `scenes/penguin.tscn`."* There is no
   `Pingu/data/pingu/` directory, and neither host loads one — `Pingu.Launcher/Program.cs` and
   `Pingu.Web/Program.cs` both build a `GameConfig` by calling `ApplyPool` / `ApplyScene` with data
   read from `data/scenes/pool.json` and nothing else.

   The only `config.json` on disk is `Pingu/Pingu.Web/wwwroot/data/pingu/config.json` — **stale
   build output**. `Pingu.Web.csproj` states "wwwroot IS BUILD OUTPUT. Nothing in it is hand-edited
   and the whole folder is gitignored"; its `CopyAuthoredWebAssets` target copies `../data/` in and
   never deletes, so that file is an orphan left behind when the real one was retired. Editing it
   cannot produce your error, because nothing reads it — and any edit you make there is silently
   discarded on the next build.

2. **The message you quoted exists in two places, and each names a different file.** Nail down
   which one you actually saw before anything else — it is the cheapest discriminator you have:

   | Exact text | Source | What it was reading |
   |---|---|---|
   | `scene authors components this build does not know: <Type or Id>` | `Pingu/Pingu.Presentation/PinguLevel.cs:163` | `data/scenes/pool.json` — the **scene** |
   | `'<path>': component '<id>' is not one this build knows.` | `ShiningPie/ShiningPie.Core/Config/GameConfig.cs:176` | `data/shiningpie/config.json` — a **config** |

   Only ShiningPie has a config-document loader, and it already ships a camera tuning group
   (`CameraConfig`, `[Guid("b735c1ba-9d72-4d1a-b0ac-7db49b5ca5a3")]`, in
   `ShiningPie/ShiningPie.Core/Config/TuningComponents.cs:165`, already present at
   `ShiningPie/data/shiningpie/config.json:47`). If your message has the `'<path>': component
   '<id>'` shape you are debugging ShiningPie, not Pingu. If it has the `scene authors components`
   shape you are in Pingu and the payload is in **`data/scenes/pool.json`**, whatever file you
   believe you edited.

Everything below applies to either loader; where they differ I say so.

---

## The ordered path

### 1. Find out which document the failing loader actually read

`grep` the message text (above) and read the `path` / component id it printed. In Pingu, the
message prints `Type ?? Id` — so if you see a bare GUID, your JSON entry has no `Type` field, which
is itself worth fixing (see step 5).

*Why this is first:* if the answer is "the stale `wwwroot/.../config.json`", the debugging is over —
that file is dead. If the answer is `data/scenes/pool.json`, the rest of the list applies.

### 2. Is the record declared in an assembly that has a registry?

```
grep -rn "AuthoredRegistry" --include="*.cs" Pingu/    # → exactly one hit
Pingu/Pingu.Core/Config/GameConfig.cs:8: [assembly: AuthoredRegistry]
```

**Only `Pingu.Core` opts in.** `AuthoredRegistryGenerator` emits `AuthoredComponents` *per
assembly*, gated on that assembly attribute; `Pingu.Presentation`, `Pingu.Web` and `Pingu.Launcher`
have none. `PinguLevel` resolves with `Pingu.AuthoredComponents.Default` — Core's registry.

*Why it causes exactly this symptom:* a `CameraConfig` declared next to `FollowCamera.cs` in
`Pingu.Presentation` — the natural-looking home, since that is where the camera lives — gets **no
generated reader, no schema entry, and no diagnostic of any kind**. The build is green, the id in
the JSON is the right id, and `Materialize` still cannot resolve it. This is my prime suspect.

*Fix:* declare it in `Pingu.Core/Authoring/`. There is direct precedent — `WaterRendererConfig`
(`Pingu.Core/Authoring/RoleAndRenderers.cs`) is a purely renderer-facing component that
nonetheless lives in Core precisely so it lands in the registry and the schema.

### 3. Does the record carry **both** attributes?

```csharp
[Guid("…")]                          // identity
[Authored(DisplayName = "Camera")]   // ← the generator keys on THIS
public sealed record CameraConfig { … }
```

`AuthoredRegistryGenerator.Initialize` uses `ForAttributeWithMetadataName(AuthoredModel.AuthoredAttribute, …)`.
A record with `[Guid]` but **no `[Authored]`** is invisible to both generators and produces no
error — while `typeof(CameraConfig).GUID` still returns your GUID, so the id you pasted into the
JSON looks perfectly correct. The reverse (`[Authored]` with no usable `[Guid]`) is the loud case:
compile error **PAUT005**.

Also confirm the record is `public`, has a public parameterless constructor (else **PAUT002**), and
that every property has a plain `set` — `init` or `required` is **PAUT003**. Those are build
errors, so they can't be your problem if the build was green, but they are what to expect if you
fix step 2 and the build then goes red.

### 4. Did the schema and the registry actually regenerate?

```bash
python3 - <<'EOF'
import json; d=json.load(open('Pingu/data/authoring-schema.json'))
print(d['version']); [print(c['id'], c['type'], c['displayName']) for c in d['components']]
EOF
```

Right now that prints version 3 and **six** components — Buddy, Ice ledge, Penguin, Player, Pool
bounds, Water renderer. **No camera.** If it still prints no camera after your rebuild, the
generator never saw your type, and since the schema dump and the registry are driven by the *same*
`[Authored]` scan, the registry does not have it either. That is your error, restated.

The dump is `ParadiseDumpAuthoringSchema` in
`ParadiseEngine/src/Paradise.Authoring/build/Paradise.Authoring.targets` — `AfterTargets="Build"`,
`Inputs="@(Compile);$(MSBuildAllProjects)"`, and it only fires for the project that sets
`ParadiseAuthoringSchemaPath`, which is **`Pingu.Core`** (`Pingu.Core.csproj:35`). Two consequences:
building only the launcher or only the web host does not re-dump it, and it is deliberately skipped
when `ContinuousIntegrationBuild=true`.

This step also explains a likely part of the story: the Godot addon's Add Component picker is
schema-driven, so a component missing from this file cannot be authored in the editor — which is
usually why someone ends up hand-editing the JSON, which is where the malformed-entry causes in
step 5 come from.

### 5. Check the entry's identity fields against the schema, verbatim

Lookup is `registry.TryRead(component.Id, …)` — an exact `Guid` match — with
`TryReadByType(component.Type, …)` as a **second** attempt only
(`AuthoredComponentRouter.ReadOrThrow`).

- **Id spelling.** In a *level* document the id is deserialized by System.Text.Json, which accepts
  only the canonical lowercase hyphenated "D" form; the 32-char and braced spellings **throw**.
  (In ShiningPie's *config* loader it goes through `Guid.TryParse`, which is lenient about the
  format but produces its own distinct message — `component Id '…' is not a GUID` — if you left a
  v2-style name like `pingu.camera` in there.)
- **Type spelling.** The fallback match is exact and case-sensitive on the fully qualified CLR name.
  `Pingu.Presentation.CameraConfig` will not match a record whose namespace is `Pingu`. Copy `id`
  and `type` **verbatim out of `data/authoring-schema.json`**; never synthesize them.
- **Shape.** The level document is **PascalCase** (`"Id"`, `"Type"`, `"Data"`) and the authoring
  schema is **camelCase** — mixing them is a real bug. `Data` must be an object.
- Note the asymmetry if you are in ShiningPie: its config reader calls `TryRead(id, …)` **only**,
  with no type fallback, so a wrong id there is fatal even when `Type` is perfect.

### 6. Rule out "right id, unreadable payload" — the cause a correct GUID does *not* eliminate

`AuthoredComponentRouter.ReadFrom` catches `JsonException` **and** `InvalidOperationException` and
returns `null`, which lands the component in `unresolved` — i.e. **the same message**. The generated
readers parse `JsonElement` directly rather than through a serializer, so a field of the wrong
*kind* (a string where a float belongs) surfaces as `InvalidOperationException`, not a JSON error.

So if the id and type are provably correct and the schema lists the component, check the `Data`
body against the schema's field list: `Vector2/3`/`Quaternion` travel as float arrays, enums as
member-name strings, a composed group as a nested object, `Vector4`/`Color32` as `{r,g,b,a}`. A
property the payload omits keeps the record's own initializer, so absent fields are never the cause
— wrongly-typed present ones are.

### 7. Rule out a cross-assembly GUID collision

Two `[Authored]` types sharing an id *within* one assembly is compile error **PAUT006**. Reusing
another **assembly's** id — say an engine component's — is **not diagnosed**, and
`AuthoredComponentRouter.Resolve` consults `Paradise.Export`'s own `AuthoredComponents.Default`
*first*: your payload materializes as the engine's record, `Materialize` reports success, and the
camera numbers simply never arrive. If at some point the error disappears but nothing changes in
game, this is what happened. Generate ids with `uuidgen`; never hand-type one or continue a visible
pattern.

### 8. Ask whether the binary that failed is the binary you rebuilt

The error means *the loading build* lacks the component, so a stale artifact reproduces it forever.

- **Browser host:** `wwwroot` is generated by `CopyAuthoredWebAssets` copying `../data/` in, and the
  copy never deletes. Compare `Pingu.Web/wwwroot/data/scenes/pool.json` with
  `Pingu/data/scenes/pool.json`, rebuild, and hard-reload — a cached `.wasm`/`.dll` from an earlier
  publish is an old registry. (The dead `wwwroot/data/pingu/config.json` is proof this staleness is
  real here.)
- **Launcher:** `Pingu.Launcher.csproj` notes it must be run **from the repo root**; the scene path
  is relative.

### 9. If "I rebuilt and nothing changed" persists — check the build itself

From `pingu-workspace/`, reaching a project *through a symlink* both floods you with `CS0012` and
silently turns the engine-source override off, so you can be running a package-based build you
believe is a source build. Reach projects by a real path or `cd` in first, then verify rather than
assume:

```bash
cd /Users/quabug/proj/paradise-workspace/Pingu
dotnet build Pingu.Core/Pingu.Core.csproj -getProperty:ParadiseUseEngineSource     # expect: true
```

Likewise, never add a `Directory.Build.targets` inside a game repo — it shadows
`paradise-workspace/Directory.Build.targets` and, again, gives a green build against packages.

### 10. Reproduce it in the test suite rather than in the game

Three tests in `Pingu/Pingu.Tests/` turn this into a fast loop, and one of them is exactly your bug:

- `AuthoredInstanceTests.cs:25` — `the_registry_knows_every_authored_component_this_game_declares`
  (fails the moment your record is outside `Pingu.Core` or missing `[Authored]`)
- `AuthoringSchemaTests.cs:86` — `TheCommittedSchemaMatchesWhatTheGeneratorEmits` (drift between
  the committed dump and the generator)
- `AuthoredInstanceTests.cs:83` — `an_unknown_component_is_reported_rather_than_skipped_quietly`
  (the mechanism that produces your message)

Run from a real path: `dotnet test --project ../Pingu/Pingu.Tests/Pingu.Tests.csproj`.

---

## Where a Pingu camera tuning group actually belongs

Since there is no config document in this game, the answer is a scene/prefab-authored component:

1. Declare `CameraConfig` in `Pingu.Core/Authoring/` (beside `WaterRendererConfig`), `[Guid]` from
   `uuidgen` + `[Authored(DisplayName = "Camera")]`, plain setters, real defaults, semantic
   attributes (`[Meters]`, `[Seconds]`, `[Radians]`, `[AuthorRange]`, `[AuthorDoc]`) rather than
   Godot-specific hints. `FollowCamera` currently hard-codes its offset/FOV/smoothing in its
   constructor (`Pingu.Presentation/FollowCamera.cs`), so those are the fields.
2. Build **`Pingu.Core`** and confirm the component appears in `data/authoring-schema.json`.
3. Author it in Godot on a node and export `data/scenes/pool.json`.
4. Read it back: it will already be in `PinguLevel.AuthoredComponents` via `Materialize` — wire it
   through to `FollowCamera` in `GameHost`, and validate it in `GameConfig.ApplyScene`, which is
   where Pingu refuses an unplayable scene *by field name* instead of substituting defaults.

Ordering matters because of one detail: `PinguLevel` **throws on any unresolved component**, so a
scene exported with the camera component in it will refuse to load on every build that does not yet
know the record. Add the record and rebuild first; export second.

Also: keep field names short. Blender caps a stored property name at 63 characters and spends 32 on
its prefix, leaving roughly 31 — `FollowYawSmoothingSeconds` (25) fits, much longer will not. Pingu
is Godot-authored so this is not binding today, but the naming convention is shared across the
contract.

---

## File reference

- `/Users/quabug/proj/paradise-workspace/Pingu/Pingu.Core/Config/GameConfig.cs` — `[assembly: AuthoredRegistry]` (line 8); the "no config file any more" rationale; `ApplyPool` / `ApplyScene` validation
- `/Users/quabug/proj/paradise-workspace/Pingu/Pingu.Presentation/PinguLevel.cs` — line 163, the `scene authors components this build does not know` throw
- `/Users/quabug/proj/paradise-workspace/Pingu/Pingu.Core/Authoring/RoleAndRenderers.cs` — the precedent for a renderer-facing component living in Core
- `/Users/quabug/proj/paradise-workspace/Pingu/Pingu.Core/Pingu.Core.csproj` — line 35, `ParadiseAuthoringSchemaPath`
- `/Users/quabug/proj/paradise-workspace/Pingu/data/authoring-schema.json` — the six components this build knows
- `/Users/quabug/proj/paradise-workspace/Pingu/Pingu.Web/wwwroot/data/pingu/config.json` — the dead file (build output, unread, gitignored)
- `/Users/quabug/proj/paradise-workspace/Pingu/Pingu.Web/Pingu.Web.csproj` — `CopyAuthoredWebAssets`, "wwwroot IS BUILD OUTPUT"
- `/Users/quabug/proj/paradise-workspace/ParadiseEngine/src/Paradise.Export/Data/AuthoredComponentRouter.cs` — `Materialize` / `Resolve` / `ReadFrom`
- `/Users/quabug/proj/paradise-workspace/ParadiseEngine/src/Paradise.Authoring.Generators/AuthoredRegistryGenerator.cs` — the `[Authored]`-keyed, `[assembly: AuthoredRegistry]`-gated emission
- `/Users/quabug/proj/paradise-workspace/ParadiseEngine/src/Paradise.Authoring.Generators/AuthoringSchemaGenerator.cs` — PAUT005 / PAUT006
- `/Users/quabug/proj/paradise-workspace/ParadiseEngine/src/Paradise.Authoring/build/Paradise.Authoring.targets` — the schema auto-dump
- `/Users/quabug/proj/paradise-workspace/ShiningPie/ShiningPie.Core/Config/GameConfig.cs` — line 176, the *other* "not one this build knows"; the model config reader
- `/Users/quabug/proj/paradise-workspace/ShiningPie/ShiningPie.Core/Config/TuningComponents.cs` — line 165, an existing `CameraConfig` tuning group
