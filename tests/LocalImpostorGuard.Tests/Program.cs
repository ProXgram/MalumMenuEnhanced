using AmongUs.GameOptions;
using MalumMenu;

var tests = new (string Name, Action Run)[]
{
    ("Guest crew local-only assignment blocks its own kill", GuestCrewBlocked),
    ("All host-assigned impostor roles retain normal kill behavior", HostAssignedImpostorsAllowed),
    ("Host migration and freeplay do not intercept host-owned actions", AuthoritativeContextsAllowed),
    ("Other actors and replaced local players are not intercepted", OtherActorsAllowed),
    ("Disabled local mode restores original role and removes guard", DisableRestoresSnapshot),
    ("Failed restoration still blocks when the toggle is disabled", FailedRestoreRetainsGuard),
    ("Partial assignment failure retains original snapshot and guard", PartialAssignmentFailure),
    ("Assignment failure without a role change does not block", FailedAssignmentNoChange),
    ("External native role replacements are preserved", ExternalReplacementPreserved),
    ("Reset removes the prior round guard", ResetRemovesGuard),
    ("Round restart requires a fresh applied role", NewRoundRequiresFreshRole),
    ("Blocked kill logs once for each local assignment", LogsOncePerApplication),
    ("Guard query is read-only and emits no log", ReadOnlyGuardQuery),
    ("Destroyed or missing Unity objects do not intercept requests", MissingObjectsAllowed),
    ("No local assignment before round intro means no block", NoPrematureGuard),
    ("Inactive or disconnected clients do not intercept requests", InactiveContextsAllowed),
    ("Changing a mode flag does not erase retained local-only ownership", ModeFlagDoesNotEraseOwnership),
    ("Failed restore and reenable keep one log for the same assignment", FailedRestoreDoesNotResetLog),
    ("Actual kill-button and outbound-RPC prefixes block local-only actions", ActualPrefixesBlock),
    ("Actual prefixes preserve normal host-assigned and foreign actions", ActualPrefixesAllow),
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
    }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
return failures == 0 ? 0 : 1;

static PlayerControl SetUp(RoleTypes original = RoleTypes.Crewmate, bool apply = true)
{
    LocalImpostorHandler.Reset();
    Utils.isClient = true;
    Utils.isLobby = false;
    Utils.isNormalGame = true;
    Utils.isFreePlay = false;
    Utils.isInGame = true;
    Utils.isHost = false;
    GameOptionsManager.Instance = new GameOptionsManager();
    RoleManager.Instance = new RoleManager();
    MalumMenu.MalumMenu.Log = new TestLogger();
    ConsoleUI.Messages.Clear();
    CheatToggles.alwaysImpostor = true;
    CheatToggles.setFakeRole = false;
    PlayerPickMenu.playerpickMenu = null;
    PlayerPickMenu.customAction = null;
    var player = NewPlayer(original);
    PlayerControl.LocalPlayer = player;
    LocalImpostorHandler.OnRoundIntro();
    if (apply) LocalImpostorHandler.Tick();
    return player;
}

static PlayerControl NewPlayer(RoleTypes role) => new()
{
    Pointer = new IntPtr(Interlocked.Increment(ref PlayerIds.Next)),
    Data = new NetworkedPlayerInfo { Role = RoleManager.MakeRole(role) }
};

static void GuestCrewBlocked()
{
    foreach (var role in new[] { RoleTypes.Crewmate, RoleTypes.Engineer, RoleTypes.Scientist })
    {
        var player = SetUp(role);
        Require(player.Data.RoleType == RoleTypes.Impostor, "Handler did not apply the local presentation role.");
        Require(LocalImpostorHandler.BlocksKillRequests(player), $"Local-only assignment from {role} was not guarded.");
        Require(LocalImpostorHandler.BlockOwnKill(player), $"Own kill from {role} was accepted.");
    }
}

static void HostAssignedImpostorsAllowed()
{
    foreach (var role in new[] { RoleTypes.Impostor, RoleTypes.Shapeshifter, RoleTypes.Phantom, RoleTypes.Viper })
    {
        var player = SetUp(role, apply: false);
        var originalPointer = player.Data.Role.Pointer;
        LocalImpostorHandler.Tick();
        Require(player.Data.RoleType == role && player.Data.Role.Pointer == originalPointer, $"The host's {role} was replaced.");
        Require(RoleManager.Instance.SetRoleCalls == 0, "Handler changed a real impostor role.");
        RequireAllowed(player, $"Host-assigned {role}");
    }
}

