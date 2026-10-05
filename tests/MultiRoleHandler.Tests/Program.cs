using AmongUs.GameOptions;
using MalumMenu;
using UnityEngine;
using Plugin = MalumMenu.MalumMenu;

int total = 0, failures = 0;

Run("guest can enable bundle without rewriting any assigned role", () =>
{
    SetUp();
    var own = PlayerControl.LocalPlayer;
    var assigned = own.Data.Role;
    var other = AddPlayer(2);
    var otherAssigned = other.Data.Role;
    MultiRoleHandler.SetEnabled(true);
    MultiRoleHandler.Tick();
    Require(!Utils.isHost && MultiRoleHandler.Active && MultiRoleHandler.VentAccess, "guest needs host or has no active vent access");
    MultiRoleHandler.CycleTarget();
    MultiRoleHandler.OpenTrackingMap();
    MultiRoleHandler.OpenVitals();
    MultiRoleHandler.SetEnabled(false);
    Require(ReferenceEquals(own.Data.Role, assigned) && own.Data.RoleAssignments == 0, "local assigned role changed");
    Require(ReferenceEquals(other.Data.Role, otherAssigned) && other.Data.RoleAssignments == 0, "another player's assigned role changed");
    Require(assigned.Role == RoleTypes.Crewmate && otherAssigned.Role == RoleTypes.Crewmate, "native role type changed");
});

foreach (var guard in new (string Name, Action Change)[]
{
    ("off", () => MultiRoleHandler.SetEnabled(false)),
    ("panic", () => Plugin.isPanicked = true),
    ("outside round", () => Utils.isInGame = false),
    ("non-Normal mode", () => Utils.isNormalGame = false),
    ("missing ship", () => ShipStatus.Instance = null),
    ("destroyed ship", () => ShipStatus.Instance.Destroyed = true),
    ("missing local actor", () => PlayerControl.LocalPlayer = null),
    ("destroyed local actor", () => PlayerControl.LocalPlayer.Destroyed = true),
    ("foreign owner", () => PlayerControl.LocalPlayer.AmOwner = false),
    ("missing player data", () => PlayerControl.LocalPlayer.Data = null),
    ("missing assigned role", () => PlayerControl.LocalPlayer.Data.Role = null),
    ("destroyed assigned role", () => PlayerControl.LocalPlayer.Data.Role.Destroyed = true),
    ("dead local player", () => PlayerControl.LocalPlayer.Data.IsDead = true),
    ("disconnected local player", () => PlayerControl.LocalPlayer.Data.Disconnected = true),
    ("missing options manager", () => GameOptionsManager.Instance = null),
    ("missing options", () => GameOptionsManager.Instance.CurrentGameOptions = null)
})
    Run(guard.Name + " disables ability access and clears live state", () =>
    {
        SetUp();
        AddPlayer(2);
        Enable();
        MultiRoleHandler.CycleTarget();
        MultiRoleHandler.OpenVitals();
        var owned = VitalsMinigame.Instances.Single();
        guard.Change();
        MultiRoleHandler.Tick();
        Require(!MultiRoleHandler.Active && !MultiRoleHandler.VentAccess && !MultiRoleHandler.HasTarget, "ineligible actor retained abilities or target");
        Require(owned.CloseCalls == 1, "ineligible actor retained owned vitals");
        MultiRoleHandler.OpenVitals();
        MultiRoleHandler.OpenTrackingMap();
        MultiRoleHandler.OpenDetectiveNotes();
        MultiRoleHandler.InterrogateTarget();
        Require(!MultiRoleHandler.CanOpenTool && !MultiRoleHandler.DetectiveAvailable && !MultiRoleHandler.CanInterrogate, "ineligible actor retained detective access");
        Require(VitalsMinigame.Instances.Count == 1 && HudManager.Instance.MapCalls.Count == 0, "ineligible context opened an ability");
    });

Run("Freeplay works with round and host flags false", () =>
{
    SetUp();
    Utils.isInGame = false;
    Utils.isFreePlay = true;
    Enable();
    Require(MultiRoleHandler.Active && MultiRoleHandler.VentAccess, "Freeplay depends on hosting or online-round flag");
});

Run("target cycle sorts valid living players and excludes owner", () =>
{
    SetUp();
    var later = AddPlayer(9);
    var first = AddPlayer(2);
    AddPlayer(1).Data.IsDead = true;
    AddPlayer(0).Data.Disconnected = true;
    AddPlayer(3).Destroyed = true;
    AddPlayer(4).Data = null;
    PlayerControl.AllPlayerControls.Add(null);
    Enable();
    MultiRoleHandler.CycleTarget();
    Require(MultiRoleHandler.IsTrackedPlayer(first) && !MultiRoleHandler.IsTrackedPlayer(PlayerControl.LocalPlayer), "cycle selected stale, dead, missing-data or owner actor");
    MultiRoleHandler.CycleTarget();
    Require(MultiRoleHandler.IsTrackedPlayer(later), "cycle does not use stable player order");
    MultiRoleHandler.CycleTarget();
    Require(MultiRoleHandler.IsTrackedPlayer(first), "cycle does not wrap over valid actors");
});

Run("empty target list never opens a tracking map", () =>
{
    SetUp();
    AddPlayer(2).Data.IsDead = true;
    Enable();
    MultiRoleHandler.CycleTarget();
    MultiRoleHandler.OpenTrackingMap();
    Require(!MultiRoleHandler.HasTarget && HudManager.Instance.MapCalls.Count == 0, "empty list produced a target or map");
});

foreach (var change in new (string Name, Action<PlayerControl> Change)[]
{
    ("death", player => player.Data.IsDead = true),
    ("disconnect", player => player.Data.Disconnected = true),
    ("destroy", player => player.Destroyed = true),
    ("missing data", player => player.Data = null)
})
    Run("tracked actor " + change.Name + " invalidates map and selection", () =>
    {
        SetUp();
        var target = AddPlayer(2);
        Enable();
        MultiRoleHandler.CycleTarget();
        change.Change(target);
        MultiRoleHandler.Tick();
        Require(!MultiRoleHandler.HasTarget && !MultiRoleHandler.IsTrackedPlayer(target), "stale actor is still tracked");
        MultiRoleHandler.OpenTrackingMap();
        Require(HudManager.Instance.MapCalls.Count == 0, "stale actor opened tracking map");
    });

foreach (bool replaceShip in new[] { false, true })
    Run((replaceShip ? "ship" : "owned player") + " replacement resets target and owned vitals", () =>
    {
        SetUp();
        AddPlayer(2);
        Enable();
        MultiRoleHandler.CycleTarget();
        MultiRoleHandler.OpenVitals();
        var owned = VitalsMinigame.Instances.Single();
        if (replaceShip) ShipStatus.Instance = new();
        else PlayerControl.LocalPlayer = MakePlayer(0, true);
        MultiRoleHandler.Tick();
        Require(MultiRoleHandler.Active && !MultiRoleHandler.HasTarget, "new session retained old target");
        Require(owned.CloseCalls == 1, "new session retained old vitals");
    });

Run("tracking recomputes current position and room at its update interval", () =>
{
    SetUp();
    var target = AddPlayer(2);
    target.Position = new(3, 4);
    Enable();
    MultiRoleHandler.CycleTarget();
    Require(MultiRoleHandler.TrackingText.Contains("5.0m, up", StringComparison.Ordinal), "first reading did not use current distance and direction");
    MultiRoleHandler.Tick(); // Arm interval after initial selection.
    target.Position = new(-8, 0);
    Utils.CurrentRoom = new() { RoomId = "Electrical" };
    Time.unscaledTime = 0.1f;
    MultiRoleHandler.Tick();
    Require(MultiRoleHandler.TrackingText.Contains("5.0m, up", StringComparison.Ordinal), "tracking ignored update interval");
    Time.unscaledTime = 0.2f;
    MultiRoleHandler.Tick();
    Require(MultiRoleHandler.TrackingText.Contains("Electrical", StringComparison.Ordinal) && MultiRoleHandler.TrackingText.Contains("8.0m, left", StringComparison.Ordinal), "tracking kept stale room or position");
    MultiRoleHandler.OpenTrackingMap();
    Require(HudManager.Instance.MapCalls.Count == 1 && HudManager.Instance.MapCalls[0].Mode == MapOptions.Modes.Normal, "tracking used wrong map mode");
});

Run("disable closes its own vitals once", () =>
{
    SetUp(); Enable();
    MultiRoleHandler.OpenVitals();
    MultiRoleHandler.OpenVitals();
    var owned = VitalsMinigame.Instances.Single();
    Require(owned.BeginCalls == 1 && owned.LastBeginPlayer is null && ReferenceEquals(Minigame.Instance, owned), "vitals did not begin as an owned local instance");
    Require(ReferenceEquals(owned.transform.parent, Camera.main.transform) && owned.transform.localPosition.z == -50, "vitals attached to wrong camera or depth");
    Require(!owned.BatteryText.gameObject.activeSelf, "local vitals retained Scientist battery indicator");
    MultiRoleHandler.SetEnabled(false);
    MultiRoleHandler.Tick();
    Require(owned.CloseCalls == 1 && Minigame.Instance is null, "disable did not close its owned instance exactly once");
});

Run("disable with unrelated minigame leaves it untouched", () =>
{
    SetUp(); Enable();
    var unrelated = new Minigame();
    Minigame.Instance = unrelated;
    MultiRoleHandler.SetEnabled(false);
    Require(unrelated.CloseCalls == 0 && !unrelated.Destroyed && ReferenceEquals(Minigame.Instance, unrelated), "cleanup closed unrelated minigame");
});

Run("disable retains unrelated replacement while closing only owned vitals", () =>
{
    SetUp(); Enable();
    MultiRoleHandler.OpenVitals();
    var owned = VitalsMinigame.Instances.Single();
    var unrelated = new Minigame();
    Minigame.Instance = unrelated;
    MultiRoleHandler.SetEnabled(false);
    Require(owned.CloseCalls == 1 && unrelated.CloseCalls == 0 && !unrelated.Destroyed && ReferenceEquals(Minigame.Instance, unrelated), "cleanup redirected to current unrelated minigame");
});

