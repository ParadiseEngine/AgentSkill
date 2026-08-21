# The authored-component contract

The engine's export contract, and the rules that are easy to get wrong. Everything here lives in
`ParadiseEngine/src/Paradise.Export/` and `src/Paradise.Authoring/`.

## Contents

- [Declaring a component](#declaring-a-component)
- [The document shape](#the-document-shape)
- [Reading and writing](#reading-and-writing)
- [`typeof(T).GUID` and its silent failure](#typeoftguid-and-its-silent-failure)
- [Schema versions and why there is no upgrade path](#schema-versions-and-why-there-is-no-upgrade-path)
- [Registries](#registries)
- [AOT constrains the design](#aot-constrains-the-design)
- [Adding an engine component](#adding-an-engine-component)

## Declaring a component

A plain record, a generated GUID, and a display name:

```csharp
[Guid("b7ab4dd8-c8da-4dc2-9e5e-192fd74deb11")]
[Authored(DisplayName = "Rigidbody")]
public sealed record RigidbodyComponentData { public float Mass { get; set; } … }
```

Generate the GUID with `uuidgen`. Never hand-type one and never continue a visible pattern — the
engine's own ids were regenerated once precisely because they were a counted sequence, which reads
as an invitation to type the next number and collide.

`[Authored]` takes **no id**. Identity comes from the BCL's `[Guid]`, so a type cannot carry two
different identities. A missing or malformed one is **PAUT005** at compile time.

Do not add a constants class collecting ids. `typeof(T).GUID` *is* the constant, and a second copy
is one rename away from drifting.

## The document shape

An entity carries a single flat list. There is no engine tier:

```json
{
  "Id": "Car",
  "Components": [
    { "Id": "f2c0357e-…", "Type": "Paradise.Export.Data.RenderableComponentData", "Data": { "Mesh": "Models/Car.glb" } },
    { "Id": "701e1037-…", "Type": "ShiningPie.Authoring.CarMarker", "Data": {} }
  ]
}
```

- **`Id`** — the component's `[Guid]`, canonical lowercase-hyphenated ("D" format). System.Text.Json
  accepts only that shape; the 32-char and braced spellings throw on read.
- **`Type`** — fully qualified CLR name. Optional on the wire, but **write it always**: it is read
  only when the id fails to resolve, and a payload carrying a bare GUID and nothing else diagnoses
  nothing to whoever is staring at it. Copy it verbatim from the schema — the fallback match is
  exact and case-sensitive, so never synthesize it.
- **`Data`** — the serialized record, exactly as the editor wrote it.

An entity that authors nothing has an **empty array**, not a set of nulls.

**Identity is the exception.** `IdentityComponentData` is spread onto `LevelEntityData`'s own
fields (`Kind`, `IsActive`, `InitialAnimation`, `Prefab`, and DisplayName/SpawnPhase when
non-blank) and leaves no entry. It is what an entity *is*, not something it has.

**Casing differs per document**, and mixing them is a real bug: the level document is
**PascalCase**; the authoring schema is **camelCase**.

## Reading and writing

```csharp
entity.Get<RenderableComponentData>()?.Mesh          // null when absent
entity.Has<RigidbodyComponentData>()                 // presence without deserializing
entity.Entries<ColliderComponentData>()              // all entries — a list does not enforce one
entity.Set(new RigidbodyComponentData { … })         // replaces an existing entry for that id
LevelEntityExtensions.Entry(record)                  // build an entry without attaching it
AuthoredComponentRouter.Materialize(entity, registry, unresolved)   // everything, once
```

`Get<T>` deserializes on each call. That is fine at load time — where every consumer in this
workspace reads — but hold the result if you are in a loop.

Prefer `Materialize` in a game host: it returns every component in one pass and reports what it
could not resolve, so a component nobody wrote an accessor for is not silently never read.

## `typeof(T).GUID` and its silent failure

`Type.GUID` returns the `[Guid]` attribute's value when present. When **absent**, it returns a
GUID the runtime derives from the type name and assembly — **not `Guid.Empty`**. So
`Get<SomethingUntagged>()` compiles, looks up an id nothing ever wrote, and returns `null`
forever.

Every authored record carries the attribute (PAUT005 enforces it), so this only bites a caller
reaching for a type that was never an authored component. Worth knowing, because nothing tells
you.

## Schema versions and why there is no upgrade path

Two independent versions, both currently **3**:

| Version | Where | Meaning |
|---|---|---|
| `LevelData.CurrentSchemaVersion` | `LevelDocument.cs` | the exported scene document |
| `AuthoringSchemaDocument.CurrentVersion` | `Schema/AuthoringSchemaDocument.cs` | the schema editors read to build their UI |

Both set `MinimumSupportedVersion == CurrentVersion`, deliberately. v2 keyed components by *name*,
and there is no way to derive a GUID from `paradise.rigidbody` — so an old document cannot be
upgraded, only regenerated. `ExportJsonReader.ReadLevel` refuses it, naming the version.

That refusal **peeks at `SchemaVersion` before deserializing the body**. Placed after, it never
fires: a v2 document dies inside STJ on the object-vs-array mismatch first and reports a token
position instead of what to do. Keep that ordering if you touch it.

Bumping a version means touching the C# constant, the Python mirror in
`ParadiseBlenderEditor/paradise_blender/contract/`, and the test fixtures that pin it.

## Registries

`[assembly: AuthoredRegistry]` asks the generator for an `AuthoredComponents` registry that
materializes that assembly's records from payloads. Both `Paradise.Export` and each game opt in.

`Resolve` consults this assembly's registry first, then the caller's — the same mechanism for
both. There is no "is this the engine's component" set any more; that question stopped having
consequences when the tiers merged, and reintroducing it would be a step backwards.

The generated readers parse `JsonElement` directly rather than through a serializer, so a field of
the wrong *kind* surfaces as `InvalidOperationException`, not `JsonException`. Anything catching
"this payload is not that component" must catch both, or one malformed payload throws out of
`Materialize` and costs the whole scene.

## AOT constrains the design

`Paradise.Export` is `IsAotCompatible` with `TreatWarningsAsErrors`. `Type.GetType(component.Type)`
is a **build error** (IL2057/IL3050), not merely a slower path. Any id→type dispatch must be a
closed, compile-time set — a generated registry or an explicit chain of `ReadElement<T>` calls.

## Adding an engine component

The point of the current design is that this is small:

1. Declare the record with `[Guid]` + `[Authored]`.
2. Rebuild — the generator re-dumps the authoring schema and the registry.
3. Regenerate the Blender addon's vendored copy (see `blender.md`).

No editor change, no router entry, no contract change. If a step is asking you to name the id
somewhere new, that is a signal to reconsider rather than to add a constant.
