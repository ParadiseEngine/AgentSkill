# "Not a component this build knows" — debugging path for Pingu

## First, the premise needs correcting: there is no `config.json`

Nothing in Pingu reads a `data/pingu/config.json`. It was deleted when every tunable moved onto the
`[Authored]` component that owns it. The only surviving mentions are stale doc comments:

- `/Users/quabug/proj/paradise-workspace/Pingu/Pingu.Presentation/PinguLevel.cs:17`
- `/Users/quabug/proj/paradise-workspace/Pingu/Pingu.Core/Config/GameConfig.cs:18`
- `/Users/quabug/proj/paradise-workspace/Pingu/Pingu.Tests/ConfigTests.cs:12` — *"These used to load
  data/pingu/config.json. There is no config file any more."*

Both hosts load exactly one authored document, the **scene**:

- `/Users/quabug/proj/paradise-workspace/Pingu/Pingu.Launcher/Program.cs:94` → `data/scenes/pool.json`
- `/Users/quabug/proj/paradise-workspace/Pingu/Pingu.Web/Program.cs:91` → `data/scenes/pool.json`

So either you edited the scene export and called it config.json, or you created a `config.json` that
nothing opens and the error is coming from the scene. **Check which file the failure is actually
about before anything else** — if it is a file you created, the fix is "put the component in the
scene", not "debug the registry".

## Where the message comes from

`/Users/quabug/proj/paradise-workspace/Pingu/Pingu.Presentation/PinguLevel.cs:156-165`:

```csharp
if (unresolved.Count > 0)
{
    throw new InvalidDataException(
        $"scene authors components this build does not know: "
        + string.Join(", ", unresolved.Select(c => c.Type ?? c.Id.ToString())));
}
```

`unresolved` is filled by `AuthoredComponentRouter.Materialize`
(`/Users/quabug/proj/paradise-workspace/ParadiseEngine/src/Paradise.Export/Data/AuthoredComponentRouter.cs:114-135`)
for every payload that `Resolve` returned null for.

**Read the text of your error before doing anything else.** It prints `c.Type ?? c.Id`:

- It names a **CLR type** (`Pingu.CameraConfig`) → the payload carries a `Type` field; resolution
  failed on both the GUID and the name.
- It names a **bare GUID** → the payload has no `Type` at all. That only happens with a hand-edited
  or hand-generated document; the Godot addon always writes both
  (`ParadiseGodotEditor/Paradise.Godot.Editor/Authoring/AuthoredEntityCore.cs:903-911`). That alone
  is most of the diagnosis.

## Step 1 (do this first) — is your GUID in the built registry?

This bisects the entire problem space and needs no build. The registry is source-generated and emits
one static field per component named `Id_<Namespace>_<Type>`, so the built assembly literally
contains them:

```bash
cd /Users/quabug/proj/paradise-workspace/Pingu
strings -a Pingu.Core/bin/Debug/net10.0/Pingu.Core.dll | grep -E 'Id_Pingu_|Read_Pingu_'
strings -a Pingu.Core/bin/Debug/net10.0/Pingu.Core.dll | grep -i '<your-guid-prefix>'
```

Today that prints `Id_Pingu_PoolConfig`, `Read_Pingu_PoolConfig`, … and
`$fad4d14d-2d8b-4d36-a8d9-36cdb7b874c1`. If your camera component is not in that list, the registry
never learned it → **Step 2**. If it *is* in the list, the registry knows it and the message is
misleading you → **Step 6**.

(Equivalent at runtime: print `Pingu.AuthoredComponents.Default.ComponentIds`.)

## Step 2 — is the record in the right assembly?

`PinguLevel.cs:124` consults exactly one game registry: `Pingu.AuthoredComponents.Default`.