foreach (bool exiling in new[] { false, true })
    Run((exiling ? "exile" : "meeting") + " closes owned vitals before further ability use", () =>
    {
        SetUp(); AddPlayer(2); Enable();
        MultiRoleHandler.CycleTarget();
        MultiRoleHandler.OpenVitals();
        var owned = VitalsMinigame.Instances.Single();
        if (exiling) Utils.isExiling = true; else Utils.isMeeting = true;
        MultiRoleHandler.Tick();
        MultiRoleHandler.OpenVitals();
        MultiRoleHandler.OpenTrackingMap();
        Require(owned.CloseCalls == 1 && VitalsMinigame.Instances.Count == 1 && HudManager.Instance.MapCalls.Count == 0, "meeting/exile retained or reopened owned ability");
        Require(!MultiRoleHandler.VentAccess, "meeting/exile still permits vent button");
    });

foreach (var guard in new (string Name, Action Change)[]
{
    ("vent", () => PlayerControl.LocalPlayer.inVent = true),
    ("ladder", () => PlayerControl.LocalPlayer.onLadder = true),
    ("moving platform", () => PlayerControl.LocalPlayer.inMovingPlat = true),
    ("meeting", () => Utils.isMeeting = true),
    ("exile", () => Utils.isExiling = true),
    ("death", () => PlayerControl.LocalPlayer.Data.IsDead = true),
    ("intro", () => HudManager.Instance.IsIntroDisplayed = true),
    ("open chat", () => HudManager.Instance.Chat.IsOpenOrOpening = true),
    ("cannot move", () => PlayerControl.LocalPlayer.CanMove = false),
    ("missing HUD", () => HudManager.Instance = null),
    ("destroyed HUD", () => HudManager.Instance.Destroyed = true),
    ("other minigame", () => Minigame.Instance = new())
})
    Run(guard.Name + " refuses track cycling, map and vitals open", () =>
    {
        SetUp();
        var first = AddPlayer(2);
        AddPlayer(3);
        Enable();
        MultiRoleHandler.CycleTarget();
        var hud = HudManager.Instance;
        guard.Change();
        MultiRoleHandler.CycleTarget();
        MultiRoleHandler.OpenTrackingMap();
        MultiRoleHandler.OpenVitals();
        MultiRoleHandler.OpenDetectiveNotes();
        MultiRoleHandler.InterrogateTarget();
        Require(!MultiRoleHandler.CanOpenTool && !MultiRoleHandler.DetectiveAvailable && !MultiRoleHandler.CanInterrogate, "blocked context retained detective availability");
        Require(VitalsMinigame.Instances.Count == 0 && hud.MapCalls.Count == 0, "blocked context opened vitals or map");
        if (MultiRoleHandler.Active) Require(MultiRoleHandler.IsTrackedPlayer(first), "blocked context advanced target");
    });

foreach (var missing in new (string Name, Action Change)[]
{
    ("role manager", () => RoleManager.Instance = null),
    ("Scientist role", () => RoleManager.Instance.AllRoles.Clear()),
    ("Scientist prefab", () => ((ScientistRole)RoleManager.Instance.AllRoles[0]).VitalsPrefab = null),
    ("Scientist wrong native type", () => RoleManager.Instance.AllRoles = [new RoleBehaviour { Role = RoleTypes.Scientist }]),
    ("camera", () => Camera.main = null)
})
    Run("missing " + missing.Name + " is contained without role assignment", () =>
    {
        SetUp(); Enable();
        missing.Change();
        MultiRoleHandler.OpenVitals();
        Require(VitalsMinigame.Instances.Count == 0 && MultiRoleHandler.Active, "missing prerequisite created vitals or disabled role bundle");
        Require(Plugin.Log.Warnings.Count == 1 && PlayerControl.LocalPlayer.Data.RoleAssignments == 0, "missing prerequisite was hidden or role changed");
    }, allowWarnings: true);

Run("failed instantiate does not leak a minigame", () =>
{
    SetUp(); Enable(); UnityEngine.Object.ThrowOnInstantiate = true;
    MultiRoleHandler.OpenVitals();
    Require(VitalsMinigame.Instances.Count == 0 && Minigame.Instance is null && MultiRoleHandler.Active, "instantiate failure leaked minigame or disabled bundle");
    Require(Plugin.Log.Warnings.Count == 1, "instantiate failure was not reported");
}, allowWarnings: true);

Run("failed Begin closes exactly the partially opened owned instance", () =>
{
    SetUp(); Enable();
    ((ScientistRole)RoleManager.Instance.AllRoles[0]).VitalsPrefab.ThrowOnBegin = true;
    MultiRoleHandler.OpenVitals();
    var owned = VitalsMinigame.Instances.Single();
    Require(owned.CloseCalls == 1 && Minigame.Instance is null && MultiRoleHandler.Active, "Begin failure leaked owned vitals or disabled unrelated tools");
    Require(Plugin.Log.Warnings.Count == 1, "Begin failure was not reported");
}, allowWarnings: true);

Run("ForceClose failure destroys owned game object and leaves replacement untouched", () =>
{
    SetUp(); Enable();
    ((ScientistRole)RoleManager.Instance.AllRoles[0]).VitalsPrefab.ThrowOnForceClose = true;
    MultiRoleHandler.OpenVitals();
    var owned = VitalsMinigame.Instances.Single();
    var unrelated = new Minigame();
    Minigame.Instance = unrelated;
    MultiRoleHandler.SetEnabled(false);
    Require(UnityEngine.Object.DestroyCalls.Count == 1 && ReferenceEquals(UnityEngine.Object.DestroyCalls[0], owned.gameObject) && owned.gameObject.Destroyed, "close failure destroyed wrong object");
    Require(unrelated.CloseCalls == 0 && !unrelated.Destroyed && ReferenceEquals(Minigame.Instance, unrelated), "close fallback touched another minigame");
});

Run("direct profile flag disable performs owned cleanup on next tick", () =>
{
    SetUp(); AddPlayer(2); Enable();
    MultiRoleHandler.CycleTarget(); MultiRoleHandler.OpenVitals();
    var owned = VitalsMinigame.Instances.Single();
    CheatToggles.multiRole = false;
    MultiRoleHandler.Tick();
    Require(owned.CloseCalls == 1 && !MultiRoleHandler.HasTarget && MultiRoleHandler.PanelOpen, "direct flag transition skipped cleanup");
});

Run("tracking exception stops mode and closes owned vitals", () =>
{
    SetUp(); var target = AddPlayer(2); Enable();
    MultiRoleHandler.CycleTarget(); MultiRoleHandler.OpenVitals();
    var owned = VitalsMinigame.Instances.Single();
    target.ThrowOnPosition = true;
    Time.unscaledTime = 1;
    MultiRoleHandler.Tick();
    Require(!CheatToggles.multiRole && !MultiRoleHandler.HasTarget && owned.CloseCalls == 1, "tick exception left role or owned UI active");
    Require(Plugin.Log.Warnings.Count == 1, "tick exception was not reported once");
}, allowWarnings: true);

Run("a host-assigned role change during use remains intact after disable", () =>
{
    SetUp(); AddPlayer(2); Enable();
    MultiRoleHandler.CycleTarget(); MultiRoleHandler.OpenVitals();
    var assignedByHost = new RoleBehaviour { Role = RoleTypes.Engineer };
    PlayerControl.LocalPlayer.Data.Role = assignedByHost;
    int assignments = PlayerControl.LocalPlayer.Data.RoleAssignments;
    MultiRoleHandler.Tick();
    MultiRoleHandler.SetEnabled(false);
    Require(ReferenceEquals(PlayerControl.LocalPlayer.Data.Role, assignedByHost), "cleanup restored an obsolete assigned role");
    Require(PlayerControl.LocalPlayer.Data.RoleAssignments == assignments, "bundle rewrote role after host assignment");
});

Run("closing owned vitals normally allows a fresh instance and cleans only that instance", () =>
{
    SetUp(); Enable(); MultiRoleHandler.OpenVitals();
    var first = VitalsMinigame.Instances.Single();
    first.ForceClose();
    MultiRoleHandler.OpenVitals();
    Require(VitalsMinigame.Instances.Count == 2, "closed native UI prevented reopening");
    var replacement = VitalsMinigame.Instances[1];
    MultiRoleHandler.SetEnabled(false);
    Require(first.CloseCalls == 1 && replacement.CloseCalls == 1 && Minigame.Instance is null, "cleanup closed obsolete or retained live owned UI");
});

Run("death then revival keeps an empty session until a new target is selected", () =>
{
    SetUp(); AddPlayer(2); Enable();
    MultiRoleHandler.CycleTarget(); MultiRoleHandler.OpenVitals();
    var owned = VitalsMinigame.Instances.Single();
    PlayerControl.LocalPlayer.Data.IsDead = true;
    MultiRoleHandler.Tick();
    PlayerControl.LocalPlayer.Data.IsDead = false;
    MultiRoleHandler.Tick();
    Require(MultiRoleHandler.Active && !MultiRoleHandler.HasTarget && owned.CloseCalls == 1, "revival resurrected stale target or owned UI");
    MultiRoleHandler.CycleTarget();
    Require(MultiRoleHandler.HasTarget, "new eligible session could not choose a fresh target");
});

Run("profile enable leaves a pre-existing task minigame untouched", () =>
{
    SetUp();
    var unrelated = new Minigame(); Minigame.Instance = unrelated;
    CheatToggles.multiRole = true;
    MultiRoleHandler.Tick();
    MultiRoleHandler.OpenVitals(); MultiRoleHandler.CycleTarget();
    Require(MultiRoleHandler.Active && VitalsMinigame.Instances.Count == 0 && !MultiRoleHandler.HasTarget, "profile enable bypassed tool guards");
    Require(ReferenceEquals(Minigame.Instance, unrelated) && unrelated.CloseCalls == 0, "profile enable took ownership of task UI");
});

