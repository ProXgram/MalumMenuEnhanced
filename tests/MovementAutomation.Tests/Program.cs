using MalumMenu;
using UnityEngine;
using Plugin = MalumMenu.MalumMenu;

int total = 0, failed = 0;

Run("foreign physics callbacks cannot steer or acquire own speed", () =>
{
    var own = Setup(); var other = AddPlayer(7);
    MovementAutomation.Start(MovementMode.ZigzagDash);
    for (int frame = 0; frame < 20; frame++) MovementAutomation.FixedTick(other.MyPhysics);
    Require(other.MyPhysics.SpeedWrites == 0 && other.MyPhysics.MovementWrites == 0, "foreign physics was changed");
    Require(own.MyPhysics.SpeedWrites == 0 && own.MyPhysics.MovementWrites == 0, "remote callbacks triggered local movement");
    Require(other.NetTransform.Snaps.Count == 0, "remote callback sent a snap");
});
Run("own flag cannot substitute for actual player/physics identity", () =>
{
    var own = Setup(); var other = AddPlayer(7); other.MyPhysics.AmOwner = true;
    MovementAutomation.Start(MovementMode.ZigzagDash); MovementAutomation.FixedTick(other.MyPhysics);
    Require(other.MyPhysics.SpeedWrites == 0 && other.MyPhysics.MovementWrites == 0, "spoofed owning physics passed identity check");
    own.MyPhysics.myPlayer = other; MovementAutomation.FixedTick(own.MyPhysics);
    Require(own.MyPhysics.SpeedWrites == 0 && own.MyPhysics.MovementWrites == 0, "wrong physics owner passed identity check");
});
Run("stunt starts restore sprint before capturing speed and modes are exclusive", () =>
{
    var own = Setup(); own.MyPhysics.Speed = 3.5f;
    SprintHandler.OnReset = () => own.MyPhysics.Speed = 1.75f;
    MovementAutomation.Start(MovementMode.ZigzagDash); Tick();
    Equal(own.MyPhysics.PeekSpeed, 3.5f, "stunt captured a sprint boost");
    AddPlayer(7).transform.position = new Vector2(5, 0);
    MovementAutomation.Start(MovementMode.ShadowFollow); Tick();
    Require(MovementAutomation.Mode == MovementMode.ShadowFollow && SprintHandler.Resets == 2, "mode or sprint reset did not switch");
    Require(own.MyPhysics.PeekSpeed > 1.75f, "follow did not acquire its own target-speed boost");
    MovementAutomation.Stop(); Exact(own.MyPhysics.PeekSpeed, 1.75f, "follow captured the previous mode's boosted speed");
});
Run("boost never compounds across fixed updates and stop restores exact bits", () =>
{
    var own = Setup(1.234567f); MovementAutomation.Start(MovementMode.ZigzagDash);
    for (int frame = 0; frame < 100; frame++)
    { Tick(frame * 0.02); Equal(own.MyPhysics.PeekSpeed, 2.469134f, "boost compounded"); Require(own.MyPhysics.LastInput.sqrMagnitude <= 1.00001f, "input exceeded unit length"); }
    MovementAutomation.Stop(); Exact(own.MyPhysics.PeekSpeed, 1.234567f, "stop changed original speed bits");
    Near(own.MyPhysics.PhysicalVelocity, Vector2.zero, "stop retained motion");
});
foreach (var pause in new (string Name, Action Change)[]
{
    ("menu", () => MenuUI.isGUIActive = true),
    ("chat", () => HudManager.Existing.Chat.IsOpenOrOpening = true),
    ("focus", () => Application.isFocused = false),
    ("meeting", () => Utils.isMeeting = true),
    ("vent", () => PlayerControl.LocalPlayer.inVent = true),
})
    Run(pause.Name + " restores active speed and removes velocity without dropping mode", () =>
    {
        var own = Setup(-1.234567f); MovementAutomation.Start(MovementMode.ZigzagDash); Tick();
        pause.Change(); Tick(0.2);
        Exact(own.MyPhysics.PeekSpeed, -1.234567f, "pause failed signed restoration");
        Near(own.MyPhysics.PhysicalVelocity, Vector2.zero, "pause retained movement");
        Require(MovementAutomation.Active, "temporary pause dropped mode");
    });
foreach (var end in new (string Name, Action Change)[]
{
    ("death", () => PlayerControl.LocalPlayer.Data.IsDead = true),
    ("disconnect", () => PlayerControl.LocalPlayer.Data.Disconnected = true),
    ("leaving game", () => { Utils.isInGame = false; Utils.isClient = false; }),
})
    Run(end.Name + " terminates movement and restores captured speed", () =>
    {
        var own = Setup(); MovementAutomation.Start(MovementMode.ZigzagDash); Tick(); end.Change(); Tick(0.2);
        Exact(own.MyPhysics.PeekSpeed, 1.75f, "ended context retained boosted speed");
        Near(own.MyPhysics.PhysicalVelocity, Vector2.zero, "ended context retained velocity");
        Require(!MovementAutomation.Active, "ended context remained active");
    });
Run("replaced physics restores old snapshot and captures replacement speed independently", () =>
{
    var own = Setup(); var old = own.MyPhysics; MovementAutomation.Start(MovementMode.ZigzagDash); Tick();
    own.MyPhysics = new(own, 4.25f); Tick(0.2);
    Exact(old.PeekSpeed, 1.75f, "old snapshot was lost");
    Equal(own.MyPhysics.PeekSpeed, 8.5f, "replacement reused the previous speed");
    MovementAutomation.Stop(); Exact(own.MyPhysics.PeekSpeed, 4.25f, "replacement restored old player's speed");
});
Run("new ship context restores motion and invalidates saved spots", () =>
{
    var own = Setup(); SaveTwoSpots(); MovementAutomation.Start(MovementMode.ZigzagDash); Tick();
    ShipStatus.Instance = new(); Tick(0.2);
    Exact(own.MyPhysics.PeekSpeed, 1.75f, "new scene retained boost");
    Require(!MovementAutomation.Active && !MovementAutomation.HasPointA && !MovementAutomation.HasPointB, "new scene inherited automation or spots");
});
Run("replaced local player does not inherit an old mode or speed", () =>
{
    var old = Setup(); MovementAutomation.Start(MovementMode.ZigzagDash); Tick();
    var replacement = new PlayerControl(0, true, 4.25f); PlayerControl.LocalPlayer = replacement;
    PlayerControl.AllPlayerControls = [replacement]; Tick(0.2);
    Exact(old.MyPhysics.PeekSpeed, 1.75f, "old owning snapshot not restored");
    Require(replacement.MyPhysics.SpeedWrites == 0 && replacement.MyPhysics.MovementWrites == 0 && !MovementAutomation.Active, "replacement inherited old automation");
});
Run("destroyed captured physics is not dereferenced during cleanup", () =>
{
    var own = Setup(); MovementAutomation.Start(MovementMode.ZigzagDash); Tick();
    var old = own.MyPhysics; int writes = old.SpeedWrites, movement = old.MovementWrites; old.Destroyed = true;
    MovementAutomation.Stop(); Require(old.SpeedWrites == writes && old.MovementWrites == movement, "cleanup accessed destroyed native physics");
});
Run("negative speed retains exact restoration and AI movement travels toward goal", () =>
{
    var own = Setup(-1.75f); MovementAutomation.Start(MovementMode.AiTasks); Tick();
    Require(own.MyPhysics.PhysicalVelocity.x > 0, "signed speed reversed autonomous navigation");
    MovementAutomation.Stop(); Exact(own.MyPhysics.PeekSpeed, -1.75f, "negative speed changed on stop");
});
Run("AI movement input is normalized even if a helper returns a large vector", () =>
{
    var own = Setup(); NavigationRouter.ForcedDirection = new Vector2(50, 20);
    MovementAutomation.Start(MovementMode.AiTasks); Tick();
    Require(own.MyPhysics.LastInput.sqrMagnitude <= 1.00001f, "large route direction reached native movement");
    Require(own.MyPhysics.SpeedWrites == 0, "AI mode unexpectedly boosted speed");
});
Run("shipless lobby permits own movement and pause restores speed", () =>
{
    var own = Setup(); Utils.isInGame = false; Utils.isLobby = true; ShipStatus.Instance = null;
    MovementAutomation.Start(MovementMode.ZigzagDash); Tick(); Equal(own.MyPhysics.PeekSpeed, 3.5f, "lobby incorrectly required a ship");
    MovementAutomation.Pause(); Exact(own.MyPhysics.PeekSpeed, 1.75f, "lobby pause retained boost");
});
Run("cycle selector filters dead/disconnected players and stops when target disappears", () =>
{
    Setup(); var alive = AddPlayer(5); AddPlayer(1).Data.IsDead = true; AddPlayer(2).Data.Disconnected = true;
    alive.transform.position = new Vector2(5, 0); MovementAutomation.CycleTarget();
    Require(MovementAutomation.TargetName == alive.Data.PlayerName, "selector chose unavailable player");
    MovementAutomation.Start(MovementMode.ShadowFollow); Tick();
    PlayerControl.AllPlayerControls.Remove(alive); Tick(0.2);
    Require(!MovementAutomation.Active, "departed player pointer remained a valid target");
});
Run("yo-yo requires distinct open-floor spots scoped to current context", () =>
{
    var own = Setup(); NavigationRouter.FloorClear = false; MovementAutomation.SavePointA();
    Require(!MovementAutomation.HasPointA, "blocked floor was saved"); NavigationRouter.FloorClear = true;
    MovementAutomation.SavePointA(); MovementAutomation.SavePointB(); MovementAutomation.Start(MovementMode.TeleportYoYo);
    Require(!MovementAutomation.Active && own.NetTransform.Snaps.Count == 0, "same-position spots activated yo-yo");
    own.transform.position = new Vector2(4, 0); MovementAutomation.SavePointB(); MovementAutomation.Start(MovementMode.TeleportYoYo);
    Require(MovementAutomation.Active, "valid distinct spots were rejected"); MovementAutomation.Reset();
    Require(!MovementAutomation.HasPointA && !MovementAutomation.HasPointB, "reset retained saved spots");
});
Run("yo-yo uses exact transform coordinates while checking true-position floor offset", () =>
{
    var own = Setup(); own.TruePositionOffset = new Vector2(0, -0.3f); SaveTwoSpots();
    MovementAutomation.Start(MovementMode.TeleportYoYo); Tick(1.0);
    Require(own.NetTransform.Snaps.Count == 1, "first scheduled snap missing");
    Near(own.NetTransform.Snaps[0].Position, Vector2.zero, "snap used feet coordinates instead of saved transform");
    Near(NavigationRouter.LastFloorTest, new Vector2(0, -0.3f), "collision check ignored transform offset");
});
Run("yo-yo cadence never catches up in bursts and stops after twelve jumps", () =>
{
    var own = Setup(); SaveTwoSpots(); MovementAutomation.Start(MovementMode.TeleportYoYo);
    Tick(0.99); Require(own.NetTransform.Snaps.Count == 0, "snap happened before interval");
    Tick(1.0); Require(own.NetTransform.Snaps.Count == 1, "first snap missing");
    for (int frame = 0; frame < 20; frame++) Tick(1.0);
    Require(own.NetTransform.Snaps.Count == 1, "same-time updates caused duplicate snaps");
    Tick(100); Require(own.NetTransform.Snaps.Count == 2, "lagged frame sent catch-up burst");
    Tick(100.99); Require(own.NetTransform.Snaps.Count == 2, "next interval retained historical deadline");
    for (int jump = 0; jump < 10; jump++) Tick(101 + jump);
    Require(own.NetTransform.Snaps.Count == 12 && !MovementAutomation.Active, "jump limit did not terminate mode");
    Tick(999); Require(own.NetTransform.Snaps.Count == 12, "finished mode sent more snaps");
    for (int index = 0; index < 12; index++)
        Near(own.NetTransform.Snaps[index].Position, (index & 1) == 0 ? Vector2.zero : new Vector2(4, 0), "destinations did not alternate");
});
foreach (float interval in new[] { 0.01f, 99f })
    Run("yo-yo interval " + interval + " is bounded", () =>
    {
        var own = Setup(); Plugin.yoyoInterval.Value = interval; SaveTwoSpots(); MovementAutomation.Start(MovementMode.TeleportYoYo);
        float due = interval < 1 ? 1 : 5; Tick(due - 0.01);
        Require(own.NetTransform.Snaps.Count == 0, "interval escaped bounds"); Tick(due);
        Require(own.NetTransform.Snaps.Count == 1, "bounded interval never fired");
    });