That class is generated **only into the assembly carrying `[assembly: AuthoredRegistry]`**, and only
into that project's `RootNamespace`. In Pingu that is one place:
`Pingu.Core/Config/GameConfig.cs:8` (`RootNamespace` = `Pingu`, set in `Pingu.Core.csproj:22`).

The gate is `AuthoredRegistryGenerator.Initialize`
(`ParadiseEngine/src/Paradise.Authoring.Generators/AuthoredRegistryGenerator.cs:110-138`): not opted
in → `Emit` is never called, **and PAUT002/003/004 are suppressed too**, so you get no diagnostic
either.

**Why this is my top structural suspect for a camera:** camera tuning is a presentation concern, so
the natural place to drop the record is beside
`/Users/quabug/proj/paradise-workspace/Pingu/Pingu.Presentation/FollowCamera.cs` (which today
hardcodes `fovDegrees = 50f`, `smoothing = 0.25f`, `offset = (0, 5, -7)` — exactly the numbers
someone would want to author). `Pingu.Presentation.csproj` has **no** `[assembly: AuthoredRegistry]`
and `RootNamespace = Pingu.Presentation`. A record declared there gets a *schema* (the schema
generator is not opt-in) but **no reader** — so it shows up in the Godot inspector, exports fine, and
then fails to load with precisely your message.

**Fix:** declare it in `Pingu.Core`, next to
`/Users/quabug/proj/paradise-workspace/Pingu/Pingu.Core/Authoring/*.cs`. `Pingu.Presentation` already
references `Pingu.Core`, so `FollowCamera` can still consume it.

Check with:
```bash
grep -rn "\[Authored" --include='*.cs' /Users/quabug/proj/paradise-workspace/Pingu | grep -v /obj/ | grep -v /bin/
```
Every hit must be under `Pingu.Core/`.

## Step 3 — does the record have a usable, unique `[Guid]`?

Identity is the BCL `[Guid]`, not the type name
(`ParadiseEngine/src/Paradise.Authoring.Generators/AuthoredModel.cs:106-135`). Two ways to lose it,
both ending in your symptom:

- **No `[Guid]`, or a malformed one** → `IdUnusable` → the registry generator **skips the component
  silently** (`AuthoredRegistryGenerator.cs:157-163`, deliberately: the diagnostic belongs to the
  other generator). `PAUT005` is raised by `AuthoringSchemaGenerator`.
- **A duplicated `[Guid]`** → `claimed.Add(type.ComponentId)` fails and the loser is dropped from
  *both* schema and registry. `PAUT006`. This is the classic copy-paste: `PoolConfig` is the
  documented example everyone copies, and it carries `fad4d14d-2d8b-4d36-a8d9-36cdb7b874c1`
  (`GameConfig.cs`, above the `PoolConfig` record).

Both are `DiagnosticSeverity.Error`, so a clean build would have failed — which makes this check
double as "did my build actually succeed and re-run the generator, or am I running a stale binary?"

```bash
grep -rn 'Guid("' --include='*.cs' /Users/quabug/proj/paradise-workspace/Pingu/Pingu.Core
python3 -c "import json;[print(c['id'],c['type']) for c in json.load(open('/Users/quabug/proj/paradise-workspace/Pingu/data/authoring-schema.json'))['components']]"
```

The schema today lists six components and **no camera**:
`Pingu.BuddyConfig`, `Pingu.LedgeConfig`, `Pingu.PenguinConfig`, `Pingu.PlayerConfig`,
`Pingu.PoolConfig`, `Pingu.WaterRendererConfig`.

## Step 4 — is the schema file stale? (this is what the *editor* reads)

`data/authoring-schema.json` is a committed **file** re-dumped by an MSBuild target after a build of
`Pingu.Core` (`ParadiseAuthoringSchemaPath` in `Pingu.Core.csproj:36`;
`ParadiseEngine/src/Paradise.Authoring/build/Paradise.Authoring.targets`). It is
`Inputs`/`Outputs`-gated and **skipped entirely under `ContinuousIntegrationBuild`**.