Run("guest notebook is detached and never borrows prefab records or replaces assigned roles", () =>
{
    SetUp(); var victim = AddPlayer(9); Enable();
    var assigned = PlayerControl.LocalPlayer.Data.Role;
    var prefab = DetectivePrefab();
    prefab.notesPageInfos.Add(new(victim.Data)); prefab.deadPlayers.Add(victim.Data);
    prefab.buttonManager = new(); prefab.secondaryButtonManager = new();
    prefab.abilityInfo = new(); prefab.meetingAbilityInfo = new();
    MultiRoleHandler.OpenDetectiveNotes();
    var detached = DetectiveRole.Instances.Single();
    var notes = DetectiveNotesMinigame.Instances.Single();
    Require(!Utils.isHost && MultiRoleHandler.Active && ReferenceEquals(PlayerControl.LocalPlayer.Data.Role, assigned) && PlayerControl.LocalPlayer.Data.RoleAssignments == 0, "opening notebook changed native role or depends on hosting");
    Require(!detached.enabled && !detached.gameObject.activeSelf && ReferenceEquals(detached.Player, PlayerControl.LocalPlayer), "detached role participates as an enabled native role");
    Require(detached.buttonManager is null && detached.secondaryButtonManager is null && detached.abilityInfo is null && detached.meetingAbilityInfo is null, "detached role retained native ability managers");
    Require(!ReferenceEquals(detached.notesPageInfos, prefab.notesPageInfos) && !ReferenceEquals(detached.deadPlayers, prefab.deadPlayers) && detached.notesPageInfos.Count == 0 && detached.deadPlayers.Count == 0, "detached model borrowed prefab death records");
    Require(prefab.notesPageInfos.Count == 1 && prefab.deadPlayers.Count == 1, "notebook cleared unrelated prefab records");
    Require(ReferenceEquals(notes.Associated, detached) && notes.BeginCalls == 1 && notes.LastBeginPlayer is null && notes.transform.localPosition.z == -50, "notebook opened with wrong association or depth");
});

Run("confirmed death preserves all captured rooms from before kill animation", () =>
{
    SetUp(); var suspect = AddPlayer(2); var victim = AddPlayer(9);
    suspect.Position = new(2, 0); victim.Position = new(9, 0);
    Utils.RoomResolver = position => new() { RoomId = "Room " + position.x };
    Enable();
    var pending = MultiRoleDetectiveHandler.CapturePendingDeath(victim, MurderResultFlags.Succeeded);
    Require(pending is not null && !MultiRoleDetectiveHandler.HasCases, "prefix recorded an unconfirmed death");
    suspect.Position = new(99, 0); victim.Position = new(99, 0);
    victim.Data.IsDead = true;
    MultiRoleDetectiveHandler.ConfirmDeath(pending, victim);
    MultiRoleHandler.OpenDetectiveNotes();
    var detective = DetectiveRole.Instances.Single();
    Require(MultiRoleDetectiveHandler.GetCapturedLocation(9, 2) == "Room 2" && MultiRoleDetectiveHandler.GetCapturedLocation(9, 9) == "Room 9", "snapshot used post-kill positions");
    Require(detective.notesPageInfos.Count == 1 && detective.notesPageInfos[0].Location == "Room 9" && ReferenceEquals(detective.deadPlayers[0], victim.Data), "native notebook did not receive confirmed snapshot");
});

foreach (var flag in new[]
{
    (MurderResultFlags)0, MurderResultFlags.FailedError, MurderResultFlags.FailedProtected,
    MurderResultFlags.Succeeded | MurderResultFlags.FailedError,
    MurderResultFlags.Succeeded | MurderResultFlags.FailedProtected,
    MurderResultFlags.DecisionByHost | MurderResultFlags.FailedError,
    MurderResultFlags.DecisionByHost | MurderResultFlags.FailedProtected
})
    Run("failed or protected kill " + flag + " cannot fabricate a case", () =>
    {
        SetUp(); var victim = AddPlayer(9); Enable();
        var pending = MultiRoleDetectiveHandler.CapturePendingDeath(victim, flag);
        victim.Data.IsDead = true;
        MultiRoleDetectiveHandler.ConfirmDeath(pending, victim);
        MultiRoleHandler.Tick();
        Require(pending is null && !MultiRoleDetectiveHandler.HasCases, "failure flags yielded a case");
    });

Run("host-decided kill records only after native death is observed", () =>
{
    SetUp(); var victim = AddPlayer(9); Enable();
    var pending = MultiRoleDetectiveHandler.CapturePendingDeath(victim, MurderResultFlags.DecisionByHost);
    Require(pending is not null, "valid native host decision was excluded");
    MultiRoleDetectiveHandler.ConfirmDeath(pending, victim);
    MultiRoleHandler.Tick();
    Require(!MultiRoleDetectiveHandler.HasCases, "host decision fabricated a death before native confirmation");
    victim.Data.IsDead = true;
    MultiRoleHandler.Tick();
    Require(MultiRoleDetectiveHandler.HasCases, "delayed host-decided death never became a case");
});

Run("later real kill replaces a protected no-death attempt's stale locations", () =>
{
    SetUp(); var suspect = AddPlayer(2); var victim = AddPlayer(9); Enable();
    Utils.CurrentRoom = new() { RoomId = "First failed attempt" };
    var rejected = MultiRoleDetectiveHandler.CapturePendingDeath(victim, MurderResultFlags.DecisionByHost);
    MultiRoleDetectiveHandler.ConfirmDeath(rejected, victim); // Protected locally; no native death.
    Time.unscaledTime = 1;
    Utils.CurrentRoom = new() { RoomId = "Later real kill" };
    var accepted = MultiRoleDetectiveHandler.CapturePendingDeath(victim, MurderResultFlags.Succeeded);
    Require(accepted is not null, "earlier no-death candidate blocked a later kill");
    victim.Data.IsDead = true;
    MultiRoleDetectiveHandler.ConfirmDeath(accepted, victim);
    MultiRoleHandler.Tick();
    Require(MultiRoleDetectiveHandler.GetCapturedLocation(victim.PlayerId, suspect.PlayerId) == "Later real kill", "failed kill's stale rooms became evidence");
});

Run("repeated confirmations and duplicate kill callbacks create one case", () =>
{
    SetUp(); var victim = AddPlayer(9); Enable();
    var pending = MultiRoleDetectiveHandler.CapturePendingDeath(victim, MurderResultFlags.Succeeded);
    victim.Data.IsDead = true;
    MultiRoleDetectiveHandler.ConfirmDeath(pending, victim);
    MultiRoleDetectiveHandler.ConfirmDeath(pending, victim);
    Require(MultiRoleDetectiveHandler.CapturePendingDeath(victim, MurderResultFlags.Succeeded) is null, "confirmed victim produced another candidate");
    MultiRoleHandler.OpenDetectiveNotes();
    Require(DetectiveRole.Instances.Single().notesPageInfos.Count == 1, "duplicate callbacks duplicated death page");
});

foreach (var invalid in new (string Name, Action<PlayerControl> Change)[]
{
    ("already dead", player => player.Data.IsDead = true),
    ("disconnected", player => player.Data.Disconnected = true),
    ("destroyed", player => player.Destroyed = true),
    ("missing data", player => player.Data = null),
    ("meeting", _ => Utils.isMeeting = true),
    ("exile", _ => Utils.isExiling = true)
})
    Run("invalid victim context " + invalid.Name + " has no death snapshot", () =>
    {
        SetUp(); var victim = AddPlayer(9); Enable(); invalid.Change(victim);
        Require(MultiRoleDetectiveHandler.CapturePendingDeath(victim, MurderResultFlags.Succeeded) is null, "invalid native death context created snapshot");
    });

Run("wrong actor cannot confirm another actor's death token", () =>
{
    SetUp(); var victim = AddPlayer(9); var other = AddPlayer(8); Enable();
    var pending = MultiRoleDetectiveHandler.CapturePendingDeath(victim, MurderResultFlags.Succeeded);
    other.Data.IsDead = true;
    MultiRoleDetectiveHandler.ConfirmDeath(pending, other);
    Require(!MultiRoleDetectiveHandler.HasCases, "foreign actor committed another victim's snapshot");
});

foreach (bool destroy in new[] { false, true })
    Run("pending victim " + (destroy ? "destruction" : "disconnect") + " discards unconfirmed evidence", () =>
    {
        SetUp(); var victim = AddPlayer(9); Enable();
        var pending = MultiRoleDetectiveHandler.CapturePendingDeath(victim, MurderResultFlags.Succeeded);
        MultiRoleDetectiveHandler.ConfirmDeath(pending, victim);
        if (destroy) victim.Destroyed = true; else victim.Data.Disconnected = true;
        victim.Data.IsDead = true;
        MultiRoleHandler.Tick();
        Require(!MultiRoleDetectiveHandler.HasCases && MultiRoleDetectiveHandler.GetCapturedLocation(9, 0).Contains("Unknown", StringComparison.Ordinal), "disconnected victim became a case");
    });

Run("expired pending candidate cannot later fabricate a case", () =>
{
    SetUp(); var victim = AddPlayer(9); Enable();
    var pending = MultiRoleDetectiveHandler.CapturePendingDeath(victim, MurderResultFlags.Succeeded);
    MultiRoleDetectiveHandler.ConfirmDeath(pending, victim);
    Time.unscaledTime = 16;
    MultiRoleHandler.Tick();
    victim.Data.IsDead = true;
    MultiRoleHandler.Tick();
    Require(!MultiRoleDetectiveHandler.HasCases, "old no-death candidate survived expiry");
});

Run("enabling mid-round keeps old deaths and absent player histories unknown", () =>
{
    SetUp(); var oldVictim = AddPlayer(8); oldVictim.Data.IsDead = true;
    var missingSuspect = AddPlayer(2); missingSuspect.Data.Disconnected = true;
    var victim = AddPlayer(9); Enable(); MultiRoleHandler.Tick();
    Require(!MultiRoleDetectiveHandler.HasCases && MultiRoleDetectiveHandler.GetCapturedLocation(8, 0).Contains("Unknown", StringComparison.Ordinal), "enable invented historical death evidence");
    ConfirmedDeath(victim);
    missingSuspect.Data.Disconnected = false;
    Require(MultiRoleDetectiveHandler.GetCapturedLocation(9, 2).Contains("Unknown", StringComparison.Ordinal), "returning player received an invented historical location");
});