static void AuthoritativeContextsAllowed()
{
    var player = SetUp();
    Utils.isHost = true;
    RequireAllowed(player, "Migrated host before its next Tick");
    LocalImpostorHandler.Tick();
    Require(player.Data.RoleType == RoleTypes.Crewmate, "Migration failed to restore the snapshot.");
    RequireAllowed(player, "Migrated host after Tick");
    player = SetUp();
    Utils.isFreePlay = true;
    RequireAllowed(player, "Freeplay");
}

static void OtherActorsAllowed()
{
    var local = SetUp();
    RequireAllowed(NewPlayer(RoleTypes.Impostor), "Foreign actor");
    PlayerControl.LocalPlayer = NewPlayer(RoleTypes.Crewmate);
    RequireAllowed(PlayerControl.LocalPlayer, "New local player before Tick");
    RequireAllowed(local, "Old local player after ownership changed");
    LocalImpostorHandler.Tick();
    Require(LocalImpostorHandler.BlocksKillRequests(PlayerControl.LocalPlayer), "New local assignment was not guarded.");
    RequireAllowed(local, "Previous local player after new assignment");
}

static void DisableRestoresSnapshot()
{
    var player = SetUp(RoleTypes.Engineer);
    CheatToggles.alwaysImpostor = false;
    Require(LocalImpostorHandler.BlocksKillRequests(player), "Guard disappeared before role restoration.");
    LocalImpostorHandler.Tick();
    Require(player.Data.RoleType == RoleTypes.Engineer, "Disabled setting failed to restore Engineer.");
    RequireAllowed(player, "Restored role");
}

static void FailedRestoreRetainsGuard()
{
    foreach (var silentFailure in new[] { false, true })
    {
        var player = SetUp(RoleTypes.Engineer);
        if (silentFailure) RoleManager.Instance.IgnoreRole = RoleTypes.Engineer;
        else RoleManager.Instance.ThrowBeforeRole = RoleTypes.Engineer;
        CheatToggles.alwaysImpostor = false;
        LocalImpostorHandler.Tick();
        Require(player.Data.RoleType == RoleTypes.Impostor, "The restore failure did not leave the applied role.");
        Require(LocalImpostorHandler.BlocksKillRequests(player), "Disabled toggle bypassed the retained local-only guard.");
        Require(LocalImpostorHandler.BlockOwnKill(player), "Failed restoration allowed a normal kill.");
        var calls = RoleManager.Instance.SetRoleCalls;
        LocalImpostorHandler.Tick();
        Require(RoleManager.Instance.SetRoleCalls == calls, "Failed restore retried every frame.");
        RoleManager.Instance.ThrowBeforeRole = null;
        RoleManager.Instance.IgnoreRole = null;
        CheatToggles.alwaysImpostor = true;
        LocalImpostorHandler.Tick();
        CheatToggles.alwaysImpostor = false;
        LocalImpostorHandler.Tick();
        Require(player.Data.RoleType == RoleTypes.Engineer, "Later successful retry lost the original snapshot.");
        RequireAllowed(player, "Recovered restoration");
    }
}

static void PartialAssignmentFailure()
{
    var player = SetUp(RoleTypes.Scientist, apply: false);
    RoleManager.Instance.ThrowAfterRole = RoleTypes.Impostor;
    LocalImpostorHandler.Tick();
    Require(LocalImpostorHandler.BlocksKillRequests(player), "Partial native assignment escaped the guard.");
    Require(LocalImpostorHandler.BlockOwnKill(player), "Partial native assignment allowed a kill.");
    CheatToggles.alwaysImpostor = false;
    LocalImpostorHandler.Tick();
    Require(player.Data.RoleType == RoleTypes.Scientist, "Partial assignment lost the original snapshot.");
    RequireAllowed(player, "Restored partial assignment");
}

static void FailedAssignmentNoChange()
{
    var player = SetUp(apply: false);
    RoleManager.Instance.ThrowBeforeRole = RoleTypes.Impostor;
    LocalImpostorHandler.Tick();
    Require(player.Data.RoleType == RoleTypes.Crewmate, "Assignment failure changed the original role.");
    RequireAllowed(player, "Assignment failure without role change");
}