The Godot addon builds its "Add Component" picker by merging the engine's compiled-in schema with
that file (`AuthoredEntityCore.cs:283-327`), and it only re-reads the file when its mtime+length
change (`RefreshSchemaIfChanged`, `AuthoredEntityCore.cs:216-250`).

Consequences to check, in order:

1. `dotnet build Pingu.Core/Pingu.Core.csproj` (from **inside** `Pingu/`) and confirm the schema
   gains your component. If it does not, you are still in Step 2 or 3.
2. Run the drift test — the single fastest "editor and build disagree" check:
   `AuthoringSchemaTests.TheCommittedSchemaMatchesWhatTheGeneratorEmits`
   (`/Users/quabug/proj/paradise-workspace/Pingu/Pingu.Tests/AuthoringSchemaTests.cs:86`).
3. **Re-export the scene from Godot only after that rebuild.** If you exported first, the addon was
   holding the old schema and either omitted the component or wrote a stale id.

## Step 5 — do the ids in the JSON and the code actually match?

Resolution is id-first, type-name second, and never guesses
(`AuthoredComponentRouter.ReadOrThrow`, `AuthoredComponentRouter.cs:180-195`):

```csharp
if (component.Id != Guid.Empty && registry.TryRead(component.Id, ...)) return byId;
if (!string.IsNullOrWhiteSpace(component.Type) && registry.TryReadByType(component.Type!, ...)) return byType;
return null;
```

So "does not know" means **both** failed. Things that break the second, repair-path attempt:

- `TryReadByType` is emitted as a C# `switch` over string literals
  (`AuthoredRegistryGenerator.cs:216-230`) → **ordinal, case-sensitive, fully-qualified**. `"Pingu.CameraConfig"`
  matches; `"Pingu.Presentation.CameraConfig"`, `"pingu.cameraconfig"`, or a trailing space does not.
- You regenerated the `[Guid]` after exporting, so the document carries the old one.
- You hand-wrote the entry and omitted `"Type"` entirely — then there is no fallback at all.

Compare directly:
```bash
python3 -c "
import json;d=json.load(open('/Users/quabug/proj/paradise-workspace/Pingu/data/scenes/pool.json'))
[print(e['Id'], c.get('Id'), c.get('Type')) for e in d['Entities'] for c in e.get('Components',[])]"
```
against the `[Guid]` in your record and its fully-qualified name.

## Step 6 — if the id IS registered, the message is lying to you

This is the trap. `AuthoredComponentRouter.ReadFrom`
(`AuthoredComponentRouter.cs:163-178`) swallows **two** exception types and returns null:

```csharp
catch (JsonException)            { return null; }   // not valid JSON for this record
catch (InvalidOperationException){ return null; }   // a field of the wrong KIND
```

The generated readers parse `JsonElement` directly rather than through a serializer, so a *string
where a float belongs*, a *number where a bool belongs*, or an *array where a scalar belongs* throws
`InvalidOperationException` from `GetSingle()`/`GetBoolean()` — and the payload lands in `unresolved`
and is reported as *"this build does not know"* even though the id resolved perfectly. The addon has
a comment about exactly this hazard at `AuthoredEntityCore.cs:876-881` (a bool arriving as `0` makes
the whole component unreadable).

Hand-written JSON is the way you hit this. Discriminate in 30 seconds with a scratch test in
`Pingu.Tests` modelled on
`/Users/quabug/proj/paradise-workspace/Pingu/Pingu.Tests/AuthoredInstanceTests.cs:83-99`, but calling
the registry **without** the router's try/catch so the real exception surfaces:

```csharp
var data = JsonDocument.Parse(yourPayloadJson).RootElement.Clone();
var known = Pingu.AuthoredComponents.Default.TryRead(new Guid("<your-guid>"), data, out var c);
// known == true + a thrown InvalidOperationException  => Step 6 (payload type mismatch)
// known == false                                       => Steps 2-5 (registry does not have it)
```