Run("meeting closes only owned notes and retains confirmed cases for later use", () =>
{
    SetUp(); var victim = AddPlayer(9); Enable(); ConfirmedDeath(victim);
    MultiRoleHandler.OpenDetectiveNotes();
    var notes = DetectiveNotesMinigame.Instances.Single();
    Utils.isMeeting = true; MultiRoleHandler.Tick();
    Require(notes.CloseCalls == 1 && Minigame.Instance is null && MultiRoleDetectiveHandler.HasCases, "meeting discarded case history or retained notes");
    Utils.isMeeting = false; MultiRoleHandler.OpenDetectiveNotes();
    Require(DetectiveNotesMinigame.Instances.Count == 2 && DetectiveRole.Instances.Count == 1, "after meeting notebook failed to reopen with the same case model");
});

foreach (bool replaceShip in new[] { false, true })
    Run("detective " + (replaceShip ? "ship" : "owned player") + " replacement destroys its own model and clears cases", () =>
    {
        SetUp(); var victim = AddPlayer(9); Enable(); ConfirmedDeath(victim);
        MultiRoleHandler.OpenDetectiveNotes();
        var detective = DetectiveRole.Instances.Single(); var notes = DetectiveNotesMinigame.Instances.Single();
        if (replaceShip) ShipStatus.Instance = new(); else PlayerControl.LocalPlayer = MakePlayer(0, true);
        MultiRoleHandler.Tick();
        Require(notes.CloseCalls == 1 && detective.gameObject.Destroyed && !MultiRoleDetectiveHandler.HasCases, "new round retained another round's detective state");
        Require(!MultiRoleDetectiveHandler.Owns(detective) && !MultiRoleDetectiveHandler.Owns(notes), "reset retained stale native UI ownership");
    });

Run("profile disable closes owned notes without touching a replacement task minigame", () =>
{
    SetUp(); var victim = AddPlayer(9); Enable(); ConfirmedDeath(victim);
    MultiRoleHandler.OpenDetectiveNotes();
    var notes = DetectiveNotesMinigame.Instances.Single(); var detective = DetectiveRole.Instances.Single();
    var unrelated = new Minigame(); Minigame.Instance = unrelated;
    CheatToggles.multiRole = false; MultiRoleHandler.Tick();
    Require(notes.CloseCalls == 1 && detective.gameObject.Destroyed && !MultiRoleDetectiveHandler.HasCases, "profile off did not reset notebook");
    Require(ReferenceEquals(Minigame.Instance, unrelated) && unrelated.CloseCalls == 0 && !unrelated.Destroyed, "notebook reset closed unrelated task UI");
});

Run("interrogation adds evidence locally while keeping assigned role unchanged", () =>
{
    SetUp(); var suspect = AddPlayer(2); var victim = AddPlayer(9); Enable(); var record = ConfirmedDeath(victim);
    var assigned = PlayerControl.LocalPlayer.Data.Role;
    MultiRoleHandler.CycleTarget();
    Require(MultiRoleHandler.CanInterrogate, "nearby living tracked player was not available");
    MultiRoleHandler.InterrogateTarget();
    var page = DetectiveRole.Instances.Single().notesPageInfos.Single();
    Require(record.Suspects.Count == 1 && ReferenceEquals(record.Suspects[0], suspect.Data) && page.suspects.Count == 0, "interrogation did not keep selected suspect in managed case evidence");
    Require(ReferenceEquals(PlayerControl.LocalPlayer.Data.Role, assigned) && PlayerControl.LocalPlayer.Data.RoleAssignments == 0, "interrogation replaced native role");
    Require(ReferenceEquals(DetectiveNotesMinigame.Instances.Single().Page, page), "interrogation opened wrong case");
});

foreach (var invalid in new (string Name, Action<PlayerControl> Change)[]
{
    ("far away", player => player.Position = new(20, 0)),
    ("dead", player => player.Data.IsDead = true),
    ("disconnected", player => player.Data.Disconnected = true),
    ("no collider", player => player.Collider = null),
    ("disabled collider", player => player.Collider.enabled = false),
    ("missing data", player => player.Data = null)
})
    Run("invalid interrogation target " + invalid.Name + " cannot create a suspect", () =>
    {
        SetUp(); var suspect = AddPlayer(2); var victim = AddPlayer(9); Enable(); ConfirmedDeath(victim);
        invalid.Change(suspect);
        Require(!MultiRoleDetectiveHandler.CanInterrogate(suspect), "invalid target was available for interrogation");
        MultiRoleDetectiveHandler.Interrogate(suspect);
        Require(DetectiveRole.Instances.Count == 0 && DetectiveNotesMinigame.Instances.Count == 0, "invalid interrogation opened notebook");
    });

Run("interrogation debounce, duplicate prevention and three-suspect limit preserve case integrity", () =>
{
    SetUp(); var suspects = new[] { AddPlayer(2), AddPlayer(3), AddPlayer(4), AddPlayer(5) };
    var victim = AddPlayer(9); Enable(); var record = ConfirmedDeath(victim);
    MultiRoleDetectiveHandler.Interrogate(suspects[0]);
    DetectiveNotesMinigame.Instances.Last().ForceClose();
    Require(!MultiRoleDetectiveHandler.CanInterrogate(suspects[1]), "same-frame interrogation bypassed debounce");
    for (int i = 1; i < 3; i++)
    {
        Time.unscaledTime = i;
        MultiRoleDetectiveHandler.Interrogate(suspects[i]);
        DetectiveNotesMinigame.Instances.Last().ForceClose();
    }
    var page = DetectiveRole.Instances.Single().notesPageInfos.Single();
    Time.unscaledTime = 3;
    MultiRoleDetectiveHandler.Interrogate(suspects[0]);
    MultiRoleDetectiveHandler.Interrogate(suspects[3]);
    Require(record.Suspects.Count == 3 && record.Suspects.Select(s => s.PlayerId).Distinct().Count() == 3 && page.suspects.Count == 0, "duplicate or fourth suspect corrupted managed case or entered native value list");
    Require(DetectiveNotesMinigame.Instances.Count == 3 && Plugin.Log.Warnings.Count == 2, "refused suspect unexpectedly opened another notebook");
}, allowWarnings: true);

Run("native suspect view displays captured evidence and unknown data without role casting", () =>
{
    SetUp(); var suspect = AddPlayer(2); var victim = AddPlayer(9); Enable(); ConfirmedDeath(victim);
    var view = new DetectiveNotesSuspectInterface();
    MultiRoleDetectiveHandler.RenderSuspect(view, suspect.Data, victim.Data, 1, 7);
    Require(view.playerName.text == suspect.Data.PlayerName && view.locationName.text == "Storage" && view.numberedText.text == "2" && view.player.MaskLayer == 7 && ReferenceEquals(view.player.RenderedPlayer, suspect.Data), "suspect view lost captured evidence or avatar");
    Require(!view.player.IncludePet && !view.player.IsDead && view.player.UpdateCallback is null && view.player.ForceAlive && view.player.DataUpdateCalls == 0 && view.player.OutfitUpdateCalls == 1, "suspect portrait used native data indexer path or incorrect Notes outfit arguments");
    var late = AddPlayer(3);
    MultiRoleDetectiveHandler.RenderSuspect(view, late.Data, victim.Data, 0, 0);
    Require(view.locationName.text.Contains("Unknown", StringComparison.Ordinal), "missing historic evidence rendered as a room");
    MultiRoleDetectiveHandler.RenderSuspect(view, null, victim.Data, 0, 0);
    Require(view.ClearCalls == 1, "missing suspect data was not cleared");
});

Run("native notebook ownership remains scoped to its model, minigame and descendants", () =>
{
    SetUp(); Enable(); MultiRoleHandler.OpenDetectiveNotes();
    var detective = DetectiveRole.Instances.Single(); var notes = DetectiveNotesMinigame.Instances.Single();
    var ownedView = new DetectiveNotesSuspectInterface(); ownedView.transform.parent = notes.transform;
    var unrelatedView = new DetectiveNotesSuspectInterface();
    Require(MultiRoleDetectiveHandler.Owns(detective) && MultiRoleDetectiveHandler.Owns(notes) && MultiRoleDetectiveHandler.Owns(ownedView), "owned UI was not recognized");
    Require(!MultiRoleDetectiveHandler.Owns(DetectivePrefab()) && !MultiRoleDetectiveHandler.Owns(new DetectiveNotesMinigame()) && !MultiRoleDetectiveHandler.Owns(unrelatedView), "scoped patch would affect unrelated native Detective UI");
    MultiRoleHandler.SetEnabled(false);
    Require(!MultiRoleDetectiveHandler.Owns(ownedView), "reset retained suspect renderer scope");
});

Run("case map uses ordinary map and never handles an unrelated notebook", () =>
{
    SetUp(); var victim = AddPlayer(9); Enable(); ConfirmedDeath(victim); MultiRoleHandler.OpenDetectiveNotes();
    var notes = DetectiveNotesMinigame.Instances.Single();
    MultiRoleDetectiveHandler.OpenCaseMap(new DetectiveNotesMinigame());
    Require(HudManager.Instance.MapCalls.Count == 0, "unrelated notebook opened custom map");
    MultiRoleDetectiveHandler.OpenCaseMap(notes);
    Require(HudManager.Instance.MapCalls.Count == 1 && HudManager.Instance.MapCalls[0].Mode == MapOptions.Modes.Normal && notes.mapFadeBackground.activeSelf, "detached notebook requested native Detective-role map mode");
});

Run("owned notebook cleanup closes its own case map and ends close scope", () =>
{
    SetUp(); var victim = AddPlayer(9); Enable(); ConfirmedDeath(victim); MultiRoleHandler.OpenDetectiveNotes();
    var notes = DetectiveNotesMinigame.Instances.Single();
    MultiRoleDetectiveHandler.OpenCaseMap(notes);
    var map = MapBehaviour.Instance;
    MultiRoleHandler.SetEnabled(false);
    Require(notes.NativeCloseCalls == 1 && notes.CloseCalls == 1 && map.CloseCalls == 1 && !map.IsOpen, "owned notebook retained case map or skipped native overlays cleanup");
    Require(MultiRoleDetectiveHandler.MayCloseMap(new MapBehaviour()), "close scope leaked beyond notebook cleanup");
});

