using System.Runtime.InteropServices;
using Paradise.Authoring;

namespace ShiningPie.Authoring;

// Patrol routes: the path an enemy walks when nothing has its attention.
//
// Belongs in ShiningPie.Core/Authoring/SceneComponents.cs, next to AnchorMarker and
// CameraGuideMarker — it is scene vocabulary (what the world IS), not tuning. It is split out
// into this file only so the diff is readable; paste the three declarations into
// SceneComponents.cs rather than adding a second [assembly: AuthoredRegistry] file.
//
// Shape of the thing: a route is ONE header entity carrying PatrolRouteMarker (the speed
// multiplier and the mode) plus N waypoint entities carrying PatrolWaypointMarker, each of
// which contributes its world position. The points are entities rather than an authored
// Vector3 array on the header for the reason every other component here derives geometry from
// its transform: the author already places the point by dragging an empty in the viewport, and
// a coordinate typed into a panel is a coordinate that stops matching the geometry the moment
// anyone moves a wall. It also cannot be done the other way here — the Blender exporter never
// emits `Parent`, so a route cannot own its points by hierarchy, which is why they are joined
// by a name instead.


/// <summary>
/// What an enemy does on reaching the last point of a route. A closed set, so an enum: the
/// editor shows a dropdown and "PingPing" becomes unrepresentable.
/// </summary>
public enum PatrolMode
{
    /// <summary>Walk to the last point, then head straight back to the first and repeat. The
    /// path is a closed ring, so the run from last to first is walked like any other leg —
    /// author it somewhere the enemy can actually go.</summary>
    Loop,

    /// <summary>Walk to the last point, turn around, walk the same points back. For a dead-end
    /// route — a corridor, a platform edge — where a Loop's closing leg would cut through
    /// geometry.</summary>
    PingPong,
}

/// <summary>
/// A patrol route: the path enemies walk while unaggroed, authored as one empty per route.
///
/// The empty's own transform is NOT the path — the path is the entities carrying
/// <see cref="PatrolWaypointMarker"/> that name this route. Put the header empty somewhere near
/// the route so it is findable in the outliner; binding reads its position for diagnostics and
/// debug draw only.
///
/// <see cref="Speed"/> is a MULTIPLIER, not a speed, and that is the whole reason it is allowed
/// to live in the scene at all: the absolute number stays the one place ShiningPie keeps balance
/// (config's <c>Enemy.MaxSpeed</c>), and this says only how this particular path relates to it —
/// a shuffle around the arcade reads differently from a brisk sweep of the platform. Same
/// argument as <see cref="CameraRig.Fov"/> riding with the pose it was framed against.
/// </summary>
[Guid("4b6f2c41-0697-404f-adbe-7fb6924e3fa0")]
[Authored(DisplayName = "Patrol route")]
public sealed record PatrolRouteMarker
{
    [AuthorDoc("Route name. Waypoints join by this exact string; a name no waypoint claims, "
        + "or two routes sharing one, fails at load naming this entity.")]
    public string Route { get; set; } = "";

    [AuthorRange(0.1, 3.0)]
    [AuthorDoc("Walk speed as a fraction of config's Enemy.MaxSpeed. 1 = full speed, 0.5 = a "
        + "patrol stroll. The absolute speed stays in config.json.")]
    public float Speed { get; set; } = 0.5f;

    [AuthorDoc("What happens at the last point: Loop closes the ring, PingPong walks back.")]
    public PatrolMode Mode { get; set; } = PatrolMode.Loop;
}

/// <summary>
/// One point of a patrol route. Its world position IS the point — the same rule anchors and
/// camera guides use — so authoring a route is dropping empties where you want the enemy to
/// walk and numbering them.
///
/// <see cref="Order"/> is explicit rather than taken from export order because export order is
/// the editor's business, not the scene's: inserting a point between two others, or renaming an
/// empty, would silently re-route the patrol. Numbers need not be contiguous (10, 20, 30 leaves
/// room to insert) but must be unique within a route.
/// </summary>
[Guid("699d6fa5-8500-4798-a508-52e89f3a6a89")]
[Authored(DisplayName = "Patrol waypoint")]
public sealed record PatrolWaypointMarker
{
    [AuthorDoc("Which route this point belongs to. Must match a Patrol route's Route exactly.")]
    public string Route { get; set; } = "";

    [AuthorRange(0, 999)]
    [AuthorDoc("Position along the route, ascending. Unique within a route; gaps are fine.")]
    public int Order { get; set; }
}