static void ExternalReplacementPreserved()
{
    foreach (var role in new[] { RoleTypes.Engineer, RoleTypes.Impostor, RoleTypes.Shapeshifter })
    {
        var player = SetUp();
        player.Data.Role = RoleManager.MakeRole(role);
        var replacedPointer = player.Data.Role.Pointer;
        RequireAllowed(player, $"External replacement {role} before Tick");
        CheatToggles.alwaysImpostor = false;
        LocalImpostorHandler.Tick();
        Require(player.Data.Role.Pointer == replacedPointer && player.Data.RoleType == role, "Restore overwrote an external role replacement.");
        RequireAllowed(player, $"External replacement {role} after Tick");
    }
}

static void ResetRemovesGuard()
{
    var player = SetUp();
    LocalImpostorHandler.Reset();
    RequireAllowed(player, "Reset round");
    Require(RoleManager.Instance.SetRoleCalls == 1, "Reset should not reassign the native role.");
}

static void NewRoundRequiresFreshRole()
{
    var player = SetUp();
    LocalImpostorHandler.OnRoundIntro();
    RequireAllowed(player, "New round before native assignment");
    player.Data.Role = RoleManager.MakeRole(RoleTypes.Engineer);
    LocalImpostorHandler.Tick();
    Require(LocalImpostorHandler.BlocksKillRequests(player), "Newly applied role in the new round was not guarded.");
}

static void LogsOncePerApplication()
{
    var player = SetUp();
    var baseline = MalumMenu.MalumMenu.Log.Entries.Count();
    Require(LocalImpostorHandler.BlockOwnKill(player), "First guarded kill passed.");
    var afterFirst = MalumMenu.MalumMenu.Log.Entries.Count();
    Require(afterFirst == baseline + 1, "First blocked action did not emit exactly one log.");
    Require(ConsoleUI.Messages.Count == 1, "First blocked action did not emit one visible console message.");
    for (var request = 0; request < 100; request++)
        Require(LocalImpostorHandler.BlockOwnKill(player), "Repeated guarded kill passed.");
    Require(MalumMenu.MalumMenu.Log.Entries.Count() == afterFirst, "Repeated blocked actions spammed the log.");
    Require(ConsoleUI.Messages.Count == 1, "Repeated blocked actions spammed the visible console.");
    CheatToggles.alwaysImpostor = false;
    LocalImpostorHandler.Tick();
    CheatToggles.alwaysImpostor = true;
    LocalImpostorHandler.Tick();
    baseline = MalumMenu.MalumMenu.Log.Entries.Count();
    Require(LocalImpostorHandler.BlockOwnKill(player), "Reapplied local assignment was unguarded.");
    Require(MalumMenu.MalumMenu.Log.Entries.Count() == baseline + 1, "New local assignment did not get its own first blocked action log.");
}

static void ReadOnlyGuardQuery()
{
    var player = SetUp();
    var pointer = player.Data.Role.Pointer;
    var logs = MalumMenu.MalumMenu.Log.Entries.Count();
    var roleCalls = RoleManager.Instance.SetRoleCalls;
    for (var query = 0; query < 100; query++)
        Require(LocalImpostorHandler.BlocksKillRequests(player), "Query failed for a retained assignment.");
    Require(MalumMenu.MalumMenu.Log.Entries.Count() == logs, "Read-only guard query emitted a log.");
    Require(player.Data.Role.Pointer == pointer && RoleManager.Instance.SetRoleCalls == roleCalls, "Read-only guard query mutated the role.");
}

static void MissingObjectsAllowed()
{
    var player = SetUp();
    RequireAllowed(null, "Missing actor");
    player.Destroyed = true;
    RequireAllowed(player, "Destroyed actor");
    player = SetUp();
    player.Data = null;
    RequireAllowed(player, "Missing player data");
    player = SetUp();
    player.Data.Role.Destroyed = true;
    RequireAllowed(player, "Destroyed role");
    player = SetUp();
    player.Data.Role = null;
    RequireAllowed(player, "Missing role");
}

static void NoPrematureGuard()
{
    var player = SetUp(apply: false);
    RequireAllowed(player, "Before any local assignment");
    LocalImpostorHandler.Reset();
    LocalImpostorHandler.Tick();
    RequireAllowed(player, "Round intro not received");
    Require(RoleManager.Instance.SetRoleCalls == 0, "Role was assigned before intro.");
}