Run("owned notebook cleanup leaves an unrelated open map untouched", () =>
{
    SetUp(); Enable(); MultiRoleHandler.OpenDetectiveNotes();
    var notes = DetectiveNotesMinigame.Instances.Single();
    var unrelated = new MapBehaviour { IsOpen = true }; MapBehaviour.Instance = unrelated;
    MultiRoleHandler.SetEnabled(false);
    Require(notes.NativeCloseCalls == 1 && notes.CloseCalls == 1 && unrelated.IsOpen && unrelated.CloseCalls == 0, "native notebook Close destroyed an unrelated map");
});

Run("manual map opening prevents another notebook from stealing map ownership", () =>
{
    SetUp(); Enable();
    var unrelated = new MapBehaviour { IsOpen = true }; MapBehaviour.Instance = unrelated;
    Require(!MultiRoleHandler.DetectiveAvailable, "notebook advertised availability over open unrelated map");
    MultiRoleHandler.OpenDetectiveNotes();
    Require(DetectiveNotesMinigame.Instances.Count == 0 && unrelated.IsOpen && unrelated.CloseCalls == 0, "opening notebook bypassed map ownership guard");
});

foreach (bool exile in new[] { false, true })
    Run((exile ? "exile" : "meeting") + " discards rejected kill candidate before a voting death", () =>
    {
        SetUp(); var victim = AddPlayer(9); Enable();
        var pending = MultiRoleDetectiveHandler.CapturePendingDeath(victim, MurderResultFlags.DecisionByHost);
        MultiRoleDetectiveHandler.ConfirmDeath(pending, victim);
        if (exile) Utils.isExiling = true; else Utils.isMeeting = true;
        MultiRoleHandler.Tick();
        victim.Data.IsDead = true;
        Utils.isExiling = Utils.isMeeting = false;
        MultiRoleHandler.Tick();
        Require(!MultiRoleDetectiveHandler.HasCases, "voting death committed an earlier rejected kill snapshot");
    });

foreach (bool failPage in new[] { false, true })
    Run("native notes " + (failPage ? "page" : "Begin") + " failure cleans partially created owned UI", () =>
    {
        SetUp(); var victim = AddPlayer(9); Enable(); ConfirmedDeath(victim);
        var prefab = DetectivePrefab().notesPrefab.GetComponent<DetectiveNotesMinigame>();
        if (failPage) prefab.ThrowOnOpenPage = true; else prefab.ThrowOnBegin = true;
        MultiRoleHandler.OpenDetectiveNotes();
        var notes = DetectiveNotesMinigame.Instances.Single();
        Require(notes.NativeCloseCalls == 1 && notes.CloseCalls == 1 && Minigame.Instance is null && !MultiRoleDetectiveHandler.Owns(notes), "failed native notebook leaked owned UI");
        Require(MultiRoleHandler.Active && MultiRoleDetectiveHandler.HasCases && Plugin.Log.Warnings.Count == 1, "notebook failure lost other abilities or case history");
    }, allowWarnings: true);

Run("notebook prefab without required component destroys only the stray clone", () =>
{
    SetUp(); Enable(); DetectivePrefab().notesPrefab = new GameObject();
    MultiRoleHandler.OpenDetectiveNotes();
    Require(DetectiveNotesMinigame.Instances.Count == 0 && UnityEngine.Object.DestroyCalls.Count == 1 && UnityEngine.Object.DestroyCalls[0] is GameObject, "missing component left a stray native prefab instance");
    Require(!DetectiveRole.Instances.Single().gameObject.Destroyed && PlayerControl.LocalPlayer.Data.RoleAssignments == 0, "missing component destroyed model or assigned role");
}, allowWarnings: true);

Run("notes ForceClose failure destroys only its own game object", () =>
{
    SetUp(); Enable();
    DetectivePrefab().notesPrefab.GetComponent<DetectiveNotesMinigame>().ThrowOnForceClose = true;
    MultiRoleHandler.OpenDetectiveNotes();
    var notes = DetectiveNotesMinigame.Instances.Single();
    var unrelated = new Minigame(); Minigame.Instance = unrelated;
    MultiRoleHandler.SetEnabled(false);
    Require(notes.gameObject.Destroyed && notes.NativeCloseCalls == 1 && notes.CloseCalls == 1, "failed close retained owned notebook");
    Require(ReferenceEquals(Minigame.Instance, unrelated) && unrelated.CloseCalls == 0 && !unrelated.Destroyed, "close fallback destroyed replacement task UI");
});

Run("selected death marker uses captured position, map scale and mirrored ship orientation", () =>
{
    SetUp(); var first = AddPlayer(8); var second = AddPlayer(9);
    first.Position = new(4, 6); second.Position = new(10, 8);
    ShipStatus.Instance.MapScale = 2;
    Enable(); ConfirmedDeath(first); ConfirmedDeath(second);
    first.Position = second.Position = new(99, 99); // Dead actors move during later animations.
    MultiRoleHandler.OpenDetectiveNotes();
    var notes = DetectiveNotesMinigame.Instances.Single();
    MultiRoleDetectiveHandler.OpenCaseMap(notes);
    var map = MapBehaviour.Instance;
    MultiRoleDetectiveHandler.UpdateCaseMap(map);
    var marker = SpriteRenderer.Instances.Single();
    Require(marker.transform.localPosition.x == 5 && marker.transform.localPosition.y == 4 && marker.transform.localPosition.z == -1, "marker did not use last selected case's captured scaled position");
    notes.currentPageIndex = 0;
    MultiRoleDetectiveHandler.UpdateCaseMap(map);
    Require(marker.transform.localPosition.x == 2 && marker.transform.localPosition.y == 3, "case selection kept another victim's marker");
    ShipStatus.Instance.transform.localScale = new(-1, 1, 1);
    MultiRoleDetectiveHandler.UpdateCaseMap(map);
    Require(marker.transform.localPosition.x == -2 && marker.transform.localPosition.y == 3, "mirrored ship marker has wrong coordinate direction");
    Require(SpriteRenderer.Instances.Count == 1 && !ReferenceEquals(marker, map.HerePoint) && !map.HerePoint.Destroyed, "notebook borrowed or repeatedly recreated native HerePoint");
    Require(!ReferenceEquals(marker.material, map.HerePoint.material) && map.HerePoint.material.Colors.Count == 0, "death-marker tint changed native marker material");
});

Run("unrelated map updates and closing preserve owned marker and native markers", () =>
{
    SetUp(); var victim = AddPlayer(9); victim.Position = new(3, 4); Enable(); ConfirmedDeath(victim);
    MultiRoleHandler.OpenDetectiveNotes(); var notes = DetectiveNotesMinigame.Instances.Single();
    MultiRoleDetectiveHandler.OpenCaseMap(notes); var ownedMap = MapBehaviour.Instance;
    MultiRoleDetectiveHandler.UpdateCaseMap(ownedMap); var marker = SpriteRenderer.Instances.Single();
    var unrelated = new MapBehaviour { IsOpen = true };
    MultiRoleDetectiveHandler.UpdateCaseMap(unrelated);
    MultiRoleDetectiveHandler.OnCaseMapClosed(unrelated);
    Require(!marker.gameObject.Destroyed && marker.transform.localPosition.x == 3 && marker.transform.localPosition.y == 4 && SpriteRenderer.Instances.Count == 1, "unrelated map changed owned death marker");
    MultiRoleDetectiveHandler.OnCaseMapClosed(ownedMap);
    Require(marker.gameObject.Destroyed && marker.material.Destroyed && !ownedMap.HerePoint.Destroyed && !unrelated.HerePoint.Destroyed && !ownedMap.HerePoint.material.Destroyed && !unrelated.HerePoint.material.Destroyed, "case-map close retained owned resources or destroyed native marker resources");
});

Run("notes cleanup destroys only its captured death marker", () =>
{
    SetUp(); var victim = AddPlayer(9); Enable(); ConfirmedDeath(victim);
    MultiRoleHandler.OpenDetectiveNotes(); var notes = DetectiveNotesMinigame.Instances.Single();
    HudManager.Instance.Map.transform.localPosition = new(7, 8, -20);
    MultiRoleDetectiveHandler.OpenCaseMap(notes); var map = MapBehaviour.Instance;
    MultiRoleDetectiveHandler.UpdateCaseMap(map); var marker = SpriteRenderer.Instances.Single();
    MultiRoleHandler.SetEnabled(false);
    Require(marker.gameObject.Destroyed && marker.material.Destroyed && !map.HerePoint.Destroyed && !map.HerePoint.material.Destroyed && map.CloseCalls == 1, "reset destroyed native marker resources or leaked owned marker resources");
    Require(map.transform.localPosition.x == 7 && map.transform.localPosition.y == 8 && map.transform.localPosition.z == -20, "reset retained the notebook's temporary map depth");
});

Run("explicit close exits empty notes without disabling the bundle or changing assigned role", () =>
{
    SetUp(); Enable(); var assigned = PlayerControl.LocalPlayer.Data.Role;
    MultiRoleHandler.OpenDetectiveNotes(); var notes = DetectiveNotesMinigame.Instances.Single();
    Require(MultiRoleDetectiveHandler.NotesOpen && !MultiRoleDetectiveHandler.HasCases, "empty notebook did not report owned open state");
    MultiRoleDetectiveHandler.CloseNotes();
    Require(!MultiRoleDetectiveHandler.NotesOpen && Minigame.Instance is null && notes.NativeCloseCalls == 1 && notes.CloseCalls == 1, "explicit close left empty notebook active");
    Require(MultiRoleHandler.Active && ReferenceEquals(PlayerControl.LocalPlayer.Data.Role, assigned) && PlayerControl.LocalPlayer.Data.RoleAssignments == 0, "empty-notes close disabled abilities or changed real role");
    MultiRoleHandler.OpenDetectiveNotes();
    Require(DetectiveNotesMinigame.Instances.Count == 2 && DetectiveRole.Instances.Count == 1, "closed empty notes could not reopen without a new assigned role");
});

Run("explicit close with no owned notes preserves an unrelated minigame", () =>
{
    SetUp(); Enable(); var unrelated = new Minigame(); Minigame.Instance = unrelated;
    MultiRoleDetectiveHandler.CloseNotes();
    Require(ReferenceEquals(Minigame.Instance, unrelated) && unrelated.CloseCalls == 0 && !unrelated.Destroyed && !MultiRoleDetectiveHandler.NotesOpen, "explicit close borrowed current unrelated minigame");
});

