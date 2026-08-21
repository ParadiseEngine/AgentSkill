# Patrol routes in ShiningPie

## What it is

Three declarations in `ShiningPie.Core/Authoring/SceneComponents.cs` (given in
`PatrolRoute.cs` beside this file), and about forty lines in
`ShiningPie.Core/SceneBinding.cs`. No engine change, no editor change, no GUID written down
anywhere but on the records themselves — which is the test of whether this design is being
worked with or against.

| Piece | Carries | Lives on |
|---|---|---|
| `PatrolRouteMarker` | `Route` name, `Speed` multiplier, `Mode` (Loop / PingPong) | one header empty per route |
| `PatrolWaypointMarker` | `Route` name, `Order` | one empty per point of the path |
| `PatrolMode` | `Loop`, `PingPong` | — |

Authoring it: drop an empty, attach **Patrol route**, name it `ArcadeLap`, set Speed 0.5 and
Mode Loop. Drop four more empties along the path, attach **Patrol waypoint** to each with
`Route = ArcadeLap` and Order 10/20/30/40. Save (the addon re-exports), and the route is in
`data/scenes/shiningpie.json`. Adding a fifth route later is Blender work only.

## Three decisions worth defending

**The path is entities, not an array on the header.** The generator does support
`Vector3[]`/`List<T>` fields, so a `Waypoints` array on `PatrolRouteMarker` would compile and
would reach the Blender panel. It would still be the wrong shape: the author places a patrol
point by dragging an empty in the viewport, and a coordinate typed into a panel silently stops
matching the geometry the first time someone moves a wall. Every other component in this file
already derives its geometry from the transform — the anchor's position, the camera guide's yaw
from its rotation and radius from its scale — so the path does too.

There is also a hard constraint behind it: the obvious alternative, making the waypoints
*children* of the route empty, is not available. The Blender exporter never populates the
contract's `Parent` field — all 194 entities in the shipped export have `"Parent": null` — so a
route cannot own its points by hierarchy in this game. They are joined by a name instead, and
the name is validated at load so a typo is an error rather than a patrol that quietly never
runs.

**`Route` is a free string, `Mode` is an enum.** The rule of thumb here is *an enum beats a free
string where the set is closed*. `Mode` is closed — two behaviors, defined by code that
implements them — so it is an enum and a typo is unrepresentable. Route names are not closed:
they are pure content, and a designer adding a patrol should not need a C# change to do it. That
is the same call `ContainerMarker.Table` makes, and it comes with the same obligation — the
string is cross-checked at bind time, so an unclaimed name fails loudly at load.

**The speed multiplier is allowed in the scene; an absolute speed would not be.**
`ShiningPie/AGENTS.md` keeps every balance number in `data/shiningpie/config.json`, and that
rule holds: the enemy's actual walk speed stays `EnemyConfig.MaxSpeed`. What the route carries is
how *this path* relates to it, which is structure the scene owns, exactly like `CameraRig.Fov`
riding with the pose it was framed against. Written as a multiplier, retuning enemies globally
still moves every patrol with them.

**Not in scope:** which enemy walks which route. Nothing here assigns one, because nothing asked
and the assignment rule is a design decision (nearest route at spawn, or an explicit
`Route` field on `EnemyMarker`). When it is wanted it is one property on `EnemyMarker` plus one
lookup — `SceneLayout.TryRoute` below is already the shape that serves it.

## Reading it back in SceneBinding

Four edits, all following the paths `AnchorMarker` and `CameraGuideMarker` already cut.

### 1. The layout record — beside `CameraGuideInfo`

```csharp
/// <summary>
/// One authored patrol route, stitched from its header entity and the waypoint entities that
/// name it. <see cref="Waypoints"/> is in authored <c>Order</c>, world-space, and always has at
/// least two entries — validation refuses anything less.
///
/// <see cref="Speed"/> is a multiplier on config's <c>Enemy.MaxSpeed</c>, not a speed:
/// the scene says how this path is walked, config says how fast an enemy is at all.
/// <see cref="Position"/> is where the author parked the header empty — diagnostics and debug
/// draw only, never the path itself.
/// </summary>
public readonly record struct PatrolRouteInfo(
    string Route,
    PatrolMode Mode,
    float Speed,
    string Name,
    Guid Guid,
    Vector3 Position,
    IReadOnlyList<Vector3> Waypoints);
```

