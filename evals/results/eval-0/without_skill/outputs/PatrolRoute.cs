// Belongs at the end of ShiningPie.Core/Authoring/SceneComponents.cs — that file IS the scene
// vocabulary, and splitting it would give the game two places to look for "what can I attach in
// Blender". Presented as its own file only because this exercise is read-only; the `using`s and
// namespace below are the ones already at the top of SceneComponents.cs, so pasting the records
// in (without the header) is the whole change.
//
// [assembly: AuthoredRegistry] is NOT repeated here: SceneComponents.cs already opts the assembly
// in, and the generator picks up every [Authored] record in it — including these.

using System.Runtime.InteropServices;
using Paradise.Authoring;

namespace ShiningPie.Authoring;

/// <summary>
/// How a patrol route is walked once its last waypoint is reached — the closed behavior
/// vocabulary of the patrol system, exactly as <see cref="CameraGuideMode"/> is the camera
/// guides'. An enum rather than two components or a bool, because a route is exactly one of
/// these and the editor's dropdown makes an invalid combination unrepresentable. A bool would
/// also have to be named (`PingPong = false`?) and would have nowhere to grow a third mode.
/// </summary>
public enum PatrolMode
{
    /// <summary>Last waypoint back to the first, forever. The route is a closed loop, so the
    /// author is responsible for the leg from the end back to the start being walkable.</summary>
    Loop,

    /// <summary>Turn around at each end and walk the route back. Costs no extra authoring for a
    /// dead-end corridor — the one case a Loop route cannot express without doubling the
    /// waypoints back on themselves.</summary>
    PingPong,
}

/// <summary>
/// A path enemies walk when nothing is chasing them: a parent empty carrying the route's
/// behavior, with a <see cref="PatrolWaypointMarker"/> child per point on the path.
///
/// <b>The waypoints are the scene's own hierarchy, not a field on this record.</b> Two reasons,
/// and the second is decisive:
/// <list type="bullet">
/// <item>A point on a path is a THING YOU DRAG. Authoring it as an empty means the author places
/// it with Blender's own gizmo against the geometry it has to clear, and a wall that moves takes
/// the waypoint with it — the same reason an anchor's position is its transform and a guide's
/// radius is its scale rather than numbers typed twice.</item>
/// <item>A list field would not be authorable AT ALL in this game's editor.
/// <c>paradise_blender/contract/authoring.py:flatten</c> turns every array in the schema into a
/// host reference ("a list of typed rows has no author asking for it yet") unless its items are
/// host-object references, so <c>List&lt;Vector3&gt; Waypoints</c> would export as nothing and
/// the Components panel would show it as a hole it cannot fill.</item>
/// </list>
///
/// Deliberately data-light, like every record beside it: the enemies' base speed stays in
/// config.json's <c>Enemy</c> section (AGENTS.md — one place for balance). What is HERE is the
/// structure the scene itself owns: which path this is, which way it is walked, and the one
/// number that is a property of the PATH rather than of the enemy walking it.
/// </summary>
[Guid("a7799321-e0fa-484d-9f80-27421b796bc7")]
[Authored(DisplayName = "Patrol route")]
public sealed record PatrolRouteMarker
{
    [AuthorDoc("What happens at the last waypoint: Loop returns to the first, PingPong walks "
        + "the route back. The waypoints are this entity's children.")]
    public PatrolMode Mode { get; set; } = PatrolMode.Loop;

    // A MULTIPLIER, not a speed — which is what keeps it on the right side of the
    // config-over-code-constants rule. `Enemy.MaxSpeed` in config.json stays the one place the
    // pack's speed is set; this says only that THIS path is walked at a fraction of it, which is
    // a fact about the path (a sentry sweeping a corridor, a lookout pacing a roof) and cannot
    // live anywhere but next to the path. Same shape as CameraRig.Fov riding with the pose it
    // was framed against.
    //
    // Range is advisory (it reaches the editor and nothing clamps at load) — SceneBinding is the
    // enforcement, and it refuses a non-positive scale rather than clamping it.
    [AuthorRange(0.05, 4)]
    [AuthorDoc("Multiplies the Enemy MaxSpeed from config.json while walking this route. 1 is "
        + "the pack's own speed; 0.4 is a patrol that is not in a hurry.")]
    public float SpeedScale { get; set; } = 1f;
}

/// <summary>
/// One point on a patrol route. Its POSITION is its transform — there is no field for it — and
/// which route it belongs to is which empty it is parented to in Blender.
///
/// <see cref="Step"/> exists because export order cannot carry it: the addon emits entities
/// sorted by NAME (<c>paradise_blender/authoring/entity.py:entity_objects</c>, so that two
/// exports of an unchanged scene do not differ), and deriving the walking order from names would
/// be binding by name — the one thing this game's scene binding never does. An explicit step
/// also survives inserting a point between two others, which renaming a run of children does not.
/// </summary>
[Guid("a93b196b-ec79-420f-a224-10f7191d23b5")]
[Authored(DisplayName = "Patrol waypoint")]
public sealed record PatrolWaypointMarker
{
    [AuthorRange(0, 999)]
    [AuthorDoc("Where this point falls in the walking order, low to high. Gaps are fine; two "
        + "waypoints of one route claiming the same step is refused at load.")]
    public int Step { get; set; }
}