Run("explicit close dismisses only owned notes while a replacement task minigame stays active", () =>
{
    SetUp(); Enable(); MultiRoleHandler.OpenDetectiveNotes();
    var notes = DetectiveNotesMinigame.Instances.Single();
    var unrelated = new Minigame(); Minigame.Instance = unrelated;
    MultiRoleDetectiveHandler.CloseNotes();
    Require(notes.NativeCloseCalls == 1 && notes.CloseCalls == 1 && !MultiRoleDetectiveHandler.NotesOpen, "explicit close did not release its own notebook");
    Require(ReferenceEquals(Minigame.Instance, unrelated) && unrelated.CloseCalls == 0 && !unrelated.Destroyed, "explicit close dismissed a replacement task minigame");
});

Run("Escape closes the active owned empty notebook exactly once", () =>
{
    SetUp(); Enable(); MultiRoleHandler.OpenDetectiveNotes();
    var notes = DetectiveNotesMinigame.Instances.Single();
    Input.Down.Add(KeyCode.Escape); MultiRoleHandler.Tick(); MultiRoleHandler.Tick();
    Require(notes.NativeCloseCalls == 1 && notes.CloseCalls == 1 && Minigame.Instance is null && !MultiRoleDetectiveHandler.NotesOpen, "Escape retained or repeatedly closed active owned notes");
    Require(MultiRoleHandler.Active && !DetectiveRole.Instances.Single().gameObject.Destroyed, "Escape destroyed reusable local detective model");
});

Run("Escape aimed at a replacement minigame leaves both it and abandoned owned notes alone", () =>
{
    SetUp(); Enable(); MultiRoleHandler.OpenDetectiveNotes();
    var notes = DetectiveNotesMinigame.Instances.Single();
    var unrelated = new Minigame(); Minigame.Instance = unrelated;
    Input.Down.Add(KeyCode.Escape); MultiRoleHandler.Tick();
    Require(notes.CloseCalls == 0 && notes.NativeCloseCalls == 0 && !notes.Destroyed, "Escape closed notes that were not the active minigame");
    Require(ReferenceEquals(Minigame.Instance, unrelated) && unrelated.CloseCalls == 0 && !unrelated.Destroyed, "Escape intercepted replacement minigame ownership");
});

foreach (var higherPriority in new (string Name, Action Change)[]
{
    ("mod menu", () => MenuUI.isGUIActive = true),
    ("open chat", () => HudManager.Instance.Chat.IsOpenOrOpening = true),
    ("missing HUD", () => HudManager.Instance = null)
})
    Run("Escape for " + higherPriority.Name + " does not also close owned notes", () =>
    {
        SetUp(); Enable(); MultiRoleHandler.OpenDetectiveNotes();
        var notes = DetectiveNotesMinigame.Instances.Single();
        higherPriority.Change(); Input.Down.Add(KeyCode.Escape); MultiRoleHandler.Tick();
        Require(notes.CloseCalls == 0 && notes.NativeCloseCalls == 0 && ReferenceEquals(Minigame.Instance, notes), "Escape bypassed higher-priority UI context");
    });

Run("Escape while the case map is open preserves notebook until a later Escape", () =>
{
    SetUp(); var victim = AddPlayer(9); Enable(); ConfirmedDeath(victim);
    MultiRoleHandler.OpenDetectiveNotes(); var notes = DetectiveNotesMinigame.Instances.Single();
    MultiRoleDetectiveHandler.OpenCaseMap(notes); var map = MapBehaviour.Instance;
    Input.Down.Add(KeyCode.Escape); MultiRoleHandler.Tick();
    Require(notes.CloseCalls == 0 && notes.NativeCloseCalls == 0 && MultiRoleDetectiveHandler.NotesOpen, "case-map Escape also closed notebook");
    Require(!map.IsOpen && map.CloseCalls == 1, "case-map Escape failed to close its exact owned map first");
    Input.Down.Clear(); MultiRoleHandler.Tick();
    Input.Down.Add(KeyCode.Escape); MultiRoleHandler.Tick();
    Require(notes.CloseCalls == 1 && notes.NativeCloseCalls == 1 && !MultiRoleDetectiveHandler.NotesOpen && map.CloseCalls == 1, "later Escape failed to exit notebook after map closed");
});

Run("explicit notebook close retains cases and destroys only its owned map resources", () =>
{
    SetUp(); var victim = AddPlayer(9); Enable(); ConfirmedDeath(victim);
    MultiRoleHandler.OpenDetectiveNotes(); var notes = DetectiveNotesMinigame.Instances.Single();
    MultiRoleDetectiveHandler.OpenCaseMap(notes); var map = MapBehaviour.Instance;
    var marker = SpriteRenderer.Instances.Single();
    MultiRoleDetectiveHandler.CloseNotes();
    Require(MultiRoleDetectiveHandler.HasCases && !MultiRoleDetectiveHandler.NotesOpen && notes.CloseCalls == 1 && map.CloseCalls == 1, "explicit close discarded history or retained owned UI");
    Require(marker.gameObject.Destroyed && marker.material.Destroyed && !map.HerePoint.Destroyed && !map.HerePoint.material.Destroyed, "explicit close leaked owned resources or destroyed native marker");
});

Run("owned page rendering preserves native victim layout without native suspect value arguments", () =>
{
    SetUp(); var suspect = AddPlayer(2); var victim = AddPlayer(9); Enable(); var record = ConfirmedDeath(victim);
    MultiRoleDetectiveHandler.Interrogate(suspect);
    var notes = DetectiveNotesMinigame.Instances.Single();
    var page = DetectiveRole.Instances.Single().notesPageInfos.Single();
    var slot = notes.suspectContainers[0];
    Require(notes.NativePageSetupCalls == 1 && ReferenceEquals(notes.NativeVictimRendered, victim.Data), "safe render skipped native victim layout");
    Require(record.Suspects.Count == 1 && page.suspects.Count == 0 && slot.suspectInterface && ReferenceEquals(slot.suspectInterface.player.RenderedPlayer, suspect.Data), "safe render lost managed interrogation suspect or entered native value list");
    Require(slot.suspectInterface.locationName.text == "Storage" && !slot.SuspectPlaceholderText.gameObject.activeSelf && ReferenceEquals(slot.BGSprite.sprite, slot.activeBGSprite) && !notes.noSuspectsPostIt.activeSelf, "occupied suspect slot did not replace native empty placeholders");
    Require(notes.suspectContainers.Take(3).Skip(1).All(empty => empty.SuspectPlaceholderText.gameObject.activeSelf && ReferenceEquals(empty.BGSprite.sprite, empty.inactiveBGSprite)), "empty suspect slots retained occupied appearance");
    Require(!notes.suspectContainers[3].gameObject.activeSelf, "unusable fourth native suspect slot remained visible");
    Require(PlayerControl.LocalPlayer.Data.Role.Role == RoleTypes.Crewmate && PlayerControl.LocalPlayer.Data.RoleAssignments == 0 && Plugin.Log.Warnings.Count == 0, "safe notebook rendering depended on a native Detective assignment or unsafe callback");
});

Run("owned page renders managed suspects repeatedly while native suspect list stays empty", () =>
{
    SetUp(); var suspect = AddPlayer(2); var victim = AddPlayer(9); Enable(); var record = ConfirmedDeath(victim);
    MultiRoleDetectiveHandler.Interrogate(suspect); var notes = DetectiveNotesMinigame.Instances.Single();
    var page = DetectiveRole.Instances.Single().notesPageInfos.Single(); var nativeList = page.suspects;
    notes.SetUpCurrentPage(); notes.SetUpCurrentPage();
    Require(page.suspects.Count == 0 && ReferenceEquals(page.suspects, nativeList), "native page body regained suspect value arguments or a temporary list mutation");
    Require(record.Suspects.Count == 1 && ReferenceEquals(record.Suspects[0], suspect.Data), "repeated page rendering mutated managed evidence");
});

Run("native page setup exception leaves separate managed suspect evidence intact", () =>
{
    SetUp(); var suspect = AddPlayer(2); var victim = AddPlayer(9); Enable(); var record = ConfirmedDeath(victim);
    MultiRoleDetectiveHandler.Interrogate(suspect); var notes = DetectiveNotesMinigame.Instances.Single();
    var page = DetectiveRole.Instances.Single().notesPageInfos.Single(); var original = page.suspects;
    notes.ThrowOnNativePageSetup = true;
    bool failed = false;
    try { notes.SetUpCurrentPage(); }
    catch (InvalidOperationException error) { failed = error.Message == "test native page setup failure"; }
    Require(failed && ReferenceEquals(page.suspects, original) && original.Count == 0 && record.Suspects.Count == 1 && ReferenceEquals(record.Suspects[0], suspect.Data), "native page exception lost managed evidence or reached unsafe suspect rendering");
});

Run("safe-page hook and renderer leave unrelated native notebooks unchanged", () =>
{
    SetUp(); Enable(); MultiRoleHandler.OpenDetectiveNotes();
    var unrelatedRole = new DetectiveRole();
    var page = new DetectiveNotesPageInfo(PlayerControl.LocalPlayer.Data);
    page.suspects.Add(new(PlayerControl.LocalPlayer.Data, null)); unrelatedRole.notesPageInfos.Add(page);
    var unrelated = new DetectiveNotesMinigame { Associated = unrelatedRole };
    var original = page.suspects; unrelated.noSuspectsPostIt.SetActive(false);
    MultiRoleDetectiveSafePage.Postfix(unrelated);
    MultiRoleDetectiveHandler.RenderPage(unrelated);
    Require(ReferenceEquals(page.suspects, original) && original.Count == 1 && !unrelated.noSuspectsPostIt.activeSelf && unrelated.suspectContainers.All(slot => slot.suspectInterface is null), "owned safe render changed an unrelated native notebook");
});