static void InactiveContextsAllowed()
{
    var player = SetUp();
    Utils.isInGame = false;
    RequireAllowed(player, "Inactive round");
    player = SetUp();
    Utils.isClient = false;
    RequireAllowed(player, "Disconnected client");
}

static void ModeFlagDoesNotEraseOwnership()
{
    var player = SetUp();
    Utils.isNormalGame = false;
    Require(LocalImpostorHandler.BlocksKillRequests(player), "A mode flag erased the retained local-only assignment guard.");
    LocalImpostorHandler.Reset();
    RequireAllowed(player, "Reset after mode change");
}

static void FailedRestoreDoesNotResetLog()
{
    var player = SetUp();
    Require(LocalImpostorHandler.BlockOwnKill(player), "Initial block failed.");
    RoleManager.Instance.ThrowBeforeRole = RoleTypes.Crewmate;
    CheatToggles.alwaysImpostor = false;
    LocalImpostorHandler.Tick();
    CheatToggles.alwaysImpostor = true;
    LocalImpostorHandler.Tick();
    Require(LocalImpostorHandler.BlockOwnKill(player), "Reenabled retained assignment was unguarded.");
    Require(MalumMenu.MalumMenu.Log.Warning.Count == 1, "A toggle reset logging for the same applied role.");
    Require(ConsoleUI.Messages.Count == 1, "A toggle repeated the visible console warning for the same role.");
}

static void ActualPrefixesBlock()
{
    var player = SetUp();
    var buttonOriginalCalls = 0;
    var rpcOriginalCalls = 0;
    // A false Harmony prefix suppresses the original method. Exercise the
    // production prefixes directly; no game or network method is invoked.
    for (var click = 0; click < 100; click++)
    {
        if (KillButton_DoClick.Prefix()) buttonOriginalCalls++;
        if (PlayerControl_RpcMurderPlayer.Prefix(player)) rpcOriginalCalls++;
    }
    Require(buttonOriginalCalls == 0, "Kill-button prefix permitted the normal action.");
    Require(rpcOriginalCalls == 0, "Outbound RPC prefix permitted the normal send.");
    Require(MalumMenu.MalumMenu.Log.Entries.Count() == 2, "Repeated prefixes should have one application log and one blocked-action log.");
    RoleManager.Instance.ThrowBeforeRole = RoleTypes.Crewmate;
    CheatToggles.alwaysImpostor = false;
    LocalImpostorHandler.Tick();
    Require(!KillButton_DoClick.Prefix(), "Toggle disable bypassed the button after failed restoration.");
    Require(!PlayerControl_RpcMurderPlayer.Prefix(player), "Toggle disable bypassed the outbound guard after failed restoration.");
}

static void ActualPrefixesAllow()
{
    foreach (var role in new[] { RoleTypes.Impostor, RoleTypes.Shapeshifter, RoleTypes.Phantom, RoleTypes.Viper })
    {
        var player = SetUp(role);
        Require(KillButton_DoClick.Prefix(), $"Button prefix blocked the host's {role}.");
        Require(PlayerControl_RpcMurderPlayer.Prefix(player), $"Outbound prefix blocked the host's {role}.");
    }
    SetUp();
    Require(PlayerControl_RpcMurderPlayer.Prefix(NewPlayer(RoleTypes.Impostor)), "Outbound prefix intercepted another player.");
    var local = PlayerControl.LocalPlayer;
    Utils.isHost = true;
    Require(KillButton_DoClick.Prefix(), "Button prefix intercepted the host.");
    Require(PlayerControl_RpcMurderPlayer.Prefix(local), "Outbound prefix intercepted the host.");
}

static void RequireAllowed(PlayerControl player, string context)
{
    Require(!LocalImpostorHandler.BlocksKillRequests(player), $"{context} was rejected by the query.");
    var logs = MalumMenu.MalumMenu.Log.Entries.Count();
    Require(!LocalImpostorHandler.BlockOwnKill(player), $"{context} was rejected by the action helper.");
    Require(MalumMenu.MalumMenu.Log.Entries.Count() == logs, $"Allowed {context} emitted a blocked-action log.");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static class PlayerIds
{
    public static long Next = 100;
}
