# Structuring a game's runtime

`games.md` covers getting a scene *in*. This is the shape of what runs it: where the simulation
ends, who owns the worlds, how a renderer reads a sim it does not share a thread with.

The reference implementation is **`ShiningPie/ShiningPie.Game`** — small enough to read in one
sitting, and every rule below is enforced there. `ShiningPie.Core` + `ShiningPie.Launcher` is the
older shape of the same game and does NOT follow this; when the two disagree, this file wins.

## Contents

- [The sim owns only what it simulates](#the-sim-owns-only-what-it-simulates)
- [Worlds belong to the host, on one thread](#worlds-belong-to-the-host-on-one-thread)
- [Publishing snapshots to another thread](#publishing-snapshots-to-another-thread)
- [Input in three layers](#input-in-three-layers)
- [Reading a sim without naming its entities](#reading-a-sim-without-naming-its-entities)
- [Loops, threads and seams](#loops-threads-and-seams)

## The sim owns only what it simulates

A simulation is a schedule and a step counter. Not a thread, not a clock, not a queue, not a
readout API.

```csharp
public sealed class GameSim : IDisposable
{
    public static World CreateWorld(SharedWorld shared, WorldConfig config);  // authored world
    public GameSim();                                                        // just the schedule
    public void Tick(World write, World read);                               // one fixed step
}
```

`SystemSchedule` holds no world either (0.19.0+): `Create()` takes nothing and every run names its
worlds — `Run(world)` classic, `Run(world, readWorld)` snapshot-read. One schedule can drive any
world of the same registry, which is what lets a host keep a pool without keeping a schedule per
world. (Before that, one game built **33** schedules — one per pooled world — plus a dictionary to
find the right one at tick time.)

Everything a sim is tempted to own belongs to whoever drives it:

| Concern | Whose |
|---|---|
| Pacing: how many steps a frame owes, catch-up, stall policy | host accumulator |
| Snapshots, interpolation, what the renderer reads | host |
| Key bindings, held state, chords | host binder |
| Drawing, audio, animation clips | renderer layer |

Each one moved out makes the next obvious. The test is uncomfortable and useful: *if this method
disappeared, would the simulation still be a simulation?*

## Worlds belong to the host, on one thread

`SharedWorld.CreateWorld` is thread-affinity-guarded. So the host creates the shared world, the
sim's world, and every twin on **the thread that will tick them** — not on main, then handed over.
Get this right and a pool can grow on demand mid-run; get it wrong and the first lazy `CreateWorld`
throws from a background thread, far from the line that chose the wrong owner.

The sim borrows; the host disposes. Order: schedule, then the shared world (every world dies with
it).

## Publishing snapshots to another thread

**A snapshot is a world plus the frame it is the state after.** That number places it on the sim
clock (`FixedStep × Frame`) and is all a consumer needs to interpolate — no cross-thread clock.

```csharp
readonly record struct WorldSnapshot(World World, long Frame);
ConcurrentQueue<WorldSnapshot> Snapshots { get; }   // producer → consumer
void Recycle(World world);                          // consumer → producer
```

Three properties worth copying:

**One copy per step: the snapshot IS the next read world.** After the tick, copy the stepped state
into a pooled world, publish it, and use that same world as the next step's read world — its
content is already the pre-step state that `[SnapshotReadSystems]` claims want. The obvious design
(copy before the tick to make a read world, copy after to publish) pays twice for the same bytes.

**Mutating a published world is safe only by storage, never by etiquette.** Seeding the next step's
input events touches `World._events`; every consumer read goes through `World._chunkManager`.
Disjoint fields, disjoint memory — that is the argument, and it holds only while consumers read
*components*. Write it at the line, with the condition that would break it, or the next person
"optimises" a component write in beside it.

**Backpressure without stalling the sim.** A dry pool mints a new twin rather than stealing a
snapshot the consumer has not fetched; a consumer-less run trims its oldest unfetched snapshots so
it cannot mint forever. A slow consumer should cost memory, never a stalled or skipped step.

The consumer side: fetch everything available into a local timeline, sample at one step behind the
newest (advancing with its own wall clock since arrival, capped so a stalled producer holds rather
than extrapolates), find the bracketing pair *by search* — so gaps from skipped publishes still
interpolate correctly — and recycle everything below the lower bracket.

## Input in three layers

Never let a device key reach gameplay.

```
window        RawInput (device transitions, timestamped AT THE PUMP)   ← Paradise.Windowing
host binder   RawInput → GameKey actions, per-action refcount
sim           action transitions → held state → intent
```

- **Timestamp at the pump**, and carry that timestamp through the binder unchanged. Stamping at
  drain time records when the *reader* woke up, which makes a recorded tape unreplayable.
- **Refcount actions in the binder.** Two keys bound to one action (W and Up → MoveForward) must
  release the action only when the *last* one lets go. This is the binder's job precisely because
  the sim should never learn that two things map to one.
- **Hold state in the sim, not the host.** Then a step with no events means "nothing changed"
  rather than "let go", and a host that reports only transitions is correct rather than lossy.
- Because only actions cross, a rebind is configuration and a tape replays identically under any
  binding.

Device quirks stay in front of the boundary. (A console host cannot report key-ups, so it
synthesises releases from auto-repeat gaps — ugly, contained, invisible to the world.)

## Reading a sim without naming its entities

Query for the components a view needs; do not look up "the player".

```csharp
foreach (var e in QueryBuilder.Create().With<ActorPosition>().With<ActorHeading>().Build(world))
    actors.Add(Sample(e.Entity));          // renderer grows instances to match
```

A new actor then appears on screen with no render change. Where a placeholder assumption survives
— "the camera follows `actors[0]`" until a camera entity exists — name it in the doc comment; that
is what makes it findable when the entity arrives.

Entity handles are stable across every world of one `SharedWorld` (same creation order), so a
handle resolved from one snapshot reads correctly from another.

## Loops, threads and seams

One loop per thread, one decision per class, and a conductor that owns nothing else:

| Class | Thread | Owns |
|---|---|---|
| `MainLoop` | main | creates everything, starts the others, tears down in reverse |
| `WindowLoop` | main | the pump — macOS delivers window events nowhere else |
| `GameLoop` | game | the fixed-step accumulator, the worlds, the snapshot pool |
| `RenderLoop` | render | vsync-paced frames, GPU objects created and destroyed here |

**Name the seams and keep the count.** Two: the window contract (input stream, close latch,
resize) and the snapshot queue. A third has to be argued for rather than added — say so in the
class doc, or one appears.

**Talk to the platform through a contract.** `Paradise.Windowing`'s `IWindow` gives the loops
close/input/resize/`CreateSurface`; only the composition root names an SDL type. Check it cheaply:
the windowed smoke runs under `SDL_VIDEO_DRIVER=dummy`, which also proves the run degrades to
pump-plus-sim when no surface can exist.

**The pump is the platform's, not the window's.** OS event queues are per-process; two windows
each draining one would eat each other's events. `IWindowPlatform.Pump()` drains once and routes
by window id.

**Shutdown is where the ordering bites.** The render thread must dispose its GPU stack before the
window its surface came from. So *check what `Join` returns*: if a worker is still alive past a
grace period, skip disposal and let process exit reclaim. Disposing under a live thread turns a
hang into a use-after-dispose crash on the way out.