Run("page switching refreshes selected victim and suspect evidence then clears empty slots", () =>
{
    SetUp(); var suspect = AddPlayer(2); var first = AddPlayer(8); var second = AddPlayer(9); Enable();
    Utils.CurrentRoom = new() { RoomId = "First death room" }; var firstRecord = ConfirmedDeath(first);
    Utils.CurrentRoom = new() { RoomId = "Second death room" }; var secondRecord = ConfirmedDeath(second);
    MultiRoleHandler.OpenDetectiveNotes(); var notes = DetectiveNotesMinigame.Instances.Single();
    var pages = DetectiveRole.Instances.Single().notesPageInfos;
    firstRecord.Suspects.Add(suspect.Data); secondRecord.Suspects.Add(suspect.Data);
    notes.currentPageIndex = 0; notes.SetUpCurrentPage();
    var view = notes.suspectContainers[0].suspectInterface;
    Require(ReferenceEquals(notes.NativeVictimRendered, first.Data) && view.locationName.text == "First death room", "first selected page showed another victim's evidence");
    notes.currentPageIndex = 1; notes.SetUpCurrentPage();
    Require(ReferenceEquals(notes.NativeVictimRendered, second.Data) && ReferenceEquals(notes.suspectContainers[0].suspectInterface, view) && view.locationName.text == "Second death room", "page switch kept stale victim or suspect location");
    secondRecord.Suspects.Clear(); notes.SetUpCurrentPage();
    Require(notes.noSuspectsPostIt.activeSelf && notes.suspectContainers[0].SuspectPlaceholderText.gameObject.activeSelf && !view.container.activeSelf && view.player.RenderedPlayer is null, "empty page retained stale suspect portrait or evidence");
});

Run("dangerous native SetPlayerInfo value-argument detour is absent from actual patch source", () =>
{
    SetUp();
    var patches = typeof(MultiRoleDetectiveSafePage).Assembly.GetTypes()
        .SelectMany(type => type.GetCustomAttributes(typeof(HarmonyLib.HarmonyPatch), false).Cast<HarmonyLib.HarmonyPatch>());
    Require(!patches.Any(patch => patch.DeclaringType == typeof(DetectiveNotesSuspectInterface) && patch.MethodName == nameof(DetectiveNotesSuspectInterface.SetPlayerInfo)), "unsafe native suspect struct-argument method was hooked again");
    Require(typeof(MultiRoleDetectiveSafePage).GetMethods().All(method => method.GetParameters().All(parameter => parameter.ParameterType != typeof(DetectiveSuspect))), "safe page callback regained a native suspect value argument");
});

Run("owned map render guard permits only synchronous local UI rendering without changing movement", () =>
{
    SetUp(); var unrelated = AddPlayer(2); var victim = AddPlayer(9); Enable(); ConfirmedDeath(victim);
    MultiRoleHandler.OpenDetectiveNotes(); var notes = DetectiveNotesMinigame.Instances.Single();
    var local = PlayerControl.LocalPlayer; local.CanMove = false;
    int observations = 0;
    HudManager.Instance.OnToggleMap = () =>
    {
        observations++;
        bool localResult = false, unrelatedResult = false;
        Require(!MultiRoleDetectiveMapRenderGuard.Prefix(local, ref localResult) && localResult && !local.CanMove, "UI scope did not permit local render or rewrote actual movement");
        Require(MultiRoleDetectiveMapRenderGuard.Prefix(unrelated, ref unrelatedResult) && !unrelatedResult, "UI scope changed an unrelated actor's move guard");
    };
    HudManager.Instance.CanRenderMap = () =>
    {
        bool result = local.CanMove;
        return !MultiRoleDetectiveMapRenderGuard.Prefix(local, ref result) ? result : local.CanMove;
    };
    MultiRoleDetectiveHandler.OpenCaseMap(notes);
    bool after = false;
    Require(observations == 1 && MapBehaviour.Instance.IsOpen && !local.CanMove && !MultiRoleDetectiveHandler.AllowOwnedMapRender(local) && MultiRoleDetectiveMapRenderGuard.Prefix(local, ref after) && !after, "render-only guard escaped its scope or did not open movement-locked map");
});

Run("map-toggle exception clears render scope and keeps movement and evidence unchanged", () =>
{
    SetUp(); var victim = AddPlayer(9); Enable(); ConfirmedDeath(victim);
    MultiRoleHandler.OpenDetectiveNotes(); var notes = DetectiveNotesMinigame.Instances.Single();
    var local = PlayerControl.LocalPlayer; local.CanMove = false;
    HudManager.Instance.OnToggleMap = () => Require(MultiRoleDetectiveHandler.AllowOwnedMapRender(local), "throw fixture did not enter owned render scope");
    HudManager.Instance.ThrowOnToggleMap = true;
    Require(!MultiRoleDetectiveSafeMap.Prefix(notes), "failed owned map should not fall through to native Detective-role map");
    bool result = false;
    Require(!MultiRoleDetectiveHandler.AllowOwnedMapRender(local) && MultiRoleDetectiveMapRenderGuard.Prefix(local, ref result) && !result && !local.CanMove, "map exception left move guard overridden");
    Require(MultiRoleDetectiveHandler.HasCases && MultiRoleDetectiveHandler.NotesOpen && Plugin.Log.Warnings.Count == 1, "map exception lost evidence or notebook");
}, allowWarnings: true);

Run("map rendering early return and unrelated notebook never enter local move scope", () =>
{
    SetUp(); var victim = AddPlayer(9); Enable(); ConfirmedDeath(victim);
    MultiRoleHandler.OpenDetectiveNotes(); var notes = DetectiveNotesMinigame.Instances.Single();
    var unrelated = new Minigame(); Minigame.Instance = unrelated;
    int observations = 0; HudManager.Instance.OnToggleMap = () => observations++;
    MultiRoleDetectiveHandler.OpenCaseMap(notes);
    Require(MultiRoleDetectiveSafeMap.Prefix(new DetectiveNotesMinigame()), "unrelated notebook's native map was intercepted");
    bool result = false;
    Require(observations == 0 && !MultiRoleDetectiveHandler.AllowOwnedMapRender(PlayerControl.LocalPlayer) && MultiRoleDetectiveMapRenderGuard.Prefix(PlayerControl.LocalPlayer, ref result) && !result && ReferenceEquals(Minigame.Instance, unrelated), "invalid map context entered local rendering scope");
});

foreach (var invalid in new (string Name, Action Change)[]
{
    ("meeting", () => Utils.isMeeting = true),
    ("exile", () => Utils.isExiling = true),
    ("mod menu", () => MenuUI.isGUIActive = true),
    ("chat", () => HudManager.Instance.Chat.IsOpenOrOpening = true),
    ("intro", () => HudManager.Instance.IsIntroDisplayed = true),
    ("vent", () => PlayerControl.LocalPlayer.inVent = true),
    ("ladder", () => PlayerControl.LocalPlayer.onLadder = true),
    ("moving platform", () => PlayerControl.LocalPlayer.inMovingPlat = true)
})
    Run("render scope rechecks " + invalid.Name + " without changing gameplay movement", () =>
    {
        SetUp(); var victim = AddPlayer(9); Enable(); ConfirmedDeath(victim);
        MultiRoleHandler.OpenDetectiveNotes(); var notes = DetectiveNotesMinigame.Instances.Single();
        var local = PlayerControl.LocalPlayer; local.CanMove = false;
        HudManager.Instance.OnToggleMap = () =>
        {
            invalid.Change();
            bool result = false;
            Require(!MultiRoleDetectiveHandler.AllowOwnedMapRender(local) && MultiRoleDetectiveMapRenderGuard.Prefix(local, ref result) && !result, "scope ignored a changed scene/UI guard");
        };
        HudManager.Instance.CanRenderMap = () => MultiRoleDetectiveHandler.AllowOwnedMapRender(local);
        MultiRoleDetectiveHandler.OpenCaseMap(notes);
        Require(!local.CanMove && !MultiRoleDetectiveHandler.AllowOwnedMapRender(local) && MapBehaviour.Instance is null, "blocked UI context opened map or retained rendering override");
    });

Run("owned case map is raised in front of notebook world depth and restores exact native local position", () =>
{
    SetUp(); var victim = AddPlayer(9); Enable(); ConfirmedDeath(victim);
    Camera.main.transform.localPosition = new(10, 10, -20);
    var map = HudManager.Instance.Map;
    map.transform.parent = new Transform { localPosition = new(-3, 2, 40) };
    map.transform.localPosition = new(7, 8, -15); map.transform.localScale = new(2, 3, 1);
    MultiRoleHandler.OpenDetectiveNotes(); var notes = DetectiveNotesMinigame.Instances.Single();
    MultiRoleDetectiveHandler.OpenCaseMap(notes);
    Require(map.transform.position.z <= notes.transform.position.z - 10 && map.transform.localPosition.x == 7 && map.transform.localPosition.y == 8 && map.transform.localScale.x == 2 && map.transform.localScale.y == 3, "map stayed behind notebook or changed native layout/scale");
    MultiRoleDetectiveHandler.CloseCaseMap();
    Require(!map.IsOpen && map.CloseCalls == 1 && map.transform.localPosition.x == 7 && map.transform.localPosition.y == 8 && map.transform.localPosition.z == -15, "case-map close did not restore exact parent-relative native position");
    Require(MultiRoleDetectiveHandler.NotesOpen && notes.CloseCalls == 0 && !map.HerePoint.Destroyed, "case-map close also dismissed notebook or native map marker");
});

Run("replacement-map Escape preserves unrelated UI and restores abandoned owned map depth", () =>
{
    SetUp(); var victim = AddPlayer(9); Enable(); ConfirmedDeath(victim);
    MultiRoleHandler.OpenDetectiveNotes(); var notes = DetectiveNotesMinigame.Instances.Single();
    var ownedMap = HudManager.Instance.Map; ownedMap.transform.localPosition = new(5, 6, -12);
    MultiRoleDetectiveHandler.OpenCaseMap(notes); var marker = SpriteRenderer.Instances.Single();
    var unrelated = new MapBehaviour { IsOpen = true }; unrelated.transform.localPosition = new(-6, -8, 45);
    MapBehaviour.Instance = unrelated;
    Input.Down.Add(KeyCode.Escape); MultiRoleHandler.Tick();
    Require(unrelated.IsOpen && unrelated.CloseCalls == 0 && unrelated.transform.localPosition.x == -6 && unrelated.transform.localPosition.y == -8 && unrelated.transform.localPosition.z == 45, "Escape modified or closed replacement map");
    Require(notes.CloseCalls == 0 && MultiRoleDetectiveHandler.NotesOpen && ownedMap.transform.localPosition.x == 5 && ownedMap.transform.localPosition.y == 6 && ownedMap.transform.localPosition.z == -12 && marker.gameObject.Destroyed && marker.material.Destroyed, "lost map retained owned presentation or Escape also closed notebook");
});