Add it to `SceneLayout` the way the other optional collections go — nullable parameter, never-null
property, so no caller has to ask whether a scene has patrols:

```csharp
public sealed record SceneLayout(
    IReadOnlyList<ActorSpawnInfo> Actors,
    IReadOnlyList<ObstacleInfo> Obstacles,
    DtNavMesh? NavMesh = null,
    IReadOnlyList<AudioEmitterInfo>? AudioEmitters = null,
    IReadOnlyList<ContainerInfo>? Containers = null,
    IReadOnlyList<AnchorInfo>? Anchors = null,
    IReadOnlyList<CameraGuideInfo>? CameraGuides = null,
    IReadOnlyList<PatrolRouteInfo>? PatrolRoutes = null)
{
    …

    /// <summary>Authored patrol routes, in header-entity order. Never null — a scene with no
    /// patrols is the normal case, and every caller iterating this should not have to say so.
    /// </summary>
    public IReadOnlyList<PatrolRouteInfo> PatrolRoutes { get; init; } = PatrolRoutes ?? [];

    /// <summary>The route with this name. False when the scene has none — callers that REQUIRE
    /// one say so themselves, with a message naming the route.</summary>
    public bool TryRoute(string route, out PatrolRouteInfo info)
    {
        foreach (var candidate in PatrolRoutes)
        {
            if (candidate.Route == route)
            {
                info = candidate;
                return true;
            }
        }
        info = default;
        return false;
    }
}
```

### 2. Collect during the `Bind` loop

Inside the existing `if (containerTables is null || !entity.IsActive) continue;` block — i.e. the
full-load path only. That guard is the live-preview switch: a patched entity re-binds through
`BindEntity`, which does actors and obstacles and deliberately not the authored gameplay layers.
A route is stitched from several entities, so it cannot be re-bound one entity at a time anyway.

`authored`, `model` and `radius` are already computed there.

```csharp
        var routeHeaders = new List<(PatrolRouteMarker Marker, LevelEntityData Entity, Vector3 Position)>();
        var waypoints = new List<(PatrolWaypointMarker Marker, string Name, Vector3 Position)>();
```

```csharp
            if (authored.OfType<PatrolRouteMarker>().FirstOrDefault() is { } routeMarker)
            {
                routeHeaders.Add((routeMarker, entity, model.Translation));
            }

            if (authored.OfType<PatrolWaypointMarker>().FirstOrDefault() is { } waypointMarker)
            {
                waypoints.Add((waypointMarker, entity.Id, model.Translation));
            }
```

Note what is *not* here: no `entity.Get<T>()`, no GUID literal, no `Kind` string. The route and
the waypoint arrive through the same `MaterializeAuthored` call every other component uses, which
is also what reports payloads this build cannot read — a scene authored against a newer
ShiningPie names the offending `Type ?? Id` instead of losing a patrol in silence.

### 3. Stitch and validate, after the loop

Beside `ValidateAnchors` / `ValidateGuides`, called from the same place:

```csharp
    Validate(actors, source);
    ValidateAnchors(anchors, source);
    ValidateGuides(guides, source);
    var routes = BuildRoutes(routeHeaders, waypoints, source);
```