Run("blocked yo-yo destination stops without sending a snap", () =>
{
    var own = Setup(); SaveTwoSpots(); MovementAutomation.Start(MovementMode.TeleportYoYo);
    NavigationRouter.FloorClear = false; Tick(1);
    Require(own.NetTransform.Snaps.Count == 0 && !MovementAutomation.Active, "blocked destination emitted a snap");
});
Run("AI arrives only inside its radius and delegates native-step gating", () =>
{
    var own = Setup(); CheatToggles.automaticTasks = true; MovementAutomation.Start(MovementMode.AiTasks);
    Require(!CheatToggles.automaticTasks, "AI start retained instant automatic tasks");
    AiTasksHandler.Goal = new Vector2(2, 0); AiTasksHandler.ArrivalDistance = 0.12f; Tick();
    Require(AiTasksHandler.Arrivals == 0 && AiTasksHandler.NativeSteps == 0, "distant goal completed a task");
    own.transform.position = new Vector2(1.87f, 0); Tick(0.2); Require(AiTasksHandler.Arrivals == 0, "outside radius invoked arrival");
    own.transform.position = new Vector2(1.9f, 0); AiTasksHandler.NativeActionAllowed = false; Tick(0.4);
    Require(AiTasksHandler.Arrivals == 1 && AiTasksHandler.NativeSteps == 0, "controller bypassed helper native gate");
    Near(own.MyPhysics.PhysicalVelocity, Vector2.zero, "arrival retained movement");
    AiTasksHandler.NativeActionAllowed = true; Tick(1);
    Require(AiTasksHandler.NativeSteps == 1, "eligible arrival did not reach helper");
});
Run("AI timer waiting keeps mode but finished helper stops it", () =>
{
    var own = Setup(); MovementAutomation.Start(MovementMode.AiTasks); Tick();
    AiTasksHandler.Available = false; Tick(0.2);
    Require(MovementAutomation.Active && MovementAutomation.Mode == MovementMode.AiTasks, "wait was treated as completion");
    Near(own.MyPhysics.PhysicalVelocity, Vector2.zero, "waiting AI kept walking");
    AiTasksHandler.IsFinished = true; Tick(0.4);
    Require(!MovementAutomation.Active && MovementAutomation.StatusText == "AI finished", "finished AI did not stop with final status");
});
Run("AI no-route retry is bounded then skips current task without stopping all tasks", () =>
{
    Setup(); MovementAutomation.Start(MovementMode.AiTasks); NavigationRouter.Blocked = true; Tick(0);
    for (int i = 1; i <= 9; i++) Tick(i * 0.5);
    Require(AiTasksHandler.Skips == 0 && MovementAutomation.Active, "task skipped before retry deadline");
    Tick(5.0); Require(AiTasksHandler.Skips == 1 && MovementAutomation.Active, "deadline did not skip current task while retaining AI");
    Tick(5.1); Require(AiTasksHandler.Skips == 1, "same blocked step was immediately skipped again");
    NavigationRouter.Blocked = false; Tick(5.5);
    Require(PlayerControl.LocalPlayer.MyPhysics.LastInput.sqrMagnitude > 0, "AI did not resume after blocked step");
});
Run("non-AI blocked route stops after bounded retry without any snap", () =>
{
    var own = Setup(); AddPlayer(7).transform.position = new Vector2(5, 0);
    NavigationRouter.Blocked = true; MovementAutomation.Start(MovementMode.ShadowFollow);
    for (int i = 0; i <= 10; i++) Tick(i * 0.5);
    Require(!MovementAutomation.Active && own.NetTransform.Snaps.Count == 0, "blocked follow did not stop safely");
});
Run("orbit steers toward routed circle points with a stable own-only boost", () =>
{
    var own = Setup(); var target = AddPlayer(7); target.transform.position = new Vector2(5, 0);
    MovementAutomation.Start(MovementMode.TurboOrbit);
    float firstBoost = 0;
    for (int frame = 0; frame < 80; frame++)
    {
        Tick(frame * 0.02);
        if (frame == 0) firstBoost = own.MyPhysics.PeekSpeed;
        Require(firstBoost > 1.75f, "orbit did not acquire a target-speed boost");
        Equal(own.MyPhysics.PeekSpeed, firstBoost, "orbit boost changed across frames");
        Require(own.MyPhysics.LastInput.sqrMagnitude > 0f, "orbit did not steer toward its circle");
        Require(MathF.Abs(Vector2.Distance(NavigationRouter.LastTo, target.GetTruePosition()) - 1.2f) < 0.0001f, "orbit goal left configured circle");
    }
    Require(target.MyPhysics.SpeedWrites == 0 && target.MyPhysics.MovementWrites == 0 && target.NetTransform.Snaps.Count == 0,
        "orbit changed the selected player's movement");
    MovementAutomation.Stop(); Exact(own.MyPhysics.PeekSpeed, 1.75f, "orbit stop retained boost");
});
Run("shadow follow filters received target heading without compounding or teleporting", () =>
{
    var own = Setup(); var target = AddPlayer(7); target.transform.position = new Vector2(5, 0);
    MovementAutomation.Start(MovementMode.ShadowFollow); Tick();
    target.transform.position = new Vector2(5, 1); Tick(0.02);
    var earlyHeading = (target.GetTruePosition() - NavigationRouter.LastTo).normalized;
    Require(earlyHeading.x > 0.8f && earlyHeading.y >= -0.0001f && earlyHeading.y < 0.4f,
        "one received heading change snapped the trailing goal");
    target.transform.position = new Vector2(5, 2); Tick(0.04);
    var heading = (target.GetTruePosition() - NavigationRouter.LastTo).normalized;
    Require(heading.x > 0.7f && heading.y > 0.2f, "meaningful received motion never influenced the selected heading");
    Require(MathF.Abs(Vector2.Distance(NavigationRouter.LastTo, target.GetTruePosition()) - 0.65f) < 0.0001f,
        "filtered follow changed its clear-floor trailing distance");
    Require(own.MyPhysics.PeekSpeed > 1.75f && own.NetTransform.Snaps.Count == 0 && target.MyPhysics.MovementWrites == 0,
        "follow lost its own boost, snapped, or wrote target velocity");
    MovementAutomation.Stop(); Exact(own.MyPhysics.PeekSpeed, 1.75f, "heading updates corrupted the original speed");
});
Run("pause invalidates route cache before future movement", () =>
{
    Setup(); MovementAutomation.Start(MovementMode.AiTasks); Tick(); int before = NavigationRouter.Resets;
    MovementAutomation.Pause(); Require(NavigationRouter.Resets > before, "pause retained a cached route");
});
Run("AI armed in a shipless lobby disables instant tasks and waits without walking", () =>
{
    var own = Setup(); Utils.isInGame = false; Utils.isLobby = true; ShipStatus.Instance = null;
    CheatToggles.automaticTasks = true; MovementAutomation.Start(MovementMode.AiTasks);
    for (int frame = 0; frame < 5; frame++) Tick(frame);
    Require(MovementAutomation.Active && MovementAutomation.Mode == MovementMode.AiTasks, "lobby disarmed AI");
    Require(!CheatToggles.automaticTasks, "armed AI retained instant tasks");
    Require(NavigationRouter.Calls == 0 && AiTasksHandler.NativeSteps == 0, "lobby navigated or advanced a task");
    Near(own.MyPhysics.PhysicalVelocity, Vector2.zero, "lobby waiting retained motion");
});
Run("armed AI survives started-without-ship then resolves tasks after the ship appears", () =>
{
    var own = Setup(); Utils.isInGame = false; Utils.isLobby = true; ShipStatus.Instance = null;
    MovementAutomation.Start(MovementMode.AiTasks); Tick(0);
    Utils.isLobby = false; Utils.isInGame = true; Tick(1);
    Require(MovementAutomation.Active && MovementAutomation.Mode == MovementMode.AiTasks, "transitional shipless round canceled armed AI");
    Require(AiTasksHandler.NativeSteps == 0 && NavigationRouter.Calls == 0, "missing ship allowed task action or navigation");
    ShipStatus.Instance = new(); Tick(2);
    Require(MovementAutomation.Active && MovementAutomation.Mode == MovementMode.AiTasks && !CheatToggles.automaticTasks,
        "ship handoff lost AI or enabled instant tasks");
    Require(NavigationRouter.Calls > 0 && own.MyPhysics.LastInput.sqrMagnitude > 0, "AI did not resolve a destination after ship appeared");
    own.transform.position = AiTasksHandler.Goal; Tick(3);
    Require(AiTasksHandler.Arrivals == 1 && AiTasksHandler.NativeSteps == 1, "new round destination did not reach delegated task helper");
});
Run("game-joined style reset cancels armed AI before a different lobby", () =>
{
    Setup(); Utils.isInGame = false; Utils.isLobby = true; ShipStatus.Instance = null;
    SaveTwoSpots(); MovementAutomation.Start(MovementMode.AiTasks); Tick(); MovementAutomation.Reset();
    var replacement = new PlayerControl(0, true); PlayerControl.LocalPlayer = replacement; PlayerControl.AllPlayerControls = [replacement];
    Tick(1);
    Require(!MovementAutomation.Active && MovementAutomation.Mode == MovementMode.Off, "new lobby inherited armed AI");
    Require(!MovementAutomation.HasPointA && !MovementAutomation.HasPointB, "new lobby inherited saved spots");
    Require(AiTasksHandler.NativeSteps == 0 && replacement.MyPhysics.MovementWrites == 0, "reset mode acted on new lobby player");
});

Run("AI steering reduces excessive speed without changing the user's base speed", () =>
{
    var own = Setup(20f); MovementAutomation.Start(MovementMode.AiTasks); Tick();
    Require(own.MyPhysics.PhysicalVelocity.magnitude <= 2.5001f, "AI walked too fast to stop at route corners");
    Require(own.MyPhysics.SpeedWrites == 0, "AI edited selected base speed");
});
Run("AI speed braking includes the game's speed multiplier", () =>
{
    var own = Setup(20f); own.MyPhysics.SpeedFactor = 3f;
    MovementAutomation.Start(MovementMode.AiTasks); Tick();
    Require(own.MyPhysics.PhysicalVelocity.magnitude <= 2.5001f, "room speed multiplier bypassed braking");
    Require(own.MyPhysics.SpeedWrites == 0, "braking edited the base speed");
});
Run("AI detects commanded movement with no actual progress and walks clear", () =>
{
    var own = Setup(); NavigationRouter.RecoveryDirection = Vector2.down; NavigationRouter.FloorClear = false;
    MovementAutomation.Start(MovementMode.AiTasks); Tick(); Tick(1.01);
    Require(NavigationRouter.RecoveryCalls == 1 && own.MyPhysics.PhysicalVelocity.y < 0,
        "nonzero steering into an item was mistaken for progress");
    Require(own.NetTransform.Snaps.Count == 0 && own.MyPhysics.SpeedWrites == 0, "recovery snapped or changed speed");
});
Run("persistent no-progress tries another task approach before skipping", () =>
{
    Setup(); NavigationRouter.RecoveryDirection = Vector2.down; AiTasksHandler.HasAlternate = true;
    MovementAutomation.Start(MovementMode.AiTasks); Tick();
    for (int second = 1; second <= 5; second++) Tick(second * 1.01);
    Require(AiTasksHandler.AlternateRequests == 1 && AiTasksHandler.Skips == 0,
        "blocked point skipped a task that had another approach");
});
Run("persistent no-progress skips an unreachable step after recovery retries", () =>
{
    var own = Setup(); NavigationRouter.RecoveryDirection = Vector2.down;
    MovementAutomation.Start(MovementMode.AiTasks); Tick();
    for (int second = 1; second <= 5; second++) Tick(second * 1.01);
    Require(AiTasksHandler.Skips == 1 && AiTasksHandler.NativeSteps == 0 && own.NetTransform.Snaps.Count == 0,
        "walker stayed forever or completed a task without reaching it");
});
Run("real physical progress does not trigger obstacle recovery", () =>
{
    var own = Setup(); MovementAutomation.Start(MovementMode.AiTasks); Tick();
    for (int second = 1; second <= 6; second++)
    {
        own.transform.position = new Vector2(second * 0.1f, 0); Tick(second * 1.01);
    }
    Require(NavigationRouter.RecoveryCalls == 0 && AiTasksHandler.Skips == 0, "moving player was treated as stuck");
});
Run("walking away and back from the same obstruction cannot reset the recovery budget", () =>
{
    var own = Setup(); NavigationRouter.RecoveryDirection = Vector2.down;
    MovementAutomation.Start(MovementMode.AiTasks); Tick();
    double at = 0;
    for (int attempt = 1; attempt <= 5; attempt++)
    {
        at += 1.1; Tick(at);
        if (attempt == 5) break;
        own.transform.position = new Vector2(0, -0.2f); Tick(at + 0.05);
        own.transform.position = Vector2.zero; Tick(at + 0.1); at += 0.1;
    }
    Require(AiTasksHandler.Skips == 1, "same blocked point recovered forever after tiny displacements");
});
Run("meeting pause time does not count toward an AI no-route timeout", () =>
{
    Setup(); NavigationRouter.Blocked = true; MovementAutomation.Start(MovementMode.AiTasks); Tick(0);
    Utils.isMeeting = true; Tick(10); Utils.isMeeting = false; Tick(11);
    Require(AiTasksHandler.Skips == 0 && AiTasksHandler.AlternateRequests == 0,
        "time spent in a meeting blacklisted a task");
    Tick(17); Require(AiTasksHandler.Skips == 1, "active no-route timeout no longer terminates retries");
});
foreach (var mode in new[] { MovementMode.AiTasks, MovementMode.ShadowFollow, MovementMode.TurboOrbit })
    Run(mode + " restores noclip-disabled body before ground navigation while preserving preference", () =>
    {
        var own = Setup(); own.Collider.enabled = false; CheatToggles.noClip = true;
        AiTasksHandler.RequireGroundBody = true;
        AddPlayer(7).transform.position = new Vector2(5, 0);
        MovementAutomation.Start(mode); Tick();
        Require(MovementAutomation.NeedsGroundCollision && own.Collider.enabled,
            "ground navigation left the owning collider disabled");
        Require(CheatToggles.noClip, "ground navigation discarded the user's noclip preference");
        Require(own.MyPhysics.PhysicalVelocity.sqrMagnitude > 0 && NavigationRouter.Calls > 0,
            "route or task resolution ran before the collision body was enabled");
        MovementAutomation.Stop();
        Require(!MovementAutomation.NeedsGroundCollision && CheatToggles.noClip,
            "stopping did not release the ground-navigation override");
    });
Run("foreign and paused callbacks cannot enable a disabled collider", () =>
{
    var own = Setup(); own.Collider.enabled = false;
    var other = AddPlayer(7); other.Collider.enabled = false;
    MovementAutomation.Start(MovementMode.AiTasks);
    MovementAutomation.FixedTick(other.MyPhysics);
    Require(!own.Collider.enabled && !other.Collider.enabled, "foreign callback changed a collision body");
    MenuUI.isGUIActive = true; Tick();
    Require(!own.Collider.enabled && NavigationRouter.Calls == 0, "menu pause enabled or navigated the body");
});
Run("recovery recalculates each frame and returns to routing immediately on clear floor", () =>
{
    var own = Setup(20); NavigationRouter.FloorClear = false; NavigationRouter.RecoveryDirection = Vector2.down;
    MovementAutomation.Start(MovementMode.AiTasks); Tick(); Tick(1.01);
    Require(NavigationRouter.RecoveryCalls == 1 && own.MyPhysics.PhysicalVelocity.y < 0, "first contact recovery was missing");
    var routeCalls = NavigationRouter.Calls;
    NavigationRouter.RecoveryDirection = new Vector2(0, 1); Tick(1.03);
    Require(NavigationRouter.RecoveryCalls == 2 && own.MyPhysics.PhysicalVelocity.y > 0,
        "controller retained the old direction after contact geometry changed");
    Require(NavigationRouter.Calls == routeCalls, "contact recovery unexpectedly used the ordinary route");
    NavigationRouter.FloorClear = true; Tick(1.05);
    Require(NavigationRouter.Calls == routeCalls + 1 && NavigationRouter.RecoveryCalls == 2 && own.MyPhysics.PhysicalVelocity.x > 0,
        "open floor retained contact recovery instead of resuming the task route");
    NavigationRouter.FloorClear = false; Tick(1.07);
    Require(NavigationRouter.RecoveryCalls == 2 && NavigationRouter.Calls == routeCalls + 2,
        "the old recovery window survived returning to open floor");
    Require(own.MyPhysics.SpeedWrites == 0 && own.NetTransform.Snaps.Count == 0, "recovery edited speed or snapped position");
});
Run("a newly blocked recovery stops the previous direction instead of continuing it", () =>
{
    var own = Setup(); NavigationRouter.FloorClear = false; NavigationRouter.RecoveryDirection = Vector2.down;
    MovementAutomation.Start(MovementMode.AiTasks); Tick(); Tick(1.01);
    Require(own.MyPhysics.PhysicalVelocity.y < 0, "fixture did not start contact recovery");
    NavigationRouter.RecoveryDirection = Vector2.zero; Tick(1.03);
    Require(NavigationRouter.RecoveryCalls == 2, "new contact geometry was not queried");
    Near(own.MyPhysics.PhysicalVelocity, Vector2.zero, "blocked recovery kept its previous walking direction");
    Require(MovementAutomation.Active && own.NetTransform.Snaps.Count == 0, "blocked recovery terminated all tasks or snapped");
});
foreach (float step in new[] { 0.02f, 0.1f, 0.25f })
    foreach (float speed in new[] { 20f, -20f })
        Run($"AI physical step stays bounded at timestep {step} and signed speed {speed}", () =>
        {
            var own = Setup(speed); own.MyPhysics.SpeedFactor = 3; Time.fixedDeltaTime = step;
            MovementAutomation.Start(MovementMode.AiTasks); Tick();
            Require(own.MyPhysics.PhysicalVelocity.magnitude * step <= 0.060001f,
                "one native walking step exceeded the checked recovery increment");
            Require(own.MyPhysics.PhysicalVelocity.x > 0 && own.MyPhysics.PhysicalVelocity.magnitude <= 2.5001f,
                "step cap reversed navigation or exceeded the walking-speed limit");
            Require(own.MyPhysics.SpeedWrites == 0 && own.NetTransform.Snaps.Count == 0,
                "step cap edited the selected speed or sent a snap");
        });