Run("unrelated native map closing does not restore or remove the active owned case map", () =>
{
    SetUp(); var victim = AddPlayer(9); Enable(); ConfirmedDeath(victim);
    MultiRoleHandler.OpenDetectiveNotes(); var notes = DetectiveNotesMinigame.Instances.Single();
    MultiRoleDetectiveHandler.OpenCaseMap(notes); var owned = MapBehaviour.Instance; var marker = SpriteRenderer.Instances.Single();
    float liftedDepth = owned.transform.localPosition.z;
    var unrelated = new MapBehaviour { IsOpen = true }; unrelated.Close();
    Require(owned.IsOpen && owned.CloseCalls == 0 && owned.transform.localPosition.z == liftedDepth && !marker.gameObject.Destroyed, "unrelated map-close postfix released active owned case map");
    Require(!unrelated.IsOpen && unrelated.CloseCalls == 1, "owned map scope intercepted unrelated native close outside notebook cleanup");
});

foreach (bool chat in new[] { false, true })
    Run("Escape for " + (chat ? "chat" : "mod menu") + " leaves owned case map depth and notebook intact", () =>
    {
        SetUp(); var victim = AddPlayer(9); Enable(); ConfirmedDeath(victim);
        MultiRoleHandler.OpenDetectiveNotes(); var notes = DetectiveNotesMinigame.Instances.Single();
        MultiRoleDetectiveHandler.OpenCaseMap(notes); var map = MapBehaviour.Instance;
        float liftedDepth = map.transform.localPosition.z;
        if (chat) HudManager.Instance.Chat.IsOpenOrOpening = true; else MenuUI.isGUIActive = true;
        Input.Down.Add(KeyCode.Escape); MultiRoleHandler.Tick();
        Require(map.IsOpen && map.CloseCalls == 0 && map.transform.localPosition.z == liftedDepth && notes.CloseCalls == 0, "higher-priority UI Escape also closed or restored case map");
    });

Run("failed owned map close still releases temporary depth and owned marker resources", () =>
{
    SetUp(); var victim = AddPlayer(9); Enable(); ConfirmedDeath(victim);
    MultiRoleHandler.OpenDetectiveNotes(); var notes = DetectiveNotesMinigame.Instances.Single();
    var map = HudManager.Instance.Map; map.transform.localPosition = new(4, 5, -25);
    MultiRoleDetectiveHandler.OpenCaseMap(notes); var marker = SpriteRenderer.Instances.Single();
    map.ThrowOnClose = true; MultiRoleDetectiveHandler.CloseCaseMap();
    Require(map.transform.localPosition.x == 4 && map.transform.localPosition.y == 5 && map.transform.localPosition.z == -25 && marker.gameObject.Destroyed && marker.material.Destroyed && !map.HerePoint.Destroyed, "failed close retained temporary map presentation or destroyed native resources");
    Require(MultiRoleDetectiveHandler.NotesOpen && notes.CloseCalls == 0 && MultiRoleDetectiveHandler.HasCases && Plugin.Log.Warnings.Count == 1, "failed case-map close lost notebook/cases or hid failure");
    map.ThrowOnClose = false;
}, allowWarnings: true);

Run("portrait with missing default outfit uses guarded current outfit reference without native data lookup", () =>
{
    SetUp(); var suspect = AddPlayer(2); var victim = AddPlayer(9); Enable(); ConfirmedDeath(victim);
    suspect.Data.Outfits.Clear(); suspect.CurrentOutfitType = PlayerOutfitType.Alternate;
    var alternate = new NetworkedPlayerInfo.PlayerOutfit { Owner = suspect.Data, PlayerName = "Alternate identity", ColorId = 4 };
    suspect.Data.Outfits.Add(PlayerOutfitType.Alternate, alternate);
    suspect.Data.ThrowOnDefaultOutfitLookup = true;
    var view = new DetectiveNotesSuspectInterface();
    MultiRoleDetectiveHandler.RenderSuspect(view, suspect.Data, victim.Data, 0, 3);
    Require(ReferenceEquals(view.player.RenderedOutfit, alternate) && view.player.DataUpdateCalls == 0 && view.player.OutfitUpdateCalls == 1 && view.player.gameObject.activeSelf, "missing default outfit used native indexer or lost current-outfit portrait");
    Require(view.playerName.text == "Alternate identity" && view.playerColor.text == "Color 4" && view.locationName.text == "Storage", "guarded outfit fallback lost name, color or captured evidence");
});

foreach (bool missingDictionary in new[] { false, true })
    Run("portrait with " + (missingDictionary ? "null" : "empty") + " outfit collection hides only avatar and keeps evidence", () =>
    {
        SetUp(); var suspect = AddPlayer(2); var victim = AddPlayer(9); Enable(); ConfirmedDeath(victim);
        if (missingDictionary) suspect.Data.Outfits = null; else suspect.Data.Outfits.Clear();
        suspect.Data.ThrowOnDefaultOutfitLookup = true;
        var view = new DetectiveNotesSuspectInterface();
        MultiRoleDetectiveHandler.RenderSuspect(view, suspect.Data, victim.Data, 0, 0);
        Require(!view.player.gameObject.activeSelf && view.player.OutfitUpdateCalls == 0 && view.player.DataUpdateCalls == 0, "missing outfit invoked native portrait renderer or data lookup");
        Require(view.container.activeSelf && view.playerName.text == "Player 2" && view.playerColor.text == "Unknown color" && view.locationName.text == "Storage", "missing cosmetics erased valid captured evidence");
    });

foreach (int colorId in new[] { -1, 999 })
    Run("invalid outfit color " + colorId + " cannot reach native palette indexer", () =>
    {
        SetUp(); var suspect = AddPlayer(2); var victim = AddPlayer(9); Enable(); ConfirmedDeath(victim);
        suspect.Data.Outfits[PlayerOutfitType.Default].ColorId = colorId;
        var view = new DetectiveNotesSuspectInterface();
        MultiRoleDetectiveHandler.RenderSuspect(view, suspect.Data, victim.Data, 0, 0);
        Require(view.playerColor.text == "Unknown color" && view.locationName.text == "Storage" && view.player.DataUpdateCalls == 0 && view.player.OutfitUpdateCalls == 1, "invalid palette index affected evidence or reached native data path");
    });

Console.WriteLine($"{total - failures}/{total} tests passed; actual MultiRoleHandler, MultiRoleDetectiveHandler and Detective patches linked, Unity/game objects and patch execution stubbed, no native ABI or server acceptance tested.");
return failures == 0 ? 0 : 1;

void SetUp()
{
    MultiRoleHandler.SetEnabled(false);
    Plugin.isPanicked = false;
    MenuUI.isGUIActive = false;
    Plugin.Log = new();
    Utils.isHost = false;
    Utils.isInGame = true;
    Utils.isFreePlay = false;
    Utils.isNormalGame = true;
    Utils.isMeeting = Utils.isExiling = false;
    Utils.CurrentRoom = new() { RoomId = "Storage" };
    Utils.RoomResolver = null;
    Time.unscaledTime = 0;
    Input.Down.Clear();
    ShipStatus.Instance = new();
    GameOptionsManager.Instance = new();
    PlayerControl.LocalPlayer = MakePlayer(0, true);
    PlayerControl.AllPlayerControls = [PlayerControl.LocalPlayer];
    HudManager.Instance = new();
    MapBehaviour.Instance = null;
    Camera.main = new();
    Minigame.Instance = null;
    RoleManager.Instance = new();
    RoleManager.Instance.AllRoles.Add(new ScientistRole { Role = RoleTypes.Scientist, VitalsPrefab = new() });
    var notesPrefab = new GameObject();
    notesPrefab.Components.Add(new DetectiveNotesMinigame { gameObject = notesPrefab });
    RoleManager.Instance.AllRoles.Add(new DetectiveRole { Role = RoleTypes.Detective, notesPrefab = notesPrefab });
    VitalsMinigame.Instances.Clear();
    DetectiveRole.Instances.Clear();
    DetectiveNotesMinigame.Instances.Clear();
    SpriteRenderer.Instances.Clear();
    UnityEngine.Object.DestroyCalls.Clear();
    UnityEngine.Object.ThrowOnInstantiate = false;
}
PlayerControl MakePlayer(byte id, bool owned = false)
{
    var player = new PlayerControl { PlayerId = id, AmOwner = owned };
    player.Data.PlayerName = "Player " + id;
    player.Data.Role = new RoleBehaviour { Role = RoleTypes.Crewmate };
    player.Data.RoleAssignments = 0;
    return player;
}
PlayerControl AddPlayer(byte id)
{
    var player = MakePlayer(id);
    PlayerControl.AllPlayerControls.Add(player);
    return player;
}
void Enable() { MultiRoleHandler.SetEnabled(true); MultiRoleHandler.Tick(); }
DetectiveRole DetectivePrefab() => RoleManager.Instance.AllRoles.OfType<DetectiveRole>().Single();
MultiRoleDetectiveHandler.PendingDeath ConfirmedDeath(PlayerControl victim)
{
    var pending = MultiRoleDetectiveHandler.CapturePendingDeath(victim, MurderResultFlags.Succeeded);
    Require(pending is not null, "fixture could not capture a native successful-kill candidate");
    victim.Data.IsDead = true;
    MultiRoleDetectiveHandler.ConfirmDeath(pending, victim);
    return pending;
}
void Run(string name, Action test, bool allowWarnings = false)
{
    total++;
    try
    {
        test();
        if (!allowWarnings) Require(Plugin.Log.Warnings.Count == 0, "unexpected handler warning or caught exception");
        Console.WriteLine("PASS " + name);
    }
    catch (Exception error)
    {
        failures++;
        Console.Error.WriteLine("FAIL " + name + ": " + error.Message);
    }
}
void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