```csharp
/// <summary>
/// Join each route header to the waypoints naming it, in authored order.
///
/// Every failure here is an authoring mistake whose runtime symptom would be an enemy that
/// simply never patrols — the same silent-nothing the camera-guide radius check exists to
/// refuse. Cheaper to fail at load, naming the entity.
/// </summary>
private static IReadOnlyList<PatrolRouteInfo> BuildRoutes(
    List<(PatrolRouteMarker Marker, LevelEntityData Entity, Vector3 Position)> headers,
    List<(PatrolWaypointMarker Marker, string Name, Vector3 Position)> waypoints,
    string source)
{
    var routes = new List<PatrolRouteInfo>();
    var seen = new HashSet<string>(StringComparer.Ordinal);

    foreach (var (marker, entity, position) in headers)
    {
        if (string.IsNullOrWhiteSpace(marker.Route))
        {
            throw new InvalidDataException(
                $"{source}: entity '{entity.Id}' authors a patrol route with no name. "
                + "Waypoints join by that name, so an unnamed route can never have a path.");
        }

        if (!seen.Add(marker.Route))
        {
            throw new InvalidDataException(
                $"{source}: two entities author the patrol route \"{marker.Route}\" "
                + $"('{entity.Id}' is the second). A route is one path with one speed.");
        }

        var points = waypoints
            .Where(w => w.Marker.Route == marker.Route)
            .OrderBy(w => w.Marker.Order)
            .ToList();

        if (points.Count < 2)
        {
            throw new InvalidDataException(
                $"{source}: patrol route \"{marker.Route}\" ('{entity.Id}') has "
                + $"{points.Count} waypoint(s). A path needs at least two — one point is an "
                + "enemy standing still, which reads as broken rather than as a patrol.");
        }

        for (var i = 1; i < points.Count; i++)
        {
            if (points[i].Marker.Order == points[i - 1].Marker.Order)
            {
                throw new InvalidDataException(
                    $"{source}: patrol route \"{marker.Route}\" has two waypoints at order "
                    + $"{points[i].Marker.Order} ('{points[i - 1].Name}' and '{points[i].Name}'). "
                    + "Which one comes first has no defensible answer — renumber one.");
            }
        }

        routes.Add(new PatrolRouteInfo(
            marker.Route, marker.Mode, marker.Speed, entity.Id, entity.EntityGuid, position,
            [.. points.Select(p => p.Position)]));
    }

    // A waypoint whose route does not exist is the typo case, and the one that would otherwise
    // vanish without a word: the route it meant to extend just silently stops short.
    foreach (var (marker, name, _) in waypoints)
    {
        if (!seen.Contains(marker.Route))
        {
            throw new InvalidDataException(
                $"{source}: patrol waypoint '{name}' names route \"{marker.Route}\", which no "
                + "entity authors. Fix the name in Blender, or add the Patrol route empty.");
        }
    }

    return routes;
}
```

`Speed` is deliberately not clamped to the `[AuthorRange(0.1, 3.0)]` on the record: that
attribute reaches the schema and nothing else — it is what the editor draws a slider from, never
an enforcement — so a hand-edited scene can still carry 12. If the game wants a hard bound, this
function is where it goes, as an explicit throw.

### 4. Hand it out

```csharp
    return new SceneLayout(actors, obstacles)
    {
        AudioEmitters = emitters,
        Containers = containers,
        Anchors = anchors,
        CameraGuides = guides,
        PatrolRoutes = routes,
        AnchorObstacles = anchorObstacles,
        Camera = camera,
    };
```

## What the sim does with it

The route arrives as points plus a policy, so stepping it is arithmetic the enemy system owns —
no authored types leak past `SceneBinding`:

```csharp
// Advance one leg. `forward` is per-enemy state; Loop never flips it.
private static int NextWaypoint(PatrolRouteInfo route, int index, ref bool forward)
{
    if (route.Mode == PatrolMode.Loop)
    {
        return (index + 1) % route.Waypoints.Count;
    }

    if (forward && index == route.Waypoints.Count - 1) { forward = false; }
    else if (!forward && index == 0)                   { forward = true; }
    return forward ? index + 1 : index - 1;
}
```

and the speed multiplies the config number at the point of use, so the two never get confused:

```csharp
var speed = config.Enemy.MaxSpeed * route.Speed;
```

## After writing it

The order that matters, because two of these fail green:

1. **Build** ShiningPie.Core. The generator re-dumps `data/authoring-schema.json` — the schema
   diff should show `shiningpie.patrol-route` and `shiningpie.patrol-waypoint` with their fields,
   and `PatrolMode` as an enum with two values. Nothing to register anywhere.
2. **Author** a route in `authoring/shiningpie.blend` and save.
3. **Re-export and diff `data/scenes/shiningpie.json`.** The suite binds the *shipped* export,
   so a passing `dotnet test` proves the committed file parses, not that the editor still writes
   it right — the diff is the only thing that proves the second, and this workspace has already
   had an editor silently export entities with no components at all behind a green build.
4. `cd ShiningPie && dotnet test --project ShiningPie.Tests/ShiningPie.Tests.csproj` —
   `SceneBindingTests` is where a scene edit that breaks binding surfaces. Worth adding cases for
   the four refusals above; they are the whole reason the strings are validated.
5. Confirm you built what you think you built:
   `dotnet build ShiningPie.Core/ShiningPie.Core.csproj -getProperty:ParadiseUseEngineSource`
   → `true`. Reach projects by a real path or `cd` in first — a path through a
   `shiningpie-workspace/` symlink builds green against the *packages*.

None of this needs an engine or editor change, and no version bump: both records live in the
game's own assembly and travel in the scene document the contract already defines.
