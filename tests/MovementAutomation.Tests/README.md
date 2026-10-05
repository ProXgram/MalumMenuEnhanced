# MovementAutomation offline regression harness

Links the actual production `src/Cheats/MovementAutomation.cs` into a standalone .NET 10 executable. Local contracts replace Unity, game players, physics, navigation, task helpers and config; no game, Unity, BepInEx or network package is referenced or run.

The scenarios cover owning-player identity, speed restoration and noncompounding, mode changes, temporary pauses versus terminal contexts, native-object replacement/destruction, signed-speed navigation, target membership, shipless lobbies, saved-spot validity and coordinate offsets, bounded yo-yo cadence without catch-up bursts, twelve-jump termination, AI arrival distance, delegated task-step gating, timer waits versus completion, and bounded unreachable-step handling.

AI lobby scenarios also verify that arming disables instant automatic tasks, survives the brief started-without-ship transition, resumes after the ship appears, and is canceled by the reset used when joining a different lobby. The task fixture rejects destinations before an actual round and ship context exist.

The 244 cases also cover native TrueSpeed factors in the AI steering cap,
nonzero steering with no actual displacement, bounded obstacle recovery even
when the body moves away and back, alternate task approaches before skipping,
meeting time excluded from route timeouts, and mixed Lag Mode phases.

Lag Mode's harness models native joystick velocity before the linked
controller postfix, then integrates velocity into actual position every
0.02-second frame. A 600-frame scenario requires more than 7 units of ordinary
movement on more than 200 nonsnap walking frames, as well as more than 200
stationary frames with no displacement between teleports. At least four
stationary teleports and two automatic returns to walking must occur. Native
velocity stays intact during walking; only the intentional stationary phase
may add a zero-velocity write. These assertions replace the previous
walking-only expectation to match the requested mixture.

After two successful walking corrections, the mode briefly holds position
and attempts two spaced teleports, backward then forward, while movement input
remains held. The final attempt restores ordinary walking on that same tick.
Blocked attempts still consume their slot. An independent 4.1-second deadline
ends the phase at the next callback even if both attempts could not complete;
a coarse 0.49-second callback case exercises that timeout without triggering
the separate long-stall reset. No queued second teleport survives the timeout.

Correction cadence varies mildly within 0.5–2 seconds. Native movement speed
and analog input strength remain unchanged during walking. A recent bounded
movement history supplies correction direction, including when native
controls are inverted; stationary teleports retain that actual travel
direction. Distance settings cap every correction at 0.25–2 units. Fast travel
is clipped to the cap instead of disabling corrections. Tests include signed
native speeds of +/-1.75 and +/-20 with both speed-factor signs, the shortest
distance setting, malformed settings, and true-position/transform offsets.

Further Lag cases cover full-path collision sweeps in both directions, a thin
wall with a clear endpoint beyond it, bounded collision attempts, held input
without actual movement, and recovery when one or both stationary teleports
are blocked. Mid-phase input release, turns, external movement, clock rollback,
long stalls, menu/meeting/vent pauses, Stop, panic, death, disconnect and a new
ship all cancel the phase. General history checks also cover invalid input,
chat/focus/exile/intro/ladder/platform/movement-lock pauses, mode changes,
replacement players, and missing or destroyed transforms. Foreign callbacks
cannot execute corrections. Manual automatic-task and noclip preferences are
preserved.

The snap method remains a local recorder. These cases do not establish native
GameAssembly execution, multiplayer acceptance, or how remote clients display
the movement.

Ground-navigation cases begin with an owned collider disabled by a noclip
fixture, and verify that the controller enables it before task resolution
without changing the noclip preference. Foreign callbacks and menu pauses
leave disabled bodies untouched. These cases exercise the controller and its
`NeedsGroundCollision` flag; the separate LateUpdate noclip integration is not
linked into this harness.

Contact-recovery cases change the available outward direction between frames,
then clear or block the contact, proving that an old direction is not replayed
past its checked segment. The AI steering checks use positive and negative
native speed factors across fixed timesteps, require actual forward movement
within 0.06 units per tick and the 2.5-units-per-second cap, and verify that
invalid timesteps stop motion. NaN and infinite yo-yo clocks stop before any
recorded request and remain stopped after the clock recovers.

Follow/orbit regression cases apply the recorded physical velocity to fixture
positions every fixed frame at all four combinations of base speed +/-20 and
native speed factor +/-3. They require stationary follow convergence without
overshoot, continuous orbit revolutions within a bounded radius, and a maximum
0.24-unit walking step checked against the fixture's travel geometry.
Target-heading cases distinguish small corrections,
accumulated slow motion, and a full opposite turn without losing the selected
nearby trailing offset. Pending moving-target routes keep their cached work and retry
on the next frame; arriving follow clears motion without clearing the route.
Zero/nonfinite native speeds and unavailable timesteps cannot produce a
nonzero steering action.

