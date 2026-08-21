# Patrol routes in ShiningPie

Read-only design against the real repos. Nothing under `paradise-workspace/` was modified.

---

## 1. The shape, and why it is two components plus the hierarchy

A patrol route is **one entity carrying behavior, with its points as child entities**:

```
Patrol.AlleySweep          ← PatrolRouteMarker { Mode = PingPong, SpeedScale = 0.45 }
├── Patrol.AlleySweep.A    ← PatrolWaypointMarker { Step = 0 }
├── Patrol.AlleySweep.B    ← PatrolWaypointMarker { Step = 1 }
└── Patrol.AlleySweep.C    ← PatrolWaypointMarker { Step = 2 }
```

Component declarations: **`PatrolRoute.cs`** (in this folder). In the repo they belong at the end
of `/Users/quabug/proj/paradise-workspace/ShiningPie/ShiningPie.Core/Authoring/SceneComponents.cs`
— that file is the whole scene vocabulary, and `[assembly: AuthoredRegistry]` at its top already
pulls in every `[Authored]` record it contains. GUIDs above were generated with `uuidgen`, per
`AuthoredAttribute`'s own instruction.

### Why the waypoints are not `List<Vector3> Waypoints` on the route record

This is the load-bearing decision, and it is not a matter of taste in this repo:

- **The Blender addon cannot author an array.**
  `/Users/quabug/proj/paradise-workspace/ParadiseBlenderEditor/paradise_blender/contract/authoring.py`,
  in `flatten()` / `_flatten_into()`: a schema field of type `array` never becomes an editable
  leaf. If its items are `authoredBy` a host object it becomes a host-reference list (that is how
  `collider` gets its shapes); otherwise it becomes `HostRef(kind="rows")` — the docstring says it
  outright: *"an array is only a host-reference list (a list of typed rows has no author asking
  for it yet)"*. A `List<Vector3>` on the record would show in the Components panel as a hole the
  host cannot fill, and would export as nothing.
- **Even if it could, it would be the wrong authoring surface.** A point on a path is a thing you
  drag against the geometry it has to clear. Every other component in `SceneComponents.cs` already
  works this way: an anchor's position is its transform, a guide's yaw is its rotation and its
  radius its scale, an anchor's solidity is whether it has a collider. Typing coordinates into a
  form next to a 3D view that already shows them is the pattern that file exists to avoid.
- **The contract's only cross-entity reference is parenting.** `AuthoredByHost` covers shape,
  mesh, sprite and asset — there is no "entity reference" kind. `LevelEntityData.Parent` is the
  one link the export can carry, and the Blender exporter fills it
  (`export/entity.py`: `parent=EntityParentData(id=parent_entity.name)`, with `id=obj.name`).

`Step` is an explicit int rather than derived from export order because the addon emits entities
**sorted by name** (`authoring/entity.py:entity_objects`, deliberately, so an unchanged scene
re-exports byte-identically). Ordering by name would be binding by name, which `SceneBinding`'s
class doc forbids. Note the distinction: reading `Parent.Id` is not name-binding — it is the
contract's spelling of *hierarchy*, and no name appears in game code.

---

## 2. How `SceneBinding` reads it back

All of this goes in
`/Users/quabug/proj/paradise-workspace/ShiningPie/ShiningPie.Core/SceneBinding.cs`.

### 2a. The bound form

```csharp
    /// <summary>
    /// One authored patrol route: the path enemies posted to it walk when nothing is chasing.
    ///
    /// <b>The waypoints are the scene's hierarchy, resolved.</b> Each is a child entity of the
    /// route empty, and its position is that child's world translation — so a route is dragged,
    /// extended and re-shaped in Blender with no code aware of any particular path. Ordered by
    /// the authored <c>Step</c>, which the export cannot supply on its own (entities come out
    /// sorted by name).
    ///
    /// <see cref="SpeedScale"/> multiplies the pack's <c>Enemy.MaxSpeed</c> from config.json
    /// rather than replacing it: the speed of a Faceless One stays a balance number in one
    /// place, and this says only that THIS path is walked at a fraction of it.
    /// </summary>
    /// <param name="Walkers">The entity GUIDs of the enemies parented to this route. GUIDs
    /// rather than actor indices because that is the identity the layout hands out everywhere
    /// else (live-preview patches address entities by GUID for the same reason), and
    /// <see cref="GameRunner"/> already indexes actors by it.</param>
    public readonly record struct PatrolRouteInfo(
        PatrolMode Mode,
        float SpeedScale,
        string Name,
        Guid Guid,
        IReadOnlyList<Vector3> Waypoints,
        IReadOnlyList<Guid> Walkers);
```