foreach (float step in new[] { 0f, -0.02f, float.NaN, float.PositiveInfinity })
    Run("invalid AI timestep " + step + " stops without moving", () =>
    {
        var own = Setup(); Time.fixedDeltaTime = step;
        MovementAutomation.Start(MovementMode.AiTasks); Tick();
        Require(!MovementAutomation.Active && own.MyPhysics.PhysicalVelocity.sqrMagnitude == 0,
            "unavailable timestep reached a walking action");
        Require(own.MyPhysics.SpeedWrites == 0 && own.NetTransform.Snaps.Count == 0,
            "unavailable timestep edited speed or sent a snap");
    });
foreach (double clock in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
    Run("invalid yo-yo clock " + clock + " stops before any recorded request", () =>
    {
        var own = Setup(); SaveTwoSpots(); MovementAutomation.Start(MovementMode.TeleportYoYo);
        Tick(clock);
        Require(own.NetTransform.Snaps.Count == 0 && !MovementAutomation.Active,
            "unavailable clock bypassed the scheduled request interval");
        Near(own.MyPhysics.PhysicalVelocity, Vector2.zero, "invalid clock retained own movement");
        Tick(100); Require(own.NetTransform.Snaps.Count == 0, "stopped yo-yo resumed after the clock recovered");
    });

foreach (float speed in new[] { 20f, -20f })
    foreach (float factor in new[] { 3f, -3f })
    {
        Run($"follow converges at signed base speed {speed} and native factor {factor} without overshooting", () =>
        {
            var own = Setup(speed); own.MyPhysics.SpeedFactor = factor;
            var target = AddPlayer(7); target.transform.position = new Vector2(5, 0);
            MovementAutomation.Start(MovementMode.ShadowFollow);
            Tick(); var goal = NavigationRouter.LastTo;
            Require(NavigationRouter.CanStand(goal) && Vector2.Distance(goal, target.GetTruePosition()) <= 1.01f &&
                goal.x < target.GetTruePosition().x, "stationary follow chose remote floor or the target's far side");
            float previousDistance = Vector2.Distance(own.GetTruePosition(), goal);
            for (int frame = 0; frame < 300; frame++)
            {
                Tick(frame * 0.02);
                var step = own.MyPhysics.PhysicalVelocity * Time.fixedDeltaTime;
                Require(step.magnitude <= 0.240001f && NavigationRouter.CanTravel(own.GetTruePosition(), own.GetTruePosition() + step),
                    "follow exceeded a clear bounded physical step");
                own.transform.position = (Vector2)own.transform.position + step;
                var distance = Vector2.Distance(own.GetTruePosition(), goal);
                Require(distance <= previousDistance + 0.0001f, "follow moved away from or overshot its stationary goal");
                previousDistance = distance;
            }
            Require(previousDistance <= 0.13f && MovementAutomation.Active, "follow failed to settle at its trailing position");
            Require(own.NetTransform.Snaps.Count == 0, "follow convergence teleported");
            MovementAutomation.Stop(); Exact(own.MyPhysics.PeekSpeed, speed, "follow did not restore its signed base speed");
        });
        Run($"orbit stays continuous at signed base speed {speed} and native factor {factor}", () =>
        {
            var own = Setup(speed); own.MyPhysics.SpeedFactor = factor;
            var target = AddPlayer(7); target.transform.position = new Vector2(5, 0);
            own.transform.position = new Vector2(6.2f, 0);
            MovementAutomation.Start(MovementMode.TurboOrbit);
            float rotation = 0; int movingFrames = 0;
            for (int frame = 0; frame < 300; frame++)
            {
                var before = own.GetTruePosition() - target.GetTruePosition();
                Tick(frame * 0.02);
                var step = own.MyPhysics.PhysicalVelocity * Time.fixedDeltaTime;
                Require(step.magnitude <= 0.240001f && NavigationRouter.CanTravel(own.GetTruePosition(), own.GetTruePosition() + step),
                    "orbit exceeded a clear bounded physical step");
                if (step.sqrMagnitude > 0) movingFrames++;
                own.transform.position = (Vector2)own.transform.position + step;
                var after = own.GetTruePosition() - target.GetTruePosition();
                Require(after.magnitude > 0.95f && after.magnitude < 1.4f, "orbit spiraled away from its target radius");
                rotation += MathF.Atan2(before.x * after.y - before.y * after.x,
                    before.x * after.x + before.y * after.y);
            }
            Require(movingFrames >= 290 && rotation > MathF.PI * 2 && MovementAutomation.Active,
                "orbit stalled, reversed, or failed to complete a continuous revolution");
            Require(own.NetTransform.Snaps.Count == 0 && target.MyPhysics.MovementWrites == 0,
                "orbit simulation snapped or moved the target");
            MovementAutomation.Stop(); Exact(own.MyPhysics.PeekSpeed, speed, "orbit did not restore its signed base speed");
        });
    }
Run("small received target corrections preserve the following heading", () =>
{
    var own = Setup(); var target = AddPlayer(7); target.transform.position = new Vector2(5, 0);
    MovementAutomation.Start(MovementMode.ShadowFollow); Tick();
    var initial = NavigationRouter.LastTo - target.GetTruePosition();
    target.transform.position = new Vector2(5.03f, 0); Tick(0.02);
    Near(NavigationRouter.LastTo - target.GetTruePosition(), initial, "small correction replaced the trailing heading");
    target.transform.position = new Vector2(4.97f, 0); Tick(0.04);
    Near(NavigationRouter.LastTo - target.GetTruePosition(), initial, "opposite correction flipped the trailing goal");
    Require(own.NetTransform.Snaps.Count == 0, "heading filter teleported");
});
Run("slow received motion accumulates into a meaningful following heading", () =>
{
    Setup(); var target = AddPlayer(7); target.transform.position = new Vector2(5, 0);
    MovementAutomation.Start(MovementMode.ShadowFollow); Tick();
    for (int frame = 1; frame <= 3; frame++)
    {
        target.transform.position = new Vector2(5, frame * 0.04f); Tick(frame * 0.02);
    }
    var heading = (target.GetTruePosition() - NavigationRouter.LastTo).normalized;
    Require(heading.x > 0.4f && heading.y > 0.3f && MathF.Abs(heading.magnitude - 1f) < 0.0001f,
        "per-frame noise threshold discarded accumulated real motion or snapped the heading");
});
Run("an opposite received heading turns gradually without collapsing the trailing offset", () =>
{
    Setup(); var target = AddPlayer(7); target.transform.position = new Vector2(5, 0);
    MovementAutomation.Start(MovementMode.ShadowFollow); Tick();
    target.transform.position = new Vector2(4, 0); Tick(0.02);
    var offset = target.GetTruePosition() - NavigationRouter.LastTo;
    var heading = offset.normalized;
    Require(offset.magnitude > 0.3f && heading.x > 0.7f && MathF.Abs(heading.y) > 0.1f,
        "opposite heading snapped, froze, or reduced the trailing offset to zero");
});
foreach (var mode in new[] { MovementMode.ShadowFollow, MovementMode.TurboOrbit })
    Run(mode + " pending route retains cache and retries next frame without an imposed half-second stop", () =>
    {
        var own = Setup(); AddPlayer(7).transform.position = new Vector2(5, 0);
        MovementAutomation.Start(mode); var resets = NavigationRouter.Resets;
        NavigationRouter.Blocked = true; Tick(); Tick(0.02); Tick(0.04);
        Require(NavigationRouter.MovingCalls == 3 && NavigationRouter.Resets == resets,
            "pending route was reset or hidden behind the controller retry interval");
        Near(own.MyPhysics.PhysicalVelocity, Vector2.zero, "pending route retained motion");
        NavigationRouter.Blocked = false; Tick(0.06);
        Require(own.MyPhysics.PhysicalVelocity.sqrMagnitude > 0 && NavigationRouter.Resets == resets,
            "available route waited for the old half-second deadline or lost its cache");
        Require(own.NetTransform.Snaps.Count == 0, "pending-route recovery sent a snap");
    });
Run("follow arrival holds its position without clearing route state", () =>
{
    var own = Setup(); AddPlayer(7).transform.position = new Vector2(5, 0);
    MovementAutomation.Start(MovementMode.ShadowFollow); Tick();
    var resets = NavigationRouter.Resets;
    own.transform.position = NavigationRouter.LastTo; Tick(0.02); Tick(0.04);
    Near(own.MyPhysics.PhysicalVelocity, Vector2.zero, "arrival did not clear own velocity");
    Require(MovementAutomation.Active && NavigationRouter.Resets == resets,
        "arrival used full Pause and discarded its route cache");
    Exact(own.MyPhysics.PeekSpeed, 1.75f, "arrival did not restore the captured speed");
    Require(own.NetTransform.Snaps.Count == 0, "arrival teleported");
});
foreach (var mode in new[] { MovementMode.ShadowFollow, MovementMode.TurboOrbit })
{
    Run(mode + " keeps an active pending search after five seconds and resumes immediately when ready", () =>
    {
        var own = Setup(); AddPlayer(7).transform.position = new Vector2(5, 0);
        var other = AddPlayer(8);
        MovementAutomation.Start(mode); var resets = NavigationRouter.Resets;
        NavigationRouter.Blocked = true; NavigationRouter.IsMovingRoutePending = true;
        Tick(); Tick(6);
        Require(MovementAutomation.Active && NavigationRouter.Resets == resets && NavigationRouter.MovingCalls == 2,
            "five-second timeout discarded an active bounded route search");
        Near(own.MyPhysics.PhysicalVelocity, Vector2.zero, "pending search retained own movement");
        int routeCalls = NavigationRouter.MovingCalls, movementWrites = own.MyPhysics.MovementWrites;
        MovementAutomation.FixedTick(other.MyPhysics);
        Require(NavigationRouter.MovingCalls == routeCalls && own.MyPhysics.MovementWrites == movementWrites &&
            other.MyPhysics.MovementWrites == 0, "foreign callback advanced a search or changed a player");
        NavigationRouter.Blocked = false; NavigationRouter.IsMovingRoutePending = false; Tick(6.02);
        Require(MovementAutomation.Active && own.MyPhysics.PhysicalVelocity.sqrMagnitude > 0 &&
            NavigationRouter.Resets == resets, "ready search retained a retry delay or was stopped");
        MovementAutomation.Stop(); routeCalls = NavigationRouter.MovingCalls; Tick(7);
        Near(own.MyPhysics.PhysicalVelocity, Vector2.zero, "stopped pending mode retained velocity");
        Require(NavigationRouter.MovingCalls == routeCalls && own.NetTransform.Snaps.Count == 0,
            "stopped mode advanced routing or sent a snap");
    });
    Run(mode + " stops when a long pending search becomes unavailable", () =>
    {
        var own = Setup(); AddPlayer(7).transform.position = new Vector2(5, 0);
        MovementAutomation.Start(mode);
        NavigationRouter.Blocked = true; NavigationRouter.IsMovingRoutePending = true;
        Tick(); Tick(6); Require(MovementAutomation.Active, "pending search was stopped prematurely");
        NavigationRouter.IsMovingRoutePending = false; Tick(6.02);
        Require(!MovementAutomation.Active, "finished unavailable search waited for the maximum pending deadline");
        Near(own.MyPhysics.PhysicalVelocity, Vector2.zero, "unavailable search left own movement active");
        Require(own.NetTransform.Snaps.Count == 0, "unavailable search sent a snap");
    });
    Run(mode + " keeps the thirty-second limit even if a search remains pending", () =>
    {
        var own = Setup(); AddPlayer(7).transform.position = new Vector2(5, 0);
        MovementAutomation.Start(mode);
        NavigationRouter.Blocked = true; NavigationRouter.IsMovingRoutePending = true;
        Tick(); Tick(29.99); Require(MovementAutomation.Active, "pending search lost its intended time allowance");
        Tick(30);
        Require(!MovementAutomation.Active && !NavigationRouter.IsMovingRoutePending,
            "pending flag bypassed the bounded stop or survived its reset");
        Near(own.MyPhysics.PhysicalVelocity, Vector2.zero, "pending deadline left own movement active");
        Require(own.NetTransform.Snaps.Count == 0, "pending deadline sent a snap");
    });
}
foreach (var mode in new[] { MovementMode.ShadowFollow, MovementMode.TurboOrbit })
    foreach (float factor in new[] { 0f, float.NaN, float.PositiveInfinity })
        Run(mode + " rejects unavailable native speed factor " + factor, () =>
        {
            var own = Setup(); own.MyPhysics.SpeedFactor = factor;
            AddPlayer(7).transform.position = new Vector2(5, 0);
            MovementAutomation.Start(mode); Tick();
            Require(own.MyPhysics.LastInput.sqrMagnitude == 0 && own.NetTransform.Snaps.Count == 0,
                "unavailable native speed reached a nonzero steering action");
        });
foreach (var mode in new[] { MovementMode.ShadowFollow, MovementMode.TurboOrbit })
    foreach (float step in new[] { 0f, float.NaN })
        Run(mode + " rejects unavailable fixed timestep " + step, () =>
        {
            var own = Setup(); Time.fixedDeltaTime = step;
            AddPlayer(7).transform.position = new Vector2(5, 0);
            MovementAutomation.Start(mode); Tick();
            Require(own.MyPhysics.LastInput.sqrMagnitude == 0 && own.NetTransform.Snaps.Count == 0,
                "unavailable timestep reached a nonzero steering action");
        });

Run("follow selects nearby direct floor when the preferred trailing spot is in a wall", () =>
{
    var own = Setup(); var target = AddPlayer(7); target.transform.position = new Vector2(5, 0);
    FixtureWall(4.05f, -0.35f, 4.65f, 0.35f);
    Require(!NavigationRouter.CanStand(new Vector2(4.35f, 0)) && NavigationRouter.CanStand(own.GetTruePosition()),
        "fixture did not distinguish a blocked goal from the clear actor");
    MovementAutomation.Start(MovementMode.ShadowFollow); Tick();
    var goal = NavigationRouter.LastTo;
    Require(NavigationRouter.MovingCalls == 1 && NavigationRouter.CanStand(goal) && NavigationRouter.CanTravel(own.GetTruePosition(), goal),
        "follow sent a blocked destination or rejected an open approach");
    Require(Vector2.Distance(goal, target.GetTruePosition()) <= 1.01f && MathF.Abs(goal.y) > 0.35f,
        "wall fallback left the selected player's nearby floor");
    for (int frame = 1; frame <= 20; frame++)
    {
        var next = own.GetTruePosition() + own.MyPhysics.PhysicalVelocity * Time.fixedDeltaTime;
        Require(NavigationRouter.CanStand(next) && NavigationRouter.CanTravel(own.GetTruePosition(), next),
            "fallback walking crossed the fixture wall");
        own.transform.position = next; Tick(frame * 0.02);
    }
    Require(own.GetTruePosition().magnitude > 0.5f && NavigationRouter.BlockedMovingGoals == 0 && own.NetTransform.Snaps.Count == 0,
        "blocked preferred point prevented walking, reached the router, or caused a snap");
});
Run("orbit chooses a legal alternate arc when the forward arc is blocked", () =>
{
    var own = Setup(); var target = AddPlayer(7); target.transform.position = new Vector2(5, 0);
    own.transform.position = new Vector2(6.2f, 0);
    FixtureWall(4.5f, 0.03f, 6.5f, 1.5f);
    MovementAutomation.Start(MovementMode.TurboOrbit); Tick();
    var goal = NavigationRouter.LastTo;
    Require(NavigationRouter.MovingCalls == 1 && goal.y < -0.1f && NavigationRouter.CanStand(goal) &&
        NavigationRouter.CanTravel(own.GetTruePosition(), goal), "blocked forward arc was kept despite an open reverse arc");
    Require(Vector2.Distance(goal, target.GetTruePosition()) <= 1.21f && own.MyPhysics.PhysicalVelocity.y < 0,
        "alternate arc left the target area or moved toward the wall");
    Require(NavigationRouter.BlockedMovingGoals == 0 && own.NetTransform.Snaps.Count == 0, "orbit routed into a blocked goal or snapped");
});
Run("orbit can use a larger clear arc when both short arcs are blocked", () =>
{
    var own = Setup(); var target = AddPlayer(7); target.transform.position = new Vector2(5, 0);
    own.transform.position = new Vector2(6.2f, 0);
    var blocked = new List<Vector2>();
    foreach (float side in new[] { -1f, 1f })
        foreach (float radius in new[] { 0.6f, 0.9f, 1.2f })
            blocked.Add(target.GetTruePosition() + new Vector2(MathF.Cos(side * 0.35f), MathF.Sin(side * 0.35f)) * radius);
    NavigationRouter.StandPredicate = point => blocked.All(center => Vector2.Distance(point, center) > 0.055f);
    NavigationRouter.TravelPredicate = (from, to) => FixtureSegmentClear(from, to, NavigationRouter.StandPredicate);
    MovementAutomation.Start(MovementMode.TurboOrbit); Tick();
    var relative = NavigationRouter.LastTo - target.GetTruePosition();
    Require(NavigationRouter.MovingCalls == 1 && MathF.Abs(MathF.Atan2(relative.y, relative.x)) > 0.5f && relative.magnitude <= 1.21f,
        "both blocked short arcs prevented selecting a larger nearby arc");
    Require(NavigationRouter.CanStand(NavigationRouter.LastTo) && NavigationRouter.CanTravel(own.GetTruePosition(), NavigationRouter.LastTo) &&
        own.MyPhysics.PhysicalVelocity.sqrMagnitude > 0 && NavigationRouter.BlockedMovingGoals == 0 && own.NetTransform.Snaps.Count == 0,
        "larger arc crossed blocked floor, failed to walk, or snapped");
});
foreach (var mode in new[] { MovementMode.ShadowFollow, MovementMode.TurboOrbit })
{
    Run(mode + " waits armed with zero movement when all nearby floor is blocked then resumes after the target moves", () =>
    {
        var own = Setup(); var target = AddPlayer(7); target.transform.position = new Vector2(5, 0);
        var blockedCenter = target.GetTruePosition();
        NavigationRouter.StandPredicate = point => Vector2.Distance(point, blockedCenter) > 1.4f;
        MovementAutomation.Start(mode);
        foreach (double at in new[] { 0d, 1d, 20d })
        {
            int checks = NavigationRouter.StandChecks; Tick(at);
            Require(NavigationRouter.StandChecks - checks <= 80, "fully blocked floor exceeded the bounded candidate search");
            Near(own.MyPhysics.PhysicalVelocity, Vector2.zero, "fully blocked floor retained movement");
            Require(MovementAutomation.Active && NavigationRouter.MovingCalls == 0 && own.NetTransform.Snaps.Count == 0,
                "no open destination disarmed the mode or attempted a blocked route/snap");
        }
        target.transform.position = new Vector2(9, 0); Tick(20.02);
        Require(MovementAutomation.Active && own.MyPhysics.PhysicalVelocity.sqrMagnitude > 0 && NavigationRouter.BlockedMovingGoals == 0,
            "target reaching open floor did not bypass the failed-goal wait");
    });
    Run(mode + " immediately replaces a cached destination that becomes blocked", () =>
    {
        var own = Setup(); AddPlayer(7).transform.position = new Vector2(5, 0);
        MovementAutomation.Start(mode); Tick(); var former = NavigationRouter.LastTo;
        NavigationRouter.StandPredicate = point => Vector2.Distance(point, former) >= 0.12f;
        NavigationRouter.TravelPredicate = (from, to) => FixtureSegmentClear(from, to, NavigationRouter.StandPredicate);
        Tick(0.02);
        var replacement = NavigationRouter.LastTo;
        Require(NavigationRouter.MovingCalls == 2 && Vector2.Distance(former, replacement) >= 0.12f && NavigationRouter.CanStand(replacement),
            "a blocked cached destination survived until the cache timer expired");
        Require(own.MyPhysics.PhysicalVelocity.sqrMagnitude > 0 && NavigationRouter.BlockedMovingGoals == 0 && own.NetTransform.Snaps.Count == 0,
            "replacement goal did not produce bounded walking or attempted a blocked route/snap");
    });
    Run(mode + " time without any legal goal does not expire the next route attempt", () =>
    {
        var own = Setup(); var target = AddPlayer(7); target.transform.position = new Vector2(5, 0);
        MovementAutomation.Start(mode); NavigationRouter.Blocked = true; Tick();
        NavigationRouter.StandPredicate = point => Vector2.Distance(point, target.GetTruePosition()) > 1.4f; Tick(1);
        NavigationRouter.StandPredicate = null; Tick(10);
        Require(MovementAutomation.Active, "no-goal wait reused an expired route timeout");
        Near(own.MyPhysics.PhysicalVelocity, Vector2.zero, "first pending route tick retained movement");
        NavigationRouter.Blocked = false; Tick(10.02);
        Require(MovementAutomation.Active && own.MyPhysics.PhysicalVelocity.sqrMagnitude > 0,
            "fresh route allowance did not resume normal walking");
    });
    Run(mode + " preserves outward local-contact recovery despite no direct candidate travel", () =>
    {
        var own = Setup(); AddPlayer(7).transform.position = new Vector2(5, 0);
        NavigationRouter.StandPredicate = point => MathF.Abs(point.x) > 0.05f;
        NavigationRouter.RecoveryDirection = new Vector2(-1, 0);
        MovementAutomation.Start(mode); Tick();
        Require(NavigationRouter.RecoveryCalls == 1 && own.MyPhysics.PhysicalVelocity.x < 0,
            "blocked local clearance rejected all legal target goals instead of walking outward");
        own.transform.position = new Vector2(-0.1f, 0); Tick(0.02);
        Require(own.MyPhysics.PhysicalVelocity.x > 0 && NavigationRouter.BlockedMovingGoals == 0 && own.NetTransform.Snaps.Count == 0,
            "clear local floor did not resume the selected target route");
    });
    Run(mode + " survives same-player lobby loading and retains its target at round handoff", () =>
    {
        var own = Setup(); Utils.isLobby = true; Utils.isInGame = false; ShipStatus.Instance = null;
        var target = AddPlayer(7); target.transform.position = new Vector2(5, 0);
        SaveTwoSpots(); MovementAutomation.Start(mode); Tick();
        Utils.isLobby = false; Utils.isInGame = true; Tick(1);
        Require(MovementAutomation.Active && MovementAutomation.SelectedPlayer == target, "shipless loading disarmed the same lobby's target mode");
        Near(own.MyPhysics.PhysicalVelocity, Vector2.zero, "shipless loading retained motion");
        ShipStatus.Instance = new(); Tick(2);
        Require(MovementAutomation.Mode == mode && MovementAutomation.SelectedPlayer == target && own.MyPhysics.PhysicalVelocity.sqrMagnitude > 0,
            "round handoff lost its existing target or inherited a stopped mode");
        Require(!MovementAutomation.HasPointA && !MovementAutomation.HasPointB && own.NetTransform.Snaps.Count == 0,
            "round handoff preserved lobby snap destinations or teleported");
        MovementAutomation.Stop(); Exact(own.MyPhysics.PeekSpeed, 1.75f, "handoff lost the original owned speed");
    });
    Run(mode + " cannot carry lobby target automation onto a replacement player", () =>
    {
        var old = Setup(); Utils.isLobby = true; Utils.isInGame = false; ShipStatus.Instance = null;
        var target = AddPlayer(7); target.transform.position = new Vector2(5, 0);
        MovementAutomation.Start(mode); Tick();
        var replacement = new PlayerControl(0, true, 4.25f); PlayerControl.LocalPlayer = replacement;
        PlayerControl.AllPlayerControls = [replacement, target]; Utils.isLobby = false; Utils.isInGame = true; ShipStatus.Instance = new(); Tick(1);
        Require(!MovementAutomation.Active && MovementAutomation.SelectedPlayer == null && replacement.MyPhysics.MovementWrites == 0 &&
            replacement.MyPhysics.SpeedWrites == 0, "replacement player inherited the previous lobby's automation");
        Exact(old.MyPhysics.PeekSpeed, 1.75f, "replacement transition did not release old speed");
        Require(replacement.NetTransform.Snaps.Count == 0 && target.MyPhysics.MovementWrites == 0, "transition affected another actor");
    });
}
Run("choosing a different player discards the previous player's side-offset cache", () =>
{
    Setup(); AddPlayer(7).transform.position = new Vector2(5, 0); var next = AddPlayer(8); next.transform.position = new Vector2(10, 0);
    FixtureWall(4.05f, -0.35f, 4.65f, 0.35f);
    MovementAutomation.Start(MovementMode.ShadowFollow); Tick(); Require(MathF.Abs(NavigationRouter.LastTo.y) > 0.35f, "fixture did not produce a side-offset cache");
    NavigationRouter.StandPredicate = null; NavigationRouter.TravelPredicate = null;
    MovementAutomation.CycleTarget(); Tick(0.02);
    Require(MovementAutomation.SelectedPlayer == next && MathF.Abs(NavigationRouter.LastTo.y) < 0.0001f && NavigationRouter.LastTo.x < 10,
        "new clear target reused the previous player's wall-side offset");
    Require(NavigationRouter.CanStand(NavigationRouter.LastTo) && NavigationRouter.BlockedMovingGoals == 0,
        "target change bypassed destination clearance");
});

foreach (var mode in new[] { MovementMode.ShadowFollow, MovementMode.TurboOrbit })
    foreach (float initialLead in new[] { 20f, 40f })
        foreach (float baseSpeed in new[] { 1.75f, -1.75f })
            Run($"{mode} catches a continuously moving target from {initialLead} units at signed speed {baseSpeed} and slider {(baseSpeed < 0 ? 1 : 3)}", () =>
            {
                var own = Setup(baseSpeed); var target = AddPlayer(7);
                Plugin.targetMovementMultiplier.Value = baseSpeed < 0 ? 1f : 3f;
                target.transform.position = new Vector2(initialLead, 0);
                MovementAutomation.Start(mode);
                for (int frame = 0; frame < 1500; frame++)
                {
                    float at = frame * Time.fixedDeltaTime;
                    // Receiving a distant actor does not depend on a renderer or
                    // local scene visibility. The target moves throughout chase.
                    target.transform.position = new Vector2(initialLead + 1.75f * at, 3f * MathF.Sin(at * 0.15f));
                    Tick(at);
                    if (frame == 0)
                        Require(own.MyPhysics.PhysicalVelocity.sqrMagnitude > 0, "distant target required proximity before moving");
                    Require(MovementAutomation.Active, "continuous distant pursuit lost its mode");
                    ApplyCheckedTargetStep(own);
                }
                float finalSeparation = Vector2.Distance(own.GetTruePosition(), target.GetTruePosition());
                Console.WriteLine(FormattableString.Invariant($"MEASURE pursuit mode={mode} initialSeparation={initialLead:F3} finalSeparation={finalSeparation:F3} simulatedSeconds=30 frames=1500 targetWorldSpeedX=1.75 targetWorldSpeedYMax=0.45 signedBase={baseSpeed} slider={Plugin.targetMovementMultiplier.Value}"));
                Require(finalSeparation < (baseSpeed < 0 ? 4f : 2f),
                    "faster pursuit never closed a moving target's initial lead");
                Require(own.NetTransform.Snaps.Count == 0 && target.MyPhysics.SpeedWrites == 0 && target.MyPhysics.MovementWrites == 0,
                    "pursuit teleported or changed the remote actor");
                MovementAutomation.Stop(); Exact(own.MyPhysics.PeekSpeed, baseSpeed, "distant pursuit lost its original signed speed");
            });

    foreach (float baseSpeed in new[] { 1.75f, -1.75f })
    Run("target slider increases orbit angular rate at signed base speed " + baseSpeed, () =>
    {
        float Measure(float multiplier)
        {
            var own = Setup(baseSpeed); Plugin.targetMovementMultiplier.Value = multiplier;
            var target = AddPlayer(7); target.transform.position = new Vector2(5, 0);
            own.transform.position = new Vector2(6.2f, 0);
            MovementAutomation.Start(MovementMode.TurboOrbit);
            float rotation = 0;
            for (int frame = 0; frame < 400; frame++)
            {
                var before = own.GetTruePosition() - target.GetTruePosition();
                Tick(frame * Time.fixedDeltaTime); ApplyCheckedTargetStep(own);
                var after = own.GetTruePosition() - target.GetTruePosition();
                Require(after.magnitude > 0.95f && after.magnitude < 1.4f, "increasing the slider destroyed the orbit radius");
                rotation += MathF.Atan2(before.x * after.y - before.y * after.x, before.x * after.x + before.y * after.y);
            }
            MovementAutomation.Stop(); Exact(own.MyPhysics.PeekSpeed, baseSpeed, "slider comparison failed to restore original speed");
            return rotation;
        }
        float slow = Measure(1f), fast = Measure(4f);
        Require(slow > MathF.PI * 2 && fast > slow * 2f,
            "orbit's target-distance throttle or old step cap erased the slider effect");
        Console.WriteLine(FormattableString.Invariant($"MEASURE orbit signedBase={baseSpeed} simulatedSeconds=8 slider1Radians={slow:F3} slider4Radians={fast:F3} ratio={fast / slow:F3} slider1Turns={slow / (2f * MathF.PI):F3} slider4Turns={fast / (2f * MathF.PI):F3}"));
    });

foreach (var mode in new[] { MovementMode.ShadowFollow, MovementMode.TurboOrbit })
    Run(mode + " updates bounded target multiplier without compounding or borrowing the dash slider", () =>
    {
        const float original = -1.234567f;
        var own = Setup(original); AddPlayer(7).transform.position = new Vector2(1.5f, 0);
        if (mode == MovementMode.TurboOrbit) own.transform.position = new Vector2(2.7f, 0);
        Plugin.stuntMultiplier.Value = 1;
        MovementAutomation.Start(mode);
        foreach (var setting in new (float Input, float Expected)[]
        {
            (1f, 1f), (4f, 4f), (float.NaN, 3f), (float.PositiveInfinity, 3f), (-9f, 1f), (99f, 4f),
        })
        {
            Plugin.targetMovementMultiplier.Value = setting.Input;
            Tick(Time.realtimeSinceStartupAsDouble + 0.02);
            Exact(own.MyPhysics.PeekSpeed, original * setting.Expected, "target config compounded or escaped finite bounds");
            Require(own.MyPhysics.LastInput.sqrMagnitude <= 1.00001f, "config update sent unbounded input");
        }
        MovementAutomation.Start(MovementMode.ZigzagDash); Tick(1);
        Exact(own.MyPhysics.PeekSpeed, original, "dash reused the target multiplier or a boosted snapshot");
        MovementAutomation.Stop(); Exact(own.MyPhysics.PeekSpeed, original, "config updates lost the original signed bits");
    });

foreach (var mode in new[] { MovementMode.ShadowFollow, MovementMode.TurboOrbit })
    foreach (var pause in new (string Name, Action Change, Action Resume)[]
    {
        ("menu", () => MenuUI.isGUIActive = true, () => MenuUI.isGUIActive = false),
        ("chat", () => HudManager.Existing.Chat.IsOpenOrOpening = true, () => HudManager.Existing.Chat.IsOpenOrOpening = false),
        ("meeting", () => Utils.isMeeting = true, () => Utils.isMeeting = false),
        ("focus", () => Application.isFocused = false, () => Application.isFocused = true),
        ("vent", () => PlayerControl.LocalPlayer.inVent = true, () => PlayerControl.LocalPlayer.inVent = false),
    })
        Run(mode + " restores signed target boost on " + pause.Name + " and recaptures the resumed native speed", () =>
        {
            const float original = -1.234567f, changed = -2.345678f;
            var own = Setup(original); AddPlayer(7).transform.position = new Vector2(8, 0);
            own.MyPhysics.SpeedFactor = -3;
            MovementAutomation.Start(mode); Tick();
            Require(own.MyPhysics.PeekSpeed < original && own.MyPhysics.PhysicalVelocity.x > 0, "target mode failed to boost or compensate the signed factor");
            pause.Change(); Tick(0.2);
            Exact(own.MyPhysics.PeekSpeed, original, "temporary pause failed exact signed restoration");
            Near(own.MyPhysics.PhysicalVelocity, Vector2.zero, "temporary target pause retained motion");
            Require(MovementAutomation.Active, "temporary controller pause dropped the mode");
            own.MyPhysics.Speed = changed; pause.Resume(); Tick(0.4);
            Require(own.MyPhysics.PhysicalVelocity.x > 0, "target mode did not resume after pause");
            MovementAutomation.Stop(); Exact(own.MyPhysics.PeekSpeed, changed, "stop overwrote the new native speed selected while paused");
            Near(own.MyPhysics.PhysicalVelocity, Vector2.zero, "target stop retained motion");
        });

foreach (var mode in new[] { MovementMode.ShadowFollow, MovementMode.TurboOrbit })
    foreach (float fixedStep in new[] { 0.005f, 0.02f, 0.1f, 0.25f })
        Run($"{mode} high signed native speed respects swept step and world-speed limits at dt {fixedStep}", () =>
        {
            var own = Setup(-20f); own.MyPhysics.SpeedFactor = -3f; Time.fixedDeltaTime = fixedStep;
            AddPlayer(7).transform.position = new Vector2(40, 0);
            MovementAutomation.Start(mode); Tick();
            Require(own.MyPhysics.PhysicalVelocity.x > 0 && own.MyPhysics.PhysicalVelocity.magnitude <= 12.0001f,
                "negative base/factor reversed chase or bypassed the world-speed cap");
            ApplyCheckedTargetStep(own);
            MovementAutomation.Stop(); Exact(own.MyPhysics.PeekSpeed, -20f, "timestep braking changed original native speed");
        });

foreach (var mode in new[] { MovementMode.ShadowFollow, MovementMode.TurboOrbit })
    foreach (float baseSpeed in new[] { 20f, -20f })
        Run(mode + " reduces the actual boosted step before a thin blocked corner at signed speed " + baseSpeed, () =>
        {
            var own = Setup(baseSpeed); own.MyPhysics.SpeedFactor = -3;
            AddPlayer(7).transform.position = new Vector2(5, 0);
            FixtureWall(0.15f, -1f, 0.19f, 1f);
            NavigationRouter.ForcedDirection = new Vector2(1, 0);
            MovementAutomation.Start(mode); Tick();
            var step = own.MyPhysics.PhysicalVelocity * Time.fixedDeltaTime;
            Require(step.x > 0f && step.x < 0.15f && MathF.Abs(step.y) < 0.0001f,
                "boosted step entered the corner or rejected a safe shorter step");
            Require(NavigationRouter.TravelChecksRecorded.Any(check => !check.Clear && check.From.sqrMagnitude < 0.0001f && check.To.x > 0.2f),
                "controller never swept the larger proposed step against live geometry");
            ApplyCheckedTargetStep(own);
            MovementAutomation.Stop(); Exact(own.MyPhysics.PeekSpeed, baseSpeed, "corner braking changed the saved signed speed");
        });

foreach (var mode in new[] { MovementMode.ShadowFollow, MovementMode.TurboOrbit })
    Run(mode + " clears old velocity and restores speed when every bounded proposed step is blocked", () =>
    {
        var own = Setup(1.234567f); AddPlayer(7).transform.position = new Vector2(5, 0);
        MovementAutomation.Start(mode); Tick();
        Require(own.MyPhysics.PhysicalVelocity.sqrMagnitude > 0, "fixture did not start moving");
        NavigationRouter.TravelChecksRecorded.Clear(); NavigationRouter.TravelPredicate = (_, _) => false;
        Tick(0.02);
        Near(own.MyPhysics.PhysicalVelocity, Vector2.zero, "failed sweeps retained an old native velocity");
        Exact(own.MyPhysics.PeekSpeed, 1.234567f, "failed sweeps retained the target boost");
        Require(MovementAutomation.Active && NavigationRouter.TravelChecksRecorded.Count <= 30,
            "blocked corner dropped its mode or tried an unbounded number of candidates/steps");
        Require(own.NetTransform.Snaps.Count == 0, "failed corner sweeps snapped the actor");
    });

foreach (var mode in new[] { MovementMode.ShadowFollow, MovementMode.TurboOrbit })
    Run(mode + " caps physical movement at an intermediate checked waypoint", () =>
    {
        var own = Setup(-20f); own.MyPhysics.SpeedFactor = 3;
        AddPlayer(7).transform.position = new Vector2(20, 0);
        NavigationRouter.ForcedDirection = new Vector2(1, 1).normalized;
        NavigationRouter.SteeringDistance = 0.07f;
        MovementAutomation.Start(mode); Tick();
        var step = own.MyPhysics.PhysicalVelocity * Time.fixedDeltaTime;
        Require(step.x > 0 && step.y > 0 && step.magnitude <= 0.070001f,
            "high-speed chase crossed the router's intermediate steering point");
        ApplyCheckedTargetStep(own);
        NavigationRouter.SteeringDistance = 0f; Tick(0.02);
        Near(own.MyPhysics.PhysicalVelocity, Vector2.zero, "unavailable steering distance reused the previous step");
        Exact(own.MyPhysics.PeekSpeed, -20f, "unavailable waypoint retained a boost");
    });

foreach (var mode in new[] { MovementMode.ShadowFollow, MovementMode.TurboOrbit })
    Run(mode + " keeps contact recovery short despite the target speed boost", () =>
    {
        var own = Setup(-20f); own.MyPhysics.SpeedFactor = -3;
        AddPlayer(7).transform.position = new Vector2(5, 0);
        NavigationRouter.StandPredicate = point => MathF.Abs(point.x) > 0.05f;
        NavigationRouter.RecoveryDirection = new Vector2(-1, 0);
        NavigationRouter.SteeringDistance = 0.15f;
        MovementAutomation.Start(mode); Tick();
        var step = own.MyPhysics.PhysicalVelocity * Time.fixedDeltaTime;
        Require(NavigationRouter.RecoveryCalls == 1 && step.x < 0 && step.magnitude <= 0.060001f &&
            own.MyPhysics.PhysicalVelocity.magnitude <= 2.50001f, "target boost turned overlapping contact recovery into an unchecked long step");
        MovementAutomation.Stop(); Exact(own.MyPhysics.PeekSpeed, -20f, "contact recovery failed to restore the original speed");
        Require(own.NetTransform.Snaps.Count == 0, "contact recovery snapped the actor");
    });

foreach (var mode in new[] { MovementMode.ShadowFollow, MovementMode.TurboOrbit })
{
    Run(mode + " throttles repeated failed step sweeps and stops within the bounded corner timeout", () =>
    {
        var own = Setup(-1.234567f); AddPlayer(7).transform.position = new Vector2(5, 0);
        NavigationRouter.TravelPredicate = (_, _) => false;
        MovementAutomation.Start(mode); Tick();
        int queries = NavigationRouter.TravelChecks;
        Tick(0.05);
        Require(NavigationRouter.TravelChecks == queries && NavigationRouter.MovingCalls == 1,
            "blocked-step retry performed new searches during its short cooldown");
        for (int attempt = 1; attempt <= 19; attempt++) Tick(attempt * 0.25);
        Tick(4.99); Require(MovementAutomation.Active, "blocked corner expired before five elapsed seconds");
        Tick(5.1);
        Require(!MovementAutomation.Active, "repeated sweep failures reset their stop timer forever");
        Near(own.MyPhysics.PhysicalVelocity, Vector2.zero, "corner timeout retained stale velocity");
        Exact(own.MyPhysics.PeekSpeed, -1.234567f, "corner timeout failed signed boost restoration");
        queries = NavigationRouter.TravelChecks; Tick(10);
        Require(NavigationRouter.TravelChecks == queries && own.NetTransform.Snaps.Count == 0, "stopped corner resumed querying or snapped");
    });
    Run(mode + " clears failed-sweep timeout only after a successful submitted movement", () =>
    {
        var own = Setup(1.234567f); AddPlayer(7).transform.position = new Vector2(5, 0);
        NavigationRouter.TravelPredicate = (_, _) => false;
        MovementAutomation.Start(mode); Tick(); Tick(4);
        NavigationRouter.TravelPredicate = null; Tick(4.2);
        Require(own.MyPhysics.PhysicalVelocity.sqrMagnitude > 0 && MovementAutomation.Active, "clear corner did not submit resumed native movement");
        ApplyCheckedTargetStep(own);
        NavigationRouter.TravelPredicate = (_, _) => false; Tick(4.4); Tick(5.2);
        Require(MovementAutomation.Active, "a successful movement retained the previous blocked timer");
        Near(own.MyPhysics.PhysicalVelocity, Vector2.zero, "new blocked episode retained resumed velocity");
        Tick(8.9); Require(MovementAutomation.Active, "fresh blocked episode expired before its own five-second limit");
        Tick(9.5); Require(!MovementAutomation.Active, "fresh blocked episode never reached its terminal timeout");
        Exact(own.MyPhysics.PeekSpeed, 1.234567f, "second blocked episode lost original speed");
        Require(own.NetTransform.Snaps.Count == 0, "corner retries sent a snap");
    });
}

foreach (var mode in new[] { MovementMode.ShadowFollow, MovementMode.TurboOrbit })
    Run(mode + " walks toward an alternate floor endpoint after the first completed route is unreachable", () =>
    {
        var own = Setup(); var target = AddPlayer(7); target.transform.position = new Vector2(20, 0);
        // All nearby endpoints stand clear, but a long direct segment is not a
        // usable approach. Short physical walking steps remain independently safe.
        NavigationRouter.TravelPredicate = (from, to) => Vector2.Distance(from, to) <= 0.25f;
        Vector2? first = null;
        NavigationRouter.MovingGoalUnreachable = goal =>
        {
            first ??= goal;
            return Vector2.Distance(goal, first.Value) < 0.001f;
        };
        MovementAutomation.Start(mode); Tick();
        Require(NavigationRouter.CompletedFailedGoals.Count == 1 && MovementAutomation.Active,
            "first unreachable endpoint did not produce a completed failure");
        Near(own.MyPhysics.PhysicalVelocity, Vector2.zero, "failed approach retained velocity");
        for (int frame = 1; frame <= 250; frame++)
        {
            Tick(frame * Time.fixedDeltaTime); ApplyCheckedTargetStep(own);
            Require(MovementAutomation.Active, "valid alternative approach was treated as terminal failure");
        }
        Require(NavigationRouter.MovingDestinations.Any(point => Vector2.Distance(point, first.Value) > 0.1f) &&
            Vector2.Distance(own.GetTruePosition(), target.GetTruePosition()) < 3f,
            "controller kept choosing the inaccessible pocket instead of walking toward another candidate");
        Require(NavigationRouter.BlockedMovingGoals == 0 && own.NetTransform.Snaps.Count == 0 && target.MyPhysics.MovementWrites == 0,
            "alternate approach routed into blocked floor, snapped, or moved the target");
        MovementAutomation.Stop(); Exact(own.MyPhysics.PeekSpeed, 1.75f, "alternate approach lost the original native speed");
    });

Run("orbit candidate ordering cannot skip the reachable reverse short arc at the middle radius", () =>
{
    var own = Setup(); var target = AddPlayer(7); target.transform.position = new Vector2(20, 0);
    NavigationRouter.TravelPredicate = (from, to) => Vector2.Distance(from, to) <= 0.25f;
    var reachable = target.GetTruePosition() + new Vector2(MathF.Cos(MathF.PI - 0.35f), MathF.Sin(MathF.PI - 0.35f)) * 0.9f;
    NavigationRouter.MovingGoalUnreachable = point => Vector2.Distance(point, reachable) > 0.001f;
    MovementAutomation.Start(MovementMode.TurboOrbit);
    for (int attempt = 0; attempt < 18 && own.MyPhysics.PhysicalVelocity.sqrMagnitude == 0; attempt++) Tick(attempt * 0.2);
    Require(own.MyPhysics.PhysicalVelocity.sqrMagnitude > 0 && Vector2.Distance(NavigationRouter.LastTo, reachable) < 0.001f,
        "changing orbit direction reordered failures and skipped the only reachable floor candidate");
    ApplyCheckedTargetStep(own);
    Require(own.NetTransform.Snaps.Count == 0 && MovementAutomation.Active, "reachable candidate required a snap or lost the mode");
});

foreach (var mode in new[] { MovementMode.ShadowFollow, MovementMode.TurboOrbit })
{
    Run(mode + " exhausts a finite set of distinct unreachable floor candidates without resetting the total timeout", () =>
    {
        var own = Setup(-1.234567f); AddPlayer(7).transform.position = new Vector2(20, 0);
        NavigationRouter.TravelPredicate = (from, to) => Vector2.Distance(from, to) <= 0.25f;
        NavigationRouter.MovingGoalUnreachable = _ => true;
        MovementAutomation.Start(mode);
        for (int attempt = 0; attempt <= 25; attempt++)
        {
            Tick(attempt * 0.2);
            Near(own.MyPhysics.PhysicalVelocity, Vector2.zero, "unreachable alternative submitted an old velocity");
        }
        int expectedChoices = mode == MovementMode.ShadowFollow ? 24 : 18;
        var unique = new List<Vector2>();
        foreach (var point in NavigationRouter.CompletedFailedGoals)
            if (unique.All(previous => Vector2.Distance(previous, point) > 0.001f)) unique.Add(point);
        Require(unique.Count == expectedChoices && NavigationRouter.CompletedFailedGoals.Count <= 26,
            "finite candidate search repeated old choices, skipped available variants, or exceeded its bounded calls");
        Require(!MovementAutomation.Active, "candidate changes kept resetting the no-route timeout");
        Exact(own.MyPhysics.PeekSpeed, -1.234567f, "exhausted alternatives changed original signed speed");
        Require(own.NetTransform.Snaps.Count == 0, "unreachable alternatives snapped the actor");
    });
    Run(mode + " preserves a pending endpoint and switches candidates only after a completed route failure", () =>
    {
        var own = Setup(); AddPlayer(7).transform.position = new Vector2(20, 0);
        NavigationRouter.TravelPredicate = (from, to) => Vector2.Distance(from, to) <= 0.25f;
        Vector2? first = null;
        NavigationRouter.MovingGoalUnreachable = point =>
        {
            first ??= point;
            return Vector2.Distance(point, first.Value) < 0.001f;
        };
        MovementAutomation.Start(mode); int resets = NavigationRouter.Resets;
        NavigationRouter.IsMovingRoutePending = true;
        Tick(); Tick(1); Tick(6);
        Require(MovementAutomation.Active && NavigationRouter.Resets == resets && NavigationRouter.CompletedFailedGoals.Count == 0 &&
            NavigationRouter.MovingDestinations.All(point => Vector2.Distance(point, first.Value) < 0.001f),
            "pending route was discarded or counted as a finished unreachable endpoint");
        Near(own.MyPhysics.PhysicalVelocity, Vector2.zero, "pending alternative retained native motion");
        NavigationRouter.IsMovingRoutePending = false; Tick(6.02); Tick(6.2);
        Require(MovementAutomation.Active && NavigationRouter.CompletedFailedGoals.Count == 1 &&
            Vector2.Distance(NavigationRouter.LastTo, first.Value) > 0.1f && own.MyPhysics.PhysicalVelocity.sqrMagnitude > 0,
            "completed failure did not advance to a usable alternative on the next bounded retry");
        ApplyCheckedTargetStep(own);
    });
    Run(mode + " keeps the thirty-second budget across several pending alternative searches", () =>
    {
        var own = Setup(); AddPlayer(7).transform.position = new Vector2(20, 0);
        NavigationRouter.TravelPredicate = (from, to) => Vector2.Distance(from, to) <= 0.25f;
        NavigationRouter.MovingGoalUnreachable = _ => true;
        MovementAutomation.Start(mode);
        for (int stage = 0; stage < 14; stage++)
        {
            NavigationRouter.IsMovingRoutePending = true; Tick(stage * 2.0);
            Require(MovementAutomation.Active, "new pending candidate prematurely discarded its remaining budget");
            NavigationRouter.IsMovingRoutePending = false; Tick(stage * 2.0 + 1.8);
            Require(MovementAutomation.Active, "completed failure stopped before trying the remaining candidates");
        }
        NavigationRouter.IsMovingRoutePending = true; Tick(29.99);
        Require(MovementAutomation.Active && NavigationRouter.CompletedFailedGoals.Count == 14,
            "candidate retries lost their shared pending-search allowance");
        Tick(30);
        Require(!MovementAutomation.Active && !NavigationRouter.IsMovingRoutePending,
            "advancing candidates restarted the global thirty-second no-progress budget");
        Near(own.MyPhysics.PhysicalVelocity, Vector2.zero, "alternative-search deadline retained velocity");
        Exact(own.MyPhysics.PeekSpeed, 1.75f, "alternative-search deadline changed the native speed");
    });
    Run(mode + " resets unreachable candidate history when the selected player moves to new floor", () =>
    {
        Setup(); var target = AddPlayer(7); target.transform.position = new Vector2(20, 0);
        NavigationRouter.TravelPredicate = (from, to) => Vector2.Distance(from, to) <= 0.25f;
        NavigationRouter.MovingGoalUnreachable = _ => true;
        MovementAutomation.Start(mode); Tick();
        var preferredOffset = NavigationRouter.LastTo - target.GetTruePosition();
        Tick(0.2); Require(Vector2.Distance(NavigationRouter.LastTo - target.GetTruePosition(), preferredOffset) > 0.1f,
            "fixture did not advance its unreachable candidate");
        target.transform.position = new Vector2(21.2f, 0); Tick(0.4);
        Near(NavigationRouter.LastTo - target.GetTruePosition(), preferredOffset, "new floor kept the stale candidate index");
        Require(MovementAutomation.Active, "target movement was treated as a terminal failure");
    });
    Run(mode + " resets unreachable candidate history when selecting another player", () =>
    {
        Setup(); var target = AddPlayer(7); target.transform.position = new Vector2(20, 0);
        var next = AddPlayer(8); next.transform.position = new Vector2(30, 0);
        NavigationRouter.TravelPredicate = (from, to) => Vector2.Distance(from, to) <= 0.25f;
        NavigationRouter.MovingGoalUnreachable = _ => true;
        MovementAutomation.Start(mode); Tick();
        var preferredOffset = NavigationRouter.LastTo - target.GetTruePosition();
        Tick(0.2); MovementAutomation.CycleTarget(); Tick(0.4);
        Require(MovementAutomation.SelectedPlayer == next, "selector did not choose the new live actor");
        Near(NavigationRouter.LastTo - next.GetTruePosition(), preferredOffset, "new selected player inherited an old unreachable candidate index");
    });
}

Run("mixed lag alternates normal walking and stationary teleports then automatically returns to walking", () =>
{
    var own = Setup(); var other = AddPlayer(7); BeginLag();
    float ordinaryProgress = 0f; int ordinaryFrames = 0, stationaryFrames = 0, stationarySnaps = 0, returnedToWalking = 0;
    for (int frame = 0; frame < 600; frame++)
    {
        var before = (Vector2)own.transform.position; bool wasStationary = MovementAutomation.LagStationaryPhase;
        int snaps = own.NetTransform.Snaps.Count, writes = own.MyPhysics.MovementWrites;
        WalkLag(1);
        bool stationary = MovementAutomation.LagStationaryPhase;
        Require(own.MyPhysics.MovementWrites == writes + (stationary ? 2 : 1),
            "normal walking was overridden outside the intentional stationary phase");
        Near(own.MyPhysics.PhysicalVelocity, stationary ? Vector2.zero : new Vector2(1.75f, 0),
            "phase did not retain normal velocity or suppress intentional stationary velocity");
        if (wasStationary && own.NetTransform.Snaps.Count > snaps) stationarySnaps++;
        if (wasStationary && !stationary) returnedToWalking++;
        if (own.NetTransform.Snaps.Count == snaps)
        {
            var progress = (Vector2)own.transform.position - before;
            if (stationary)
            {
                Near(progress, Vector2.zero, "stationary phase walked between teleports");
                stationaryFrames++;
            }
            else
            {
                Require(progress.x > 0.03f, "ordinary walking phase stopped between corrections");
                ordinaryProgress += progress.x; ordinaryFrames++;
            }
        }
    }
    Require(ordinaryFrames > 200 && ordinaryProgress > 7f, "mixed mode did not include substantial normal walking");
    Require(stationaryFrames > 200 && stationarySnaps >= 4 && returnedToWalking >= 2,
        "mixed mode omitted stationary teleports or failed to resume walking repeatedly");
    Require(own.NetTransform.Snaps.Any(snap => snap.Position.x < snap.From.x - 0.08f), "no backward teleport happened");
    Require(own.NetTransform.Snaps.Any(snap => snap.Position.x > snap.From.x + 0.08f), "no forward teleport happened");
    var gaps = own.NetTransform.Snaps.Zip(own.NetTransform.Snaps.Skip(1), (left, right) => right.Time - left.Time).ToArray();
    Require(gaps.All(gap => gap >= 0.499 && gap <= 2.04) && gaps.Select(gap => Math.Round(gap, 2)).Distinct().Count() >= 2,
        "corrections became a rapid burst or lost their varied cadence");
    Require(own.MyPhysics.SpeedWrites == 0 && other.MyPhysics.MovementWrites == 0 && other.NetTransform.Snaps.Count == 0,
        "mixed lag edited speed or another player's movement");
});
foreach (float speed in new[] { 1.75f, -1.75f, 20f, -20f })
    foreach (float factor in new[] { 1f, -1f })
        Run($"lag corrections follow actual signed motion at speed {speed} and factor {factor}", () =>
        {
            var own = Setup(speed); own.MyPhysics.SpeedFactor = factor; BeginLag();
            WalkLag(240);
            float sign = Math.Sign(speed * factor);
            Require(own.NetTransform.Snaps.Count >= 4, "signed or fast walking never completed both phases");
            Require((own.NetTransform.Snaps[0].Position.x - own.NetTransform.Snaps[0].From.x) * sign < -0.08f,
                "first correction did not go backward along actual native travel");
            Require(own.NetTransform.Snaps.Any(snap => (snap.Position.x - snap.From.x) * sign > 0.08f),
                "forward teleport opposed actual native travel");
            Require((own.NetTransform.Snaps[2].Position.x - own.NetTransform.Snaps[2].From.x) * sign < -0.08f &&
                (own.NetTransform.Snaps[3].Position.x - own.NetTransform.Snaps[3].From.x) * sign > 0.08f,
                "stationary teleport direction ignored the preceding signed native travel");
            Require(own.NetTransform.Snaps.All(snap => Vector2.Distance(snap.From, snap.Position) <= 1.2501f), "fast walking escaped the distance cap");
            Near(own.MyPhysics.PhysicalVelocity, MovementAutomation.LagStationaryPhase ? Vector2.zero : new Vector2(speed * factor, 0), "lag changed signed native velocity outside its stationary phase");
            Exact(own.MyPhysics.PeekSpeed, speed, "lag changed native speed bits");
            Require(own.MyPhysics.SpeedWrites == 0, "lag wrote a speed boost");
        });
foreach (var setting in new (float Input, float Maximum)[] { (0.01f, 0.25f), (0.25f, 0.25f), (99f, 2f), (float.NaN, 1.25f), (float.PositiveInfinity, 1.25f) })
    Run("lag maximum distance " + setting.Input + " clips actual fast travel instead of disabling corrections", () =>
    {
        var own = Setup(20f); Plugin.lagJumpDistance.Value = setting.Input; BeginLag(); WalkLag(240);
        Require(own.NetTransform.Snaps.Count >= 3, "short setting disabled corrections for fast walking");
        Require(own.NetTransform.Snaps.All(snap => Vector2.Distance(snap.From, snap.Position) <= setting.Maximum + 0.001f),
            "snap exceeded its finite configured distance limit");
    });
foreach (var setting in new (float Input, double Minimum)[] { (0.01f, 0.5), (99f, 2), (float.NaN, 0.75), (float.PositiveInfinity, 0.75) })
    Run("lag correction interval " + setting.Input + " retains bounded cadence", () =>
    {
        var own = Setup(); Plugin.lagInterval.Value = setting.Input; BeginLag(); WalkLag(400);
        Require(own.NetTransform.Snaps.Count >= 3, "bounded interval never produced recurring corrections");
        Require(own.NetTransform.Snaps[0].Time >= setting.Minimum - 0.001, "initial correction escaped the interval bound");
        Require(own.NetTransform.Snaps.Zip(own.NetTransform.Snaps.Skip(1), (a, b) => b.Time - a.Time)
            .All(gap => gap >= setting.Minimum - 0.001 && gap <= 2.04), "cadence escaped its finite bounds");
    });
Run("lag sweeps true coordinates but snaps transform coordinates without stopping walking", () =>
{
    var own = Setup(); own.transform.position = new Vector2(3, 7); own.TruePositionOffset = new Vector2(0.15f, -0.3f);
    BeginLag(new Vector2(0, 1)); WalkLag(200);
    Require(own.NetTransform.Snaps.Count >= 2, "coordinate case had no backward and forward corrections");
    foreach (var snap in own.NetTransform.Snaps)
        Require(NavigationRouter.TravelChecksRecorded.Any(check => check.Clear &&
            Vector2.Distance(check.From, snap.From + own.TruePositionOffset) < 0.0001f &&
            Vector2.Distance(check.To, snap.Position + own.TruePositionOffset) < 0.0001f),
            "snap had no matching full true-coordinate sweep");
    Near(own.MyPhysics.PhysicalVelocity, MovementAutomation.LagStationaryPhase ? Vector2.zero : new Vector2(0, 1.75f), "coordinate conversion changed native walking outside its stationary phase");
});
Run("held input without actual walking cannot manufacture teleports", () =>
{
    var own = Setup(); BeginLag(); WalkLag(400, false);
    Require(own.NetTransform.Snaps.Count == 0 && MovementAutomation.Active, "stationary held input manufactured travel");
    WalkLag(60); Require(own.NetTransform.Snaps.Count > 0, "walking never recovered after a stationary hold");
});
Run("lag respects analog walking strength rather than boosting native velocity", () =>
{
    var own = Setup(); BeginLag(new Vector2(0.5f, 0)); WalkLag(250);
    Require(own.NetTransform.Snaps.Count > 0, "analog movement never produced a correction");
    Near(own.MyPhysics.PhysicalVelocity, MovementAutomation.LagStationaryPhase ? Vector2.zero : new Vector2(0.875f, 0), "lag normalized or boosted analog velocity outside its stationary phase");
});
foreach (var released in new (string Name, Vector2 Input)[]
{
    ("released", Vector2.zero), ("NaN", new Vector2(float.NaN, 0)),
    ("infinite", new Vector2(float.PositiveInfinity, 0)), ("overflowing", new Vector2(float.MaxValue, float.MaxValue)),
})
    Run("lag forgets a recent walking trail after " + released.Name + " input", () =>
    {
        var own = Setup(); BeginLag(); WalkLag(30);
        SetLagInput(released.Input); WalkLag(20);
        Require(own.NetTransform.Snaps.Count == 0 && MovementAutomation.Active, "invalid or released input replayed the old trail");
        SetLagInput(new Vector2(1, 0)); WalkLag(30);
        Require(own.NetTransform.Snaps.Count == 0, "resumed input inherited an old correction deadline");
        WalkLag(20); Require(own.NetTransform.Snaps.Count == 1, "fresh walking did not recover after a full interval");
    });
foreach (var pause in new (string Name, Action Pause, Action Resume)[]
{
    ("menu", () => MenuUI.isGUIActive = true, () => MenuUI.isGUIActive = false),
    ("chat", () => HudManager.Existing.Chat.IsOpenOrOpening = true, () => HudManager.Existing.Chat.IsOpenOrOpening = false),
    ("focus", () => Application.isFocused = false, () => Application.isFocused = true),
    ("meeting", () => Utils.isMeeting = true, () => Utils.isMeeting = false),
    ("exile", () => Utils.isExiling = true, () => Utils.isExiling = false),
    ("intro", () => HudManager.Existing.IsIntroDisplayed = true, () => HudManager.Existing.IsIntroDisplayed = false),
    ("vent", () => PlayerControl.LocalPlayer.inVent = true, () => PlayerControl.LocalPlayer.inVent = false),
    ("ladder", () => PlayerControl.LocalPlayer.onLadder = true, () => PlayerControl.LocalPlayer.onLadder = false),
    ("platform", () => PlayerControl.LocalPlayer.inMovingPlat = true, () => PlayerControl.LocalPlayer.inMovingPlat = false),
    ("movement lock", () => PlayerControl.LocalPlayer.CanMove = false, () => PlayerControl.LocalPlayer.CanMove = true),
})
    Run("lag clears walking history across " + pause.Name + " pauses", () =>
    {
        var own = Setup(); BeginLag(); WalkLag(30); pause.Pause(); WalkLag(20); pause.Resume(); WalkLag(30);
        Require(MovementAutomation.Active && own.NetTransform.Snaps.Count == 0, "pause retained an almost-due correction");
        WalkLag(20); Require(own.NetTransform.Snaps.Count == 1, "normal walking never resumed after pause");
        Require(own.MyPhysics.SpeedWrites == 0, "pause or resume changed native speed");
    });
Run("turning cancels the old trail and new corrections follow the new walking direction", () =>
{
    var own = Setup(); BeginLag(); WalkLag(30); SetLagInput(new Vector2(0, 1)); WalkLag(30);
    Require(own.NetTransform.Snaps.Count == 0, "turn replayed a correction along the previous path");
    WalkLag(80);
    Require(own.NetTransform.Snaps.Count >= 2, "new direction did not build its own correction history");
    Require(own.NetTransform.Snaps.All(snap => Math.Abs(snap.Position.x - snap.From.x) < 0.0001f), "new correction used old horizontal travel");
    Require(own.NetTransform.Snaps[0].Position.y < own.NetTransform.Snaps[0].From.y, "new direction did not first rewind actual travel");
});
Run("external teleports cannot be treated as walked distance or undone by stale history", () =>
{
    var own = Setup(); BeginLag(); WalkLag(30); own.transform.position = new Vector2(30, 40); WalkLag(30);
    Require(own.NetTransform.Snaps.Count == 0, "external teleport inherited an old correction deadline");
    WalkLag(20); Require(own.NetTransform.Snaps.Count == 1 && own.NetTransform.Snaps[0].Position.x > 30,
        "old history pulled the player back across an external teleport");
});
Run("long frame stalls rearm a fresh walking interval without catching up missed corrections", () =>
{
    var own = Setup(); BeginLag(); WalkLag(30); NativeLagTick(100);
    Require(own.NetTransform.Snaps.Count == 0, "stalled clock emitted an old correction");
    WalkLag(30); Require(own.NetTransform.Snaps.Count == 0, "stalled clock kept its old deadline");
    WalkLag(20); Require(own.NetTransform.Snaps.Count == 1, "fresh walking interval failed after stall");
    int count = own.NetTransform.Snaps.Count;
    for (int duplicate = 0; duplicate < 30; duplicate++) NativeLagTick(Time.realtimeSinceStartupAsDouble);
    Require(own.NetTransform.Snaps.Count == count, "same-clock callbacks emitted a correction burst");
});
Run("clock rollback discards the previous walking trail", () =>
{
    var own = Setup(); BeginLag(); WalkLag(30); NativeLagTick(0.1); WalkLag(30);
    Require(own.NetTransform.Snaps.Count == 0, "rollback retained an old correction deadline");
    WalkLag(20); Require(own.NetTransform.Snaps.Count == 1, "fresh walking did not recover after rollback");
});
foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
    Run("invalid lag clock stops before correction: " + invalid, () =>
    {
        var own = Setup(); BeginLag(); WalkLag(30); NativeLagTick(invalid); Tick(10);
        Require(!MovementAutomation.Active && own.NetTransform.Snaps.Count == 0, "nonfinite clock replayed a correction");
    });
foreach (bool forward in new[] { false, true })
    Run("a newly blocked " + (forward ? "forward" : "backward") + " correction waits and preserves ordinary walking", () =>
    {
        var own = Setup(); BeginLag();
        if (forward) WalkUntilLagSnaps(own, 1);
        int snaps = own.NetTransform.Snaps.Count;
        NavigationRouter.TravelPredicate = (from, to) => Vector2.Distance(from, to) < 0.05f;
        int checks = NavigationRouter.TravelChecks;
        for (int step = 0; step < 110 && NavigationRouter.TravelChecks == checks; step++) WalkLag(1);
        Require(own.NetTransform.Snaps.Count == snaps && NavigationRouter.TravelChecks > checks &&
            NavigationRouter.TravelChecks <= checks + 5, "blocked correction crossed geometry or exceeded bounded retries");
        var position = (Vector2)own.transform.position; WalkLag(10);
        Require(own.transform.position.x > position.x + 0.3f, "a blocked correction froze ordinary walking");
        Require(NavigationRouter.TravelChecks <= checks + 5, "blocked correction retried on every frame");
        NavigationRouter.TravelPredicate = null; WalkLag(110);
        Require(own.NetTransform.Snaps.Count > snaps, "clear floor never recovered after the bounded correction interval");
    });
Run("forward teleport cannot cross a thin wall even with a clear far endpoint", () =>
{
    var own = Setup(); BeginLag(); WalkUntilLagSnaps(own, 1);
    while (Time.realtimeSinceStartupAsDouble < 1.7) WalkLag(1);
    float wall = own.GetTruePosition().x + 0.12f;
    FixtureWall(wall, -1, wall + 0.08f, 1);
    int count = own.NetTransform.Snaps.Count, checks = NavigationRouter.TravelChecks; WalkLag(12);
    Require(NavigationRouter.TravelChecks > checks && NavigationRouter.TravelChecksRecorded
        .Skip(checks).Any(check => !check.Clear && check.To.x > wall + 0.08f),
        "fixture never exercised a forward correction across the thin wall");
    Require(own.GetTruePosition().x <= wall + 0.0001f, "native walking fixture or teleport crossed the wall");
    Require(own.NetTransform.Snaps.Skip(count).All(snap => snap.Position.x <= wall + 0.0001f), "forward correction ignored the full thin-wall sweep");
});
Run("foreign physics and a forged owner flag cannot execute a due lag correction", () =>
{
    var own = Setup(); var other = AddPlayer(7); BeginLag(); WalkLag(35);
    int ownWrites = own.MyPhysics.MovementWrites; Time.realtimeSinceStartupAsDouble = 0.8;
    MovementAutomation.FixedTick(other.MyPhysics); other.MyPhysics.AmOwner = true; MovementAutomation.FixedTick(other.MyPhysics);
    Require(own.NetTransform.Snaps.Count == 0 && other.NetTransform.Snaps.Count == 0 && own.MyPhysics.MovementWrites == ownWrites,
        "foreign callback executed an own or remote correction");
});
foreach (var end in new (string Name, Action End)[]
{
    ("F8 Stop action", () => MovementAutomation.Stop()),
    ("panic", () => Plugin.isPanicked = true),
    ("death", () => PlayerControl.LocalPlayer.Data.IsDead = true),
    ("disconnect", () => PlayerControl.LocalPlayer.Data.Disconnected = true),
    ("new ship", () => ShipStatus.Instance = new()),
    ("leaving client", () => Utils.isClient = false),
})
    Run("lag corrections stop on " + end.Name, () =>
    {
        var own = Setup(); BeginLag(); WalkLag(35); end.End(); Tick(0.8); Tick(100);
        Require(!MovementAutomation.Active && own.NetTransform.Snaps.Count == 0, "terminal context retained a pending correction");
        Require(own.MyPhysics.SpeedWrites == 0, "terminal cleanup edited base speed");
    });
Run("newly owned player cannot inherit another player's lag history", () =>
{
    var own = Setup(); BeginLag(); WalkLag(35);
    var next = new PlayerControl(0, true); PlayerControl.LocalPlayer = next; PlayerControl.AllPlayerControls = [next]; Tick(0.8);
    Require(!MovementAutomation.Active && own.NetTransform.Snaps.Count == 0 && next.NetTransform.Snaps.Count == 0 && next.MyPhysics.MovementWrites == 0,
        "replacement player inherited old corrections or cleanup");
});
foreach (bool destroyed in new[] { false, true })
    Run("lag stops safely for " + (destroyed ? "destroyed" : "missing") + " network transform", () =>
    {
        var own = Setup(); BeginLag(); WalkLag(35); var transform = own.NetTransform;
        if (destroyed) transform.Destroyed = true; else own.NetTransform = null;
        Tick(0.8); Require(!MovementAutomation.Active && transform.Snaps.Count == 0, "invalid transform received a correction");
    });
Run("mode changes discard lag history and keep other mode behavior separate", () =>
{
    var own = Setup(); BeginLag(); WalkLag(35); MovementAutomation.Start(MovementMode.ZigzagDash); Tick(0.8);
    MovementAutomation.Start(MovementMode.LagWalk); NativeLagTick(1); WalkLag(30);
    Require(own.NetTransform.Snaps.Count == 0, "mode switching replayed old lag history");
    WalkLag(20); Require(own.NetTransform.Snaps.Count == 1, "new lag session never observed fresh walking");
    Exact(own.MyPhysics.PeekSpeed, 1.75f, "switching retained dash boost in lag mode");
});
Run("lag collision and corrections preserve manual task and noclip preferences", () =>
{
    var own = Setup(); CheatToggles.automaticTasks = true; CheatToggles.noClip = true; own.Collider.enabled = false;
    BeginLag(); WalkLag(100);
    Require(MovementAutomation.NeedsGroundCollision && own.Collider.enabled && CheatToggles.noClip && CheatToggles.automaticTasks,
        "lag dropped collision checking or changed unrelated manual toggles");
    MovementAutomation.Stop(); Require(!MovementAutomation.NeedsGroundCollision && CheatToggles.noClip && CheatToggles.automaticTasks,
        "lag stop failed to release its collision override or changed unrelated toggles");
});

foreach (float interval in new[] { 0.5f, 0.75f, 2f })
    Run("stationary lag phase at interval " + interval + " has two spaced teleports and resumes native walking", () =>
    {
        var own = Setup(); Plugin.lagInterval.Value = interval; BeginLag(); WalkUntilLagSnaps(own, 2);
        Require(MovementAutomation.LagStationaryPhase, "two walking corrections never entered a stationary phase");
        double began = Time.realtimeSinceStartupAsDouble; int initialSnaps = own.NetTransform.Snaps.Count;
        for (int frame = 0; frame < 210 && MovementAutomation.LagStationaryPhase; frame++)
        {
            var point = (Vector2)own.transform.position; int snaps = own.NetTransform.Snaps.Count; WalkLag(1);
            if (MovementAutomation.LagStationaryPhase && snaps == own.NetTransform.Snaps.Count)
                Near(own.transform.position, point, "stationary phase leaked ordinary walking");
        }
        Require(!MovementAutomation.LagStationaryPhase && own.NetTransform.Snaps.Count == initialSnaps + 2,
            "stationary phase did not complete exactly two teleports");
        Require(Time.realtimeSinceStartupAsDouble - began <= 4.1, "stationary phase exceeded its absolute bound");
        var first = own.NetTransform.Snaps[initialSnaps]; var second = own.NetTransform.Snaps[initialSnaps + 1];
        Require(first.Time - began >= 0.499 && second.Time - first.Time >= 0.499,
            "stationary teleports bypassed the minimum interval");
        Require(first.Position.x < first.From.x && second.Position.x > second.From.x,
            "stationary phase failed to include backward and forward teleports");
        Near(own.MyPhysics.PhysicalVelocity, new Vector2(1.75f, 0), "final stationary tick failed to resume native velocity immediately");
        var resumed = (Vector2)own.transform.position; WalkLag(10);
        Require(own.transform.position.x > resumed.x + 0.3f, "phase completion did not resume integrated walking");
    });
Run("stationary lag absolute deadline ends a burst before a late second teleport", () =>
{
    var own = Setup(); Plugin.lagInterval.Value = 2f; BeginLag(); WalkUntilLagSnaps(own, 2);
    double began = Time.realtimeSinceStartupAsDouble;
    for (int frame = 0; frame < 10 && MovementAutomation.LagStationaryPhase; frame++)
        NativeLagTick(Time.realtimeSinceStartupAsDouble + 0.49);
    Require(!MovementAutomation.LagStationaryPhase && own.NetTransform.Snaps.Count == 3,
        "absolute deadline waited for or replayed the late second stationary teleport");
    Require(Time.realtimeSinceStartupAsDouble - began < 4.6,
        "stationary deadline was extended by delayed callbacks");
    Near(own.MyPhysics.PhysicalVelocity, new Vector2(1.75f, 0), "absolute timeout left walking frozen");
    WalkLag(30); Require(own.NetTransform.Snaps.Count == 3, "timed-out burst retained a queued correction");
});
foreach (bool blockFirst in new[] { false, true })
    Run("blocked stationary " + (blockFirst ? "both teleports" : "second teleport") + " cannot prolong the freeze", () =>
    {
        var own = ArmStationaryLag();
        if (!blockFirst) WalkUntilLagSnaps(own, 3);
        int snaps = own.NetTransform.Snaps.Count, checks = NavigationRouter.TravelChecks;
        double started = Time.realtimeSinceStartupAsDouble;
        NavigationRouter.TravelPredicate = (_, _) => false;
        for (int frame = 0; frame < 210 && MovementAutomation.LagStationaryPhase; frame++) WalkLag(1);
        Require(!MovementAutomation.LagStationaryPhase && own.NetTransform.Snaps.Count == snaps,
            "blocked stationary correction crossed geometry or retained the phase");
        Require(Time.realtimeSinceStartupAsDouble - started <= 4.1 && NavigationRouter.TravelChecks <= checks + 10,
            "blocked phase was unbounded or flooded collision retries");
        Near(own.MyPhysics.PhysicalVelocity, new Vector2(1.75f, 0), "blocked phase failed to restore native walking");
        NavigationRouter.TravelPredicate = null; var point = (Vector2)own.transform.position; WalkLag(10);
        Require(own.transform.position.x > point.x + 0.3f, "walking did not recover from blocked stationary corrections");
    });
foreach (bool afterFirst in new[] { false, true })
{
    Run("release cancels stationary lag " + (afterFirst ? "after" : "before") + " its first teleport", () =>
    {
        var own = ArmStationaryLag(); if (afterFirst) WalkUntilLagSnaps(own, 3);
        int snaps = own.NetTransform.Snaps.Count;
        SetLagInput(Vector2.zero); WalkLag(1);
        Require(!MovementAutomation.LagStationaryPhase && own.NetTransform.Snaps.Count == snaps, "release retained stationary lag");
        SetLagInput(new Vector2(1, 0)); WalkLag(30);
        Require(own.NetTransform.Snaps.Count == snaps && !MovementAutomation.LagStationaryPhase, "fresh hold replayed a stationary teleport");
        WalkLag(20); Require(own.NetTransform.Snaps.Count == snaps + 1, "fresh hold failed to rebuild ordinary walking history");
    });
    Run("turn cancels stationary lag " + (afterFirst ? "after" : "before") + " its first teleport", () =>
    {
        var own = ArmStationaryLag(); if (afterFirst) WalkUntilLagSnaps(own, 3);
        int snaps = own.NetTransform.Snaps.Count; var point = (Vector2)own.transform.position;
        SetLagInput(new Vector2(0, 1)); WalkLag(1);
        Require(!MovementAutomation.LagStationaryPhase && own.NetTransform.Snaps.Count == snaps,
            "turn retained an old stationary teleport");
        Require(own.transform.position.y > point.y, "turn failed to resume normal steering immediately");
        WalkLag(30); Require(own.NetTransform.Snaps.Count == snaps, "turned input inherited the old stationary deadline");
    });
}
foreach (var change in new (string Name, Action Apply)[]
{
    ("external teleport", () => PlayerControl.LocalPlayer.transform.position = new Vector2(30, 40)),
    ("long frame stall", () => Time.realtimeSinceStartupAsDouble += 10),
    ("clock rollback", () => Time.realtimeSinceStartupAsDouble = 0.1),
})
    Run("stationary lag is canceled by " + change.Name, () =>
    {
        var own = ArmStationaryLag(); int snaps = own.NetTransform.Snaps.Count;
        change.Apply(); WalkLag(1);
        Require(!MovementAutomation.LagStationaryPhase && own.NetTransform.Snaps.Count == snaps,
            "invalidated stationary history replayed a snap");
        Near(own.MyPhysics.PhysicalVelocity, new Vector2(1.75f, 0), "invalidated stationary history kept velocity frozen");
        WalkLag(30); Require(own.NetTransform.Snaps.Count == snaps, "stationary history survived its cancellation");
    });
foreach (var pause in new (string Name, Action Pause, Action Resume)[]
{
    ("menu", () => MenuUI.isGUIActive = true, () => MenuUI.isGUIActive = false),
    ("meeting", () => Utils.isMeeting = true, () => Utils.isMeeting = false),
    ("vent", () => PlayerControl.LocalPlayer.inVent = true, () => PlayerControl.LocalPlayer.inVent = false),
})
    Run("stationary lag cannot survive a " + pause.Name + " pause", () =>
    {
        var own = ArmStationaryLag(); int snaps = own.NetTransform.Snaps.Count;
        pause.Pause(); WalkLag(1); Require(!MovementAutomation.LagStationaryPhase, "pause retained stationary phase");
        pause.Resume(); WalkLag(30);
        Require(own.NetTransform.Snaps.Count == snaps && own.MyPhysics.PhysicalVelocity.x > 0,
            "pause replayed the stationary correction or failed to resume normal walking");
    });
foreach (var end in new (string Name, Action End)[]
{
    ("F8 Stop action", () => MovementAutomation.Stop()),
    ("panic", () => Plugin.isPanicked = true),
    ("death", () => PlayerControl.LocalPlayer.Data.IsDead = true),
    ("disconnect", () => PlayerControl.LocalPlayer.Data.Disconnected = true),
    ("new round ship", () => ShipStatus.Instance = new()),
})
    Run("stationary lag cancels on " + end.Name, () =>
    {
        var own = ArmStationaryLag(); int snaps = own.NetTransform.Snaps.Count;
        end.End(); Tick(Time.realtimeSinceStartupAsDouble + 0.02); Tick(100);
        Require(!MovementAutomation.Active && !MovementAutomation.LagStationaryPhase && own.NetTransform.Snaps.Count == snaps,
            "stopped context retained a stationary teleport");
    });

Console.WriteLine($"{total - failed}/{total} tests passed; actual MovementAutomation source linked, no game/network libraries loaded.");
return failed == 0 ? 0 : 1;

PlayerControl Setup(float speed = 1.75f)
{
    MovementAutomation.Reset();
    Utils.isClient = true; Utils.isInGame = true; Utils.isLobby = false; Utils.isFreePlay = false;
    Utils.isMeeting = false; Utils.isExiling = false; MenuUI.isGUIActive = false;
    Plugin.isPanicked = false; Plugin.stuntMultiplier.Value = 2; Plugin.targetMovementMultiplier.Value = 3; Plugin.yoyoInterval.Value = 1;
    Plugin.lagInterval.Value = 0.75f; Plugin.lagJumpDistance.Value = 1.25f;
    Application.isFocused = true; Time.realtimeSinceStartupAsDouble = 0; Time.fixedDeltaTime = 0.02f;
    var own = new PlayerControl(0, true, speed); PlayerControl.LocalPlayer = own; PlayerControl.AllPlayerControls = [own];
    ShipStatus.Instance = new(); HudManager.Existing = new(); SprintHandler.Resets = 0; SprintHandler.OnReset = null;
    NavigationRouter.FloorClear = true; NavigationRouter.Blocked = false; NavigationRouter.ForcedDirection = null;
    NavigationRouter.StandPredicate = null; NavigationRouter.TravelPredicate = null;
    NavigationRouter.TravelChecksRecorded.Clear(); NavigationRouter.SteeringDistance = float.PositiveInfinity;
    NavigationRouter.MovingGoalUnreachable = null; NavigationRouter.CompletedFailedGoals.Clear();
    NavigationRouter.MovingDestinations.Clear(); NavigationRouter.BlockedMovingGoals = 0;
    NavigationRouter.StandChecks = NavigationRouter.TravelChecks = 0;
    NavigationRouter.IsMovingRoutePending = false;
    NavigationRouter.MovingRouteFailed = false;
    NavigationRouter.IsRecovering = false;
    NavigationRouter.Calls = NavigationRouter.Resets = NavigationRouter.MovingCalls = 0;
    NavigationRouter.RecoveryCalls = 0; NavigationRouter.RecoveryDirection = Vector2.zero;
    AiTasksHandler.Available = true; AiTasksHandler.IsFinished = false; AiTasksHandler.NativeActionAllowed = true;
    AiTasksHandler.Goal = new Vector2(8, 0); AiTasksHandler.ArrivalDistance = 0.12f;
    AiTasksHandler.Resets = AiTasksHandler.Arrivals = AiTasksHandler.NativeSteps = AiTasksHandler.Skips = 0;
    AiTasksHandler.AlternateRequests = 0; AiTasksHandler.HasAlternate = false;
    AiTasksHandler.RequireGroundBody = false;
    CheatToggles.automaticTasks = false; CheatToggles.noClip = false;
    return own;
}
PlayerControl AddPlayer(byte id)
{
    var player = new PlayerControl(id, false); PlayerControl.AllPlayerControls.Add(player); return player;
}
void SaveTwoSpots()
{
    PlayerControl.LocalPlayer.transform.position = Vector2.zero; MovementAutomation.SavePointA();
    PlayerControl.LocalPlayer.transform.position = new Vector2(4, 0); MovementAutomation.SavePointB();
}
void Tick(double at = 0)
{
    Time.realtimeSinceStartupAsDouble = at; MovementAutomation.FixedTick(PlayerControl.LocalPlayer.MyPhysics);
}
void SetLagInput(Vector2 input) => HudManager.Existing.joystick = new FixtureJoystick { Value = input };
PlayerControl ArmStationaryLag()
{
    var own = Setup(); BeginLag(); WalkUntilLagSnaps(own, 2);
    Require(MovementAutomation.LagStationaryPhase, "fixture failed to reach stationary lag");
    return own;
}
void BeginLag(Vector2? input = null)
{
    SetLagInput(input ?? new Vector2(1, 0)); MovementAutomation.Start(MovementMode.LagWalk); NativeLagTick(0);
}
void NativeLagTick(double at)
{
    // Native FixedUpdate supplies the ordinary velocity before the linked
    // postfix. Lag is required to leave this velocity unchanged while walking.
    var local = PlayerControl.LocalPlayer;
    var input = HudManager.Existing.joystick?.DeltaL ?? Vector2.zero;
    bool valid = float.IsFinite(input.x) && float.IsFinite(input.y) && float.IsFinite(input.sqrMagnitude);
    var nativeInput = !valid ? Vector2.zero : input.sqrMagnitude > 1f ? input.normalized : input;
    local.MyPhysics.SetNormalizedVelocity(nativeInput);
    Tick(at);
}
void WalkLag(int frames, bool integrate = true)
{
    for (int frame = 0; frame < frames; frame++)
    {
        NativeLagTick(Time.realtimeSinceStartupAsDouble + 0.02d);
        var own = PlayerControl.LocalPlayer;
        var step = own.MyPhysics.PhysicalVelocity * Time.fixedDeltaTime;
        // Integrate real ordinary movement between corrections. The fixture
        // applies its synthetic geometry without recording a mod sweep.
        if (integrate && (NavigationRouter.TravelPredicate == null ||
            NavigationRouter.TravelPredicate(own.GetTruePosition(), own.GetTruePosition() + step)))
            own.transform.position = (Vector2)own.transform.position + step;
    }
}
void WalkUntilLagSnaps(PlayerControl own, int count)
{
    for (int frame = 0; frame < 300 && own.NetTransform.Snaps.Count < count; frame++) WalkLag(1);
    Require(own.NetTransform.Snaps.Count == count, "walking never reached the requested correction count");
}
void ApplyCheckedTargetStep(PlayerControl own)
{
    var from = own.GetTruePosition();
    var step = own.MyPhysics.PhysicalVelocity * Time.fixedDeltaTime;
    Require(float.IsFinite(step.x) && float.IsFinite(step.y) && step.magnitude <= 0.24001f &&
        own.MyPhysics.PhysicalVelocity.magnitude <= 12.0001f, "target step exceeded finite displacement or speed bounds");
    if (step.sqrMagnitude > 0)
    {
        var to = from + step;
        Require(NavigationRouter.TravelChecksRecorded.Any(check => check.Clear && Vector2.Distance(check.From, from) < 0.0001f &&
            Vector2.Distance(check.To, to) < 0.0001f), "actual target displacement has no matching clear sweep");
        Require(NavigationRouter.CanTravel(from, to), "actual target displacement crosses blocked fixture geometry");
        own.transform.position = (Vector2)own.transform.position + step;
    }
}
void FixtureWall(float minX, float minY, float maxX, float maxY)
{
    bool Clear(Vector2 point) => point.x <= minX || point.x >= maxX || point.y <= minY || point.y >= maxY;
    NavigationRouter.StandPredicate = Clear;
    NavigationRouter.TravelPredicate = (from, to) => FixtureSegmentClear(from, to, Clear);
}
bool FixtureSegmentClear(Vector2 from, Vector2 to, Func<Vector2, bool> clear)
{
    // Synthetic clearance only: endpoints and regularly sampled ground between
    // them exercise controller selection, not native Unity sweep semantics.
    for (int sample = 0; sample <= 128; sample++)
        if (!clear(from + (to - from) * (sample / 128f))) return false;
    return true;
}
void Run(string name, Action action)
{
    total++;
    try { action(); Console.WriteLine("PASS " + name); }
    catch (Exception error) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + error.Message); }
}
void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
void Equal(float actual, float expected, string reason) => Require(actual == expected, reason + $" ({actual} != {expected})");
void Exact(float actual, float expected, string reason) => Require(BitConverter.SingleToInt32Bits(actual) == BitConverter.SingleToInt32Bits(expected), reason);
void Near(Vector2 actual, Vector2 expected, string reason) => Require(Vector2.Distance(actual, expected) < 0.0001f, reason + $" ({actual} != {expected})");