Then check every field: `"Distance": 7` (number) not `"7"`; `"Enabled": true` not `1`;
`Vector3` as `[x,y,z]`; enums as their **member name** string (or the underlying integer).

## Step 7 — "group": composed part vs. top-level component

Your word *group* matters, because the two fail differently:

- A **composed group** (a nested record property, like `LedgeConfig.Box` →
  `Pingu.Core/Authoring/BoxColliderConfig.cs`) needs **no `[Guid]`** — it is a part, not a component
  (`AuthoredModel.cs:130-135`). On the wire it is a **nested object**: the addon writes
  `component/field/subfield` paths through `Write()`
  (`AuthoredEntityCore.cs:955-969`). A *flattened* hand-written payload does **not** error — absent
  properties keep the record's own initializers — so you would see silent defaults, not your message.
- A **top-level component** must have `[Authored]` + `[Guid]` and appear as its own entry in
  `Entities[].Components[]`.

So: if you meant a group and nested it inside an existing component's payload, you would be debugging
"my values are ignored", not this error. Getting *this* error means the loader saw a top-level
`Components[]` entry whose id/type it could not resolve. If the camera tuning was meant to be a
group, the bug may simply be that it was written as a sibling component entry.

Also confirm the reader-shape rules while you are there (`AuthoredRegistryGenerator.cs:38-90`): public
parameterless constructor, plain `{ get; set; }` — **no positional record, no `init`, no `required`**,
for the group type as well as the component (PAUT002/003/004). These are build errors, so they only
bite you if you are running a stale binary.

## Step 8 — workspace-specific staleness traps

1. **Three artifacts must agree**: the compiled registry in `Pingu.Core.dll`, the committed
   `data/authoring-schema.json`, and the exported `data/scenes/pool.json`. Rebuild → re-dump →
   re-export, in that order.
2. **The engine-source override.** `Pingu.Core.csproj` declares `Paradise.Authoring` **0.17.0**, but
   `/Users/quabug/proj/paradise-workspace/Directory.Build.targets` locally swaps `Paradise.*` for
   ProjectReferences into `ParadiseEngine/src/`. Generators arrive differently from a ProjectReference
   than from a package, so verify the generator actually ran, and compare against
   `-p:ParadiseUseEngineSource=false`.
3. **Never build through a symlinked path.** Building from `pingu-workspace/` as `Pingu/…` silently
   disables the override (it is keyed on the physical project directory) and gives you a green build
   against the *packages* — a registry compiled by a different generator version than you think. Use
   `../Pingu/…` or `cd` in first. Confirm with
   `dotnet build Pingu.Core/Pingu.Core.csproj -getProperty:ParadiseUseEngineSource` → `true`.
4. `git status` in `/Users/quabug/proj/paradise-workspace/Pingu` is currently **clean** at commit
   `c9dcffd "Identify components by [Guid], and read them from the list (#17)"` — so whatever you
   added is not in this checkout. Worth confirming you are debugging the tree you think you are.

## Summary — shortest path

1. Read the error text: type name or bare GUID? (bare GUID ⇒ hand-edited payload, no `Type` fallback)
2. `strings Pingu.Core.dll | grep Id_Pingu_` — is your component in the registry at all?
3. **Not there** → is the record in `Pingu.Core` (the only assembly with `[assembly: AuthoredRegistry]`)?
   → does it have a unique, well-formed `[Guid]`? → did the build succeed and re-dump the schema?
4. **There** → the message is misleading: the payload has a field of the wrong JSON kind. Call
   `TryRead` without the router's catch and read the real exception.
5. Either way, finish by rebuilding `Pingu.Core`, confirming `data/authoring-schema.json` gained the
   component, re-exporting `data/scenes/pool.json` from Godot, and running `AuthoringSchemaTests`.