On `SceneLayout`, alongside `Containers` / `Anchors` / `CameraGuides`:

```csharp
        /// <summary>Authored patrol routes, in entity order. Never null — a scene with no
        /// patrols is the normal case (every hand-built test layout), and every caller
        /// iterating this should not have to say so.</summary>
        public IReadOnlyList<PatrolRouteInfo> PatrolRoutes { get; init; } = PatrolRoutes ?? [];
```

(with the matching optional `IReadOnlyList<PatrolRouteInfo>? PatrolRoutes = null` primary-
constructor parameter, exactly as `CameraGuides` is declared).

### 2b. Two passes, because the export is name-sorted

A child can be emitted **before** its parent, so nothing can be stitched inside the entity loop.
The loop collects drafts; one pass after it resolves them.

```csharp
    /// <summary>A route under construction: the marker plus the children that have named it so
    /// far. A class, not a struct — it is mutated as the entity walk finds its waypoints, and
    /// the walk cannot find them in order.</summary>
    private sealed class PatrolRouteDraft(PatrolRouteMarker marker, LevelEntityData entity)
    {
        public readonly PatrolRouteMarker Marker = marker;
        public readonly string Name = entity.Id;
        public readonly Guid Guid = entity.EntityGuid;
        public readonly List<(int Step, string Name, Vector3 Position)> Waypoints = [];
        public readonly List<Guid> Walkers = [];
    }
```

Inside `Bind`, beside the other accumulators:

```csharp
        var patrolRoutes = new List<PatrolRouteDraft>();
        var patrolRouteById = new Dictionary<string, PatrolRouteDraft>(StringComparer.Ordinal);
        var patrolWaypoints = new List<(string? Parent, int Step, string Name, Vector3 Position)>();
        var patrolWalkers = new List<(string Parent, Guid Guid)>();
        // Entities the author switched OFF, so a waypoint left behind by a disabled route can be
        // told apart from one that was never parented to a route at all — see BindPatrolRoutes.
        var inactive = new HashSet<string>(StringComparer.Ordinal);
```

The one existing line that changes, so the inactive ids are recorded rather than skipped
silently:

```csharp
        //  was:  if (containerTables is null || !entity.IsActive) { continue; }
        if (containerTables is null)
        {
            continue;
        }
        if (!entity.IsActive)
        {
            inactive.Add(entity.Id);
            continue;
        }
```

Then, in the same `authored`/`model`/`radius` block the anchor, guide, container and camera
already use:

```csharp
            if (authored.OfType<PatrolRouteMarker>().FirstOrDefault() is { } routeMarker)
            {
                var draft = new PatrolRouteDraft(routeMarker, entity);
                patrolRoutes.Add(draft);
                if (!patrolRouteById.TryAdd(entity.Id, draft))
                {
                    // Ids are Blender object names, which are unique per scene — a duplicate
                    // means a document no editor here produces, and the parent links in it are
                    // ambiguous. Refuse rather than let insertion order pick a route.
                    throw new InvalidDataException(
                        $"{source}: two entities share the id '{entity.Id}', so a patrol "
                        + "waypoint parented to it names no single route.");
                }
            }

            if (authored.OfType<PatrolWaypointMarker>().FirstOrDefault() is { } waypointMarker)
            {
                patrolWaypoints.Add(
                    (entity.Parent?.Id, waypointMarker.Step, entity.Id, model.Translation));
            }

            // An enemy joins a route by being parented to it — the contract's only cross-entity
            // reference. An enemy parented to something else (a prop, a room root) is ordinary
            // scene tidiness and simply walks no route.
            if (entity.Parent is { Id: var parentId }
                && authored.OfType<EnemyMarker>().Any())
            {
                patrolWalkers.Add((parentId, entity.EntityGuid));
            }
```

and at the bottom, beside the other validators:

```csharp
        Validate(actors, source);
        ValidateAnchors(anchors, source);
        ValidateGuides(guides, source);
        var patrols = BindPatrolRoutes(
            patrolRoutes, patrolRouteById, patrolWaypoints, patrolWalkers, inactive, source);
        return new SceneLayout(actors, obstacles)
        {
            // ... existing initializers ...
            PatrolRoutes = patrols,
        };
```