Continuous pursuit cases integrate the recorded physical velocity into the
owning player's position every fixed frame for thirty simulated seconds while
the target keeps moving forward and sideways. Initial separations of twenty
and forty units cover default target multiplier three and the minimum slider
one with its distant-target catch-up. The controller must close the lead to
under two units at the default slider or under four at slider one, remain
active, sweep every actual step, leave the target's physics untouched, and
restore the original signed base speed.
Minimum-slider chase reverts to the selected slower speed near the target and
can keep a larger trailing gap. These are clear-ground controller tests using
a navigation fixture, not evidence that the real router solves distant
obstacle corridors.

Orbit comparisons integrate eight seconds at target multipliers one and four
and require more than twice the angular travel at the higher setting while
keeping the radius bounded. The executable prints measured pursuit
separations and orbit angles. Live config changes test finite fallback and
clamping without compounding, and keep the separate dash/lag multiplier
independent. Target boosts restore their original signed bits on menu, chat,
meeting, focus and vent pauses, then capture a newly selected native speed
when resumed.

High-speed target tests include negative base speeds and native factors across
fixed timesteps 0.005, 0.02, 0.1 and 0.25 seconds. They require at most twelve
world units per second and 0.24 units per physical step. Thin-wall geometry
forces a rejected full step followed by a clear shorter step; completely
blocked step attempts clear old velocity and restore the boost. Repeated
failures respect the short retry cooldown and terminate at the bounded
five-second corner timeout; one successfully submitted movement starts a
fresh timeout for a later blockage. An intermediate router steering distance
bounds the actual movement before a corner, and a
missing distance cannot replay an old step. Only an explicit router recovery
marker allows an overlapping start; that recovery retains the 0.06-unit and
2.5-units-per-second bounds instead of using chase speed.

Pending-search lifecycle cases allow follow/orbit to keep an active search
beyond five seconds, resume on the very next owning frame when ready, and stop
immediately if that long search finishes unavailable. Even a permanently
pending flag cannot extend movement past the thirty-second bound. Foreign
callbacks cannot advance the search, and stopping clears motion and prevents
further route calls.

Completed-route failure cases model clear floor in an unreachable room pocket.
The moving-route fixture reports failure for the first endpoint, independently
of its standability, then accepts a different nearby endpoint. The controller
must select the alternative and physically walk toward it with checked short
steps. The route fixture models the completed lookup outcome; it does not run
the real grid planner or prove native reachability of those endpoints.

When every endpoint fails, the controller tries all twenty-four distinct Follow
variants or eighteen Orbit variants and terminates within the existing bounded
timeout. A reverse short arc at the middle Orbit radius is the only accepted
endpoint in one case, preventing changes of turn direction from silently
reordering candidates and skipping reachable floor. Pending searches preserve
their endpoint and cache until failure actually completes; successive pending
alternatives share the thirty-second budget. Moving the selected player onto
new floor or selecting another actor resets the old candidate index. Existing
close-orbit radius and angular-rate cases still apply.

Destination-selection cases use per-point floor predicates and sampled
synthetic travel geometry. The moving-route fixture explicitly rejects blocked
endpoints and records selected destinations, covering a blocked preferred
follow point, an obstructed orbit arc, larger-arc fallback, and immediate
reselection when a cached point becomes blocked. Fully blocked nearby floor
keeps the mode armed with zero motion until the target moves to open floor;
that wait cannot consume the next route attempt's timeout. Local contact can
still recover outward even when no candidate is directly reachable. Selecting
another player discards the old side-offset cache. A same-player lobby-to-round
handoff preserves the selected target and clears saved snap spots; replacement
players cannot inherit the old movement mode.

The controller-only focus case exercises `FixedTick`'s pause branch.
`KeybindListener` is not linked; its focus-loss event also stops active modes in
the real integration, so the harness does not claim they survive alt-tab.

`RpcSnapTo` is a local recorder. The task helper is a local fixture whose native-step eligibility can be rejected independently of arrival. Signed-speed multiplication is modeled to detect reversed navigation; these contracts do not establish native method semantics or multiplayer acceptance. Navigation geometry and actual task resolution require their separate helper tests.

Run from the repository root:

```powershell
dotnet run --project .\tests\MovementAutomation.Tests\MovementAutomation.Tests.csproj -c Release
```

Passing the harness establishes the controller's local transitions under these contracts. Live input, Unity scheduling, collision behavior, network propagation, and host/server acceptance remain untested.