### 2c. The stitch, which is also the validation

```csharp
    /// <summary>
    /// Attach each waypoint and each posted enemy to the route it is parented to, and refuse the
    /// authoring mistakes that would otherwise surface as an enemy standing still.
    ///
    /// Separate from the entity walk because the export is sorted by NAME, so a waypoint is as
    /// likely to be read before its route as after it. Everything here is therefore a second
    /// pass over drafts, not a lookup during the first.
    /// </summary>
    private static IReadOnlyList<PatrolRouteInfo> BindPatrolRoutes(
        List<PatrolRouteDraft> routes,
        Dictionary<string, PatrolRouteDraft> routeById,
        List<(string? Parent, int Step, string Name, Vector3 Position)> waypoints,
        List<(string Parent, Guid Guid)> walkers,
        HashSet<string> inactive,
        string source)
    {
        foreach (var waypoint in waypoints)
        {
            if (waypoint.Parent is { } parent && routeById.TryGetValue(parent, out var route))
            {
                route.Waypoints.Add((waypoint.Step, waypoint.Name, waypoint.Position));
                continue;
            }

            // A whole route switched off in Blender takes its children with it — that is a
            // deliberate authoring action, not a mistake, and the waypoints simply vanish with
            // the route they belonged to.
            if (waypoint.Parent is { } disabled && inactive.Contains(disabled))
            {
                continue;
            }

            // Everything else is a waypoint that will never be walked. Refused rather than
            // dropped: silently ignoring it is how a patrol quietly loses a corner and nobody
            // finds out until an enemy walks through a wall.
            throw new InvalidDataException(
                $"{source}: patrol waypoint '{waypoint.Name}' is not parented to an entity "
                + "authoring a patrol route. Parent it to the route empty in Blender.");
        }

        foreach (var (parent, guid) in walkers)
        {
            if (routeById.TryGetValue(parent, out var route))
            {
                route.Walkers.Add(guid);
            }
        }

        var bound = new List<PatrolRouteInfo>(routes.Count);
        foreach (var route in routes)
        {
            // Sorted by the authored Step, which is the ONLY thing that carries walking order —
            // and the duplicate refusal below is what makes this ordering total, so an unstable
            // sort cannot make two exports of one scene disagree.
            route.Waypoints.Sort(static (a, b) => a.Step.CompareTo(b.Step));

            for (var i = 1; i < route.Waypoints.Count; i++)
            {
                if (route.Waypoints[i].Step == route.Waypoints[i - 1].Step)
                {
                    throw new InvalidDataException(
                        $"{source}: patrol route '{route.Name}' has two waypoints at step "
                        + $"{route.Waypoints[i].Step} ('{route.Waypoints[i - 1].Name}' and "
                        + $"'{route.Waypoints[i].Name}'). The order has to be decidable.");
                }
            }

            if (route.Waypoints.Count < 2)
            {
                // One point is a post, not a path, and the game already has posts: an enemy with
                // nowhere to walk is exactly the "authored, exported, and silently never active"
                // failure ValidateGuides refuses for a zero-radius guide.
                throw new InvalidDataException(
                    $"{source}: patrol route '{route.Name}' has {route.Waypoints.Count} "
                    + "waypoint(s). A route is at least two — parent more empties to it in "
                    + "Blender, or delete the route.");
            }

            var scale = route.Marker.SpeedScale;
            if (!float.IsFinite(scale) || scale <= 0f)
            {
                // Not clamped. [AuthorRange] is advisory everywhere in this game; a zero here is
                // an enemy frozen mid-corridor, which reads as a broken build rather than as a
                // slow patrol.
                throw new InvalidDataException(
                    $"{source}: patrol route '{route.Name}' has SpeedScale {scale}. It "
                    + "multiplies the pack's speed, so it must be positive.");
            }

            bound.Add(new PatrolRouteInfo(
                route.Marker.Mode,
                scale,
                route.Name,
                route.Guid,
                [.. route.Waypoints.Select(w => w.Position)],
                route.Walkers));
        }

        return bound;
    }
```

Three properties worth calling out, all inherited rather than invented:

- **Bound only when `containerTables` is non-null**, like anchors, containers and the camera —
  the live-preview re-bind path patches none of them and must not half-resolve a hierarchy.
- **Refusals name the entity and say what to do in Blender.** Every existing message in this file
  does; it is the difference between a load error and a scavenger hunt.
- **Deterministic.** Route order is entity order (name-sorted by the exporter), waypoint order is
  a total order on `Step`. Same `.blend` ⇒ same layout ⇒ same run for a seed.

---

## 3. The hop into gameplay — sketch, not part of the answer above

`SceneLayout` is where the exercise ends; this is what consumes it, kept short because the shapes
are already fixed by `Ecs/Components.cs` and `GameRunner`.

```csharp
/// <summary>Where an enemy is on its patrol route, and which way round it. Stamped by
/// GameRunner's managed step — the same outside-the-schedule exemption ChaseWaypoint uses,
/// because the waypoint list is managed data a ref-struct system cannot hold.</summary>
[Component]
public partial struct PatrolLeg
{
    public int Route;       // index into the layout's PatrolRoutes; -1 for an unposted enemy
    public int Index;       // which waypoint it is walking toward
    public int Direction;   // +1 / -1; only PingPong ever flips it
}

/// <summary>The point the patrol wants next. Valid != 0 replaces the walk-home goal.</summary>
[Component]
public partial struct PatrolTarget
{
    public Vector3 Value;
    public int Valid;
}

/// <summary>Multiplies this actor's top speed. 1 for everything that is not patrolling, so
/// MotionSystem needs no branch.</summary>
[Component]
public partial struct ActorSpeedScale
{
    public float Value;
}
```

Three touch points, each one line-ish:

1. **`GameRunner.CreateWorldWithSchedule`** resolves `PatrolRouteInfo.Walkers` through the actor
   index it already builds (`_actorIndexByGuid`) and stamps each walker's `PatrolLeg.Route` and
   `ActorSpeedScale.Value = route.SpeedScale`. Everything else gets `ActorSpeedScale.Value = 1f`.
2. **A managed step beside `PlanChaseWaypoints`** advances the leg when the enemy is within
   arrival distance of its current waypoint and stamps `PatrolTarget`. `Loop` wraps
   `(Index + 1) % Count`; `PingPong` flips `Direction` at each end. This is where the two modes
   actually differ — and it is the only place, which is the point of making `Mode` a closed enum
   rather than two components.
3. **`EnemySystem.Steer`** already has the branch: out of aggro it seeks `ActorSpawn[i]`. It seeks
   `PatrolTarget[i]` instead when `Valid != 0`, falling back to the spawn — so an enemy with no
   route behaves exactly as it does today. **`MotionSystem`** line 86 becomes
   `EnemyTuning.MaxSpeed.Value * Actors.ActorSpeedScale[i].Value`, which is where the multiplier
   lands.

Determinism is untouched: the leg advances from position and tick, and nothing rolls.

---

## 4. What else the change actually costs

- **No engine change, so no package bump.** Both records are game-side and use vocabulary
  `Paradise.Authoring` already ships; the CI "published artifacts only" trap does not apply here.
- **`data/authoring-schema.json` is regenerated by the build** (`ParadiseAuthoringSchemaPath` in
  `ShiningPie.Core.csproj`) and is committed — the addon's Components panel reads it, so the
  re-dump must be part of the same commit or Blender cannot author the new components.
- **Authoring it by script** goes in `authoring/build_zone.py` beside `camera_guide()`:
  `attach_component(obj, "a7799321-e0fa-484d-9f80-27421b796bc7", Mode="PingPong", SpeedScale=0.45)`
  on the route empty, then a child empty per point. (The `COMPONENT_*` constants at the top of
  that file still hold the old `shiningpie.*` strings from before ids became GUIDs; the addon
  stores GUID strings — `contract/component_ids.py` — so a new constant should be the GUID.)
- **Tests** belong in `ShiningPie.Tests/SceneBindingTests.cs`, in the style already there
  (`Fixture.Entity(id, typeof(T).GUID, payload)` over a `Minimal()` level, plus a `Parent`): a
  route binding its waypoints in `Step` order regardless of entity order; an orphan waypoint
  refused by name; a duplicate step refused; a one-waypoint route refused; a zero `SpeedScale`
  refused. `Fixture.Entity` currently sets no `Parent`, so it needs an optional parameter for it.
