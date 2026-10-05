using AmongUs.GameOptions;
using MalumMenu;

var tests = new (string Name, Action Run)[]
{
    ("Request records the own player and Judge target", OwnJudgeRequest),
    ("Binding, refresh, and GUI getters never send requests", ObservationNeverSends),
    ("Attempt is consumed before entering the native callback", ConsumeBeforeCall),
    ("Throwing native call consumes the round's attempt", ThrowConsumesAttempt),
    ("Cancel and rebind cannot create another round attempt", CancelRebindNoRetry),
    ("Only a genuine lifecycle reset permits a new attempt", ResetAllowsNewRound),
    ("Unbound requests are rejected without consuming an attempt", UnboundRejected),
    ("Assigned Judge, Impostor, and fake-role settings are rejected", RoleGuards),
    ("Blocked role eligibility can recover without losing its attempt", EligibilityRecovery),
    ("Binding cancels when game, host, visibility, player, or ship changes", BindingIdentityChanges),
    ("Identity change and rebind cannot reset a used attempt", IdentityChangeNoRetry),
    ("All inactive, unowned, and unavailable gameplay contexts reject", GameplayGuards),
    ("Failed ready-context gates do not consume an attempt", ReadinessRecovery),
    ("Judge experiment disables every manual kill action", KillDisabled),
    ("Sent status does not claim remote acceptance", SentIsNotRemoteSuccess),
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Require(Calls.KillRequests == 0, "Judge test called a kill RPC.");
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

static PlayerControl SetUp(bool bind = true)
{
    GuestRoundTest.Reset();
    Utils.isOnlineGame = true;
    Utils.isInGame = true;
    Utils.isHost = false;
    Utils.isNormalGame = true;
    Utils.isMeeting = false;
    Utils.isExiling = false;
    CheatToggles.alwaysImpostor = false;
    CheatToggles.setFakeRole = false;
    CheatToggles.killReach = false;
    CheatToggles.killAnyone = false;
    CheatToggles.killVanished = false;
    CheatToggles.noKillCd = false;
    Calls.RoleRequests.Clear();
    Calls.OnRoleRequest = null;
    Calls.KillRequests = 0;
    MalumMenu.MalumMenu.Log = new();
    AmongUsClient.Instance = new() { Pointer = NextPointer(), GameId = 123, HostId = 20, IsGamePublic = false };
    ShipStatus.Instance = new() { Pointer = NextPointer() };
    HudManager.Instance = new() { Pointer = NextPointer() };
    GameOptionsManager.Instance = new() { CurrentGameOptions = new() };
    var local = NewPlayer(1, RoleTypes.Crewmate);
    PlayerControl.LocalPlayer = local;
    GameData.Instance = new()
    {
        Pointer = NextPointer(),
        AllPlayers = [local.Data, NewPlayer(2, RoleTypes.Crewmate).Data,
            NewPlayer(3, RoleTypes.Crewmate).Data, NewPlayer(4, RoleTypes.Crewmate).Data,
            NewPlayer(5, RoleTypes.Crewmate).Data, NewPlayer(6, RoleTypes.Crewmate).Data,
            NewPlayer(7, RoleTypes.Impostor).Data]
    };
    if (bind) GuestRoundTest.BindCurrentRound();
    return local;
}

static PlayerControl NewPlayer(byte id, RoleTypes role) => new()
{
    Pointer = NextPointer(),
    PlayerId = id,
    Data = new() { PlayerId = id, OwnerId = 10, Role = NewRole(role) }
};

static RoleBehaviour NewRole(RoleTypes role) => new()
{
    Pointer = NextPointer(), Role = role,
    TeamType = role == RoleTypes.Impostor ? RoleTeamTypes.Impostor : RoleTeamTypes.Crewmate
};

static IntPtr NextPointer() => new(Interlocked.Increment(ref Identities.Next));

static void OwnJudgeRequest()
{
    var local = SetUp();
    Require(GuestRoundTest.IsBound && GuestRoundTest.RoleRequestBlockReason == "", "Valid guest context cannot request.");
    GuestRoundTest.SendOwnRoleRequestOnce();
    Require(Calls.RoleRequests.Count == 1, "Expected exactly one typed role request.");
    var call = Calls.RoleRequests[0];
    Require(ReferenceEquals(call.Actor, local) && call.Actor.Pointer == local.Pointer && call.Actor.PlayerId == local.PlayerId,
        "Request targeted a different player.");
    Require(call.Role == RoleTypes.Judge && call.CanOverride, "Request used the wrong target role or native argument.");
    GuestRoundTest.SendOwnRoleRequestOnce();
    Require(Calls.RoleRequests.Count == 1, "A second manual click sent another request.");
}

static void ObservationNeverSends()
{
    SetUp(bind: false);
    for (var iteration = 0; iteration < 50; iteration++)
    {
        GuestRoundTest.Refresh();
        _ = GuestRoundTest.StatusText;
        _ = GuestRoundTest.RoleRequestStatus;
        _ = GuestRoundTest.KillRequestStatus;
        _ = GuestRoundTest.CanBind;
        _ = GuestRoundTest.IsBound;
        _ = GuestRoundTest.RoleRequestBlockReason;
        _ = GuestRoundTest.KillRequestBlockReason;
        GuestRoundTest.BindCurrentRound();
    }
    Require(Calls.RoleRequests.Count == 0, "Observation or binding automatically sent a role request.");
}

static void ConsumeBeforeCall()
{
    SetUp();
    var consumedAtCall = false;
    Calls.OnRoleRequest = () =>
    {
        consumedAtCall = GuestRoundTest.RoleRequestBlockReason.Contains("already used", StringComparison.OrdinalIgnoreCase);
        GuestRoundTest.SendOwnRoleRequestOnce();
    };
    GuestRoundTest.SendOwnRoleRequestOnce();
    Require(consumedAtCall, "Attempt was still available when entering native work.");
    Require(Calls.RoleRequests.Count == 1, "Synchronous callback reentered a second native request.");
}

static void ThrowConsumesAttempt()
{
    SetUp();
    Calls.OnRoleRequest = () => throw new InvalidOperationException("Simulated native request failure.");
    GuestRoundTest.SendOwnRoleRequestOnce();
    Require(Calls.RoleRequests.Count == 1, "Throwing call did not enter native work exactly once.");
    Require(GuestRoundTest.RoleRequestStatus.Contains("attempt used", StringComparison.OrdinalIgnoreCase), "Throw did not report the consumed attempt.");
    Calls.OnRoleRequest = null;
    GuestRoundTest.SendOwnRoleRequestOnce();
    GuestRoundTest.CancelBinding();
    GuestRoundTest.BindCurrentRound();
    GuestRoundTest.SendOwnRoleRequestOnce();
    Require(Calls.RoleRequests.Count == 1, "Throwing native call was retried in the same round.");
}

static void CancelRebindNoRetry()
{
    SetUp();
    GuestRoundTest.SendOwnRoleRequestOnce();
    for (var rebind = 0; rebind < 10; rebind++)
    {
        GuestRoundTest.CancelBinding();
        GuestRoundTest.BindCurrentRound();
        GuestRoundTest.SendOwnRoleRequestOnce();
    }
    Require(Calls.RoleRequests.Count == 1, "Cancel/rebind reset this round's attempt.");
}

static void ResetAllowsNewRound()
{
    SetUp();
    GuestRoundTest.SendOwnRoleRequestOnce();
    GuestRoundTest.Reset();
    Require(!GuestRoundTest.IsBound, "Lifecycle reset retained binding.");
    GuestRoundTest.BindCurrentRound();
    GuestRoundTest.SendOwnRoleRequestOnce();
    Require(Calls.RoleRequests.Count == 2, "Genuine lifecycle reset failed to permit one new attempt.");
}

static void UnboundRejected()
{
    SetUp(bind: false);
    GuestRoundTest.SendOwnRoleRequestOnce();
    Require(Calls.RoleRequests.Count == 0, "An unbound round sent a request.");
    GuestRoundTest.BindCurrentRound();
    GuestRoundTest.SendOwnRoleRequestOnce();
    Require(Calls.RoleRequests.Count == 1, "An unbound rejection consumed the attempt.");
}

static void RoleGuards()
{
    foreach (var role in new[] { RoleTypes.Judge, RoleTypes.Impostor })
    {
        var local = SetUp();
        local.Data.Role = NewRole(role);
        Require(GuestRoundTest.RoleRequestBlockReason != "", $"Current {role} was eligible.");
        GuestRoundTest.SendOwnRoleRequestOnce();
        Require(Calls.RoleRequests.Count == 0, $"Current {role} sent a request.");
    }
    SetUp();
    CheatToggles.alwaysImpostor = true;
    GuestRoundTest.SendOwnRoleRequestOnce();
    Require(Calls.RoleRequests.Count == 0, "Always Impostor did not block the Judge request.");
    SetUp();
    CheatToggles.setFakeRole = true;
    GuestRoundTest.SendOwnRoleRequestOnce();
    Require(Calls.RoleRequests.Count == 0, "Set Fake Role did not block the Judge request.");
}

static void EligibilityRecovery()
{
    var local = SetUp();
    local.Data.Role = NewRole(RoleTypes.Judge);
    GuestRoundTest.SendOwnRoleRequestOnce();
    local.Data.Role = NewRole(RoleTypes.Engineer);
    CheatToggles.alwaysImpostor = true;
    GuestRoundTest.SendOwnRoleRequestOnce();
    CheatToggles.alwaysImpostor = false;
    CheatToggles.setFakeRole = true;
    GuestRoundTest.SendOwnRoleRequestOnce();
    CheatToggles.setFakeRole = false;
    GuestRoundTest.SendOwnRoleRequestOnce();
    Require(Calls.RoleRequests.Count == 1, "Temporary role restrictions consumed an attempt.");
}

static void BindingIdentityChanges()
{
    foreach (var change in IdentityChanges())
    {
        SetUp();
        change.Change();
        GuestRoundTest.Refresh();
        Require(!GuestRoundTest.IsBound, $"Changed {change.Name} retained binding.");
        GuestRoundTest.SendOwnRoleRequestOnce();
        Require(Calls.RoleRequests.Count == 0, $"Changed {change.Name} sent without rebind.");
    }
}

static void IdentityChangeNoRetry()
{
    foreach (var change in IdentityChanges())
    {
        SetUp();
        GuestRoundTest.SendOwnRoleRequestOnce();
        change.Change();
        GuestRoundTest.Refresh();
        GuestRoundTest.BindCurrentRound();
        GuestRoundTest.SendOwnRoleRequestOnce();
        Require(Calls.RoleRequests.Count == 1, $"Changed {change.Name} and rebind reset used attempt.");
    }
}

static (string Name, Action Change)[] IdentityChanges() =>
[
    ("game", () => AmongUsClient.Instance.GameId++),
    ("host", () => AmongUsClient.Instance.HostId++),
    ("visibility", () => AmongUsClient.Instance.IsGamePublic = true),
    ("player", () => PlayerControl.LocalPlayer = NewPlayer(1, RoleTypes.Crewmate)),
    ("ship", () => ShipStatus.Instance = new() { Pointer = NextPointer() })
];

static void GameplayGuards()
{
    var unavailable = new (string Name, Action Change)[]
    {
        ("missing client", () => AmongUsClient.Instance = null),
        ("offline", () => Utils.isOnlineGame = false),
        ("inactive round", () => Utils.isInGame = false),
        ("host", () => Utils.isHost = true),
        ("non-Normal mode", () => Utils.isNormalGame = false),
        ("missing options manager", () => GameOptionsManager.Instance = null),
        ("missing current options", () => GameOptionsManager.Instance.CurrentGameOptions = null),
        ("missing player", () => PlayerControl.LocalPlayer = null),
        ("destroyed player", () => PlayerControl.LocalPlayer.Destroyed = true),
        ("missing ship", () => ShipStatus.Instance = null),
        ("unowned player", () => PlayerControl.LocalPlayer.AmOwner = false),
        ("missing player data", () => PlayerControl.LocalPlayer.Data = null),
        ("missing role", () => PlayerControl.LocalPlayer.Data.Role = null),
        ("dead player", () => PlayerControl.LocalPlayer.Data.IsDead = true),
        ("disconnected player", () => PlayerControl.LocalPlayer.Data.Disconnected = true),
        ("missing HUD", () => HudManager.Instance = null),
        ("intro", () => HudManager.Instance.IsIntroDisplayed = true),
        ("meeting", () => Utils.isMeeting = true),
        ("exile", () => Utils.isExiling = true),
        ("unable to move", () => PlayerControl.LocalPlayer.CanMove = false)
    };
    foreach (var context in unavailable)
    {
        SetUp();
        context.Change();
        Require(GuestRoundTest.RoleRequestBlockReason != "", $"{context.Name} had no block reason.");
        GuestRoundTest.SendOwnRoleRequestOnce();
        Require(Calls.RoleRequests.Count == 0, $"{context.Name} sent a native request.");
        Require(MalumMenu.MalumMenu.Log.Errors.Count == 0, $"{context.Name} threw instead of rejecting safely.");
    }
}

static void ReadinessRecovery()
{
    var local = SetUp();
    Utils.isMeeting = true;
    GuestRoundTest.SendOwnRoleRequestOnce();
    Utils.isMeeting = false;
    local.CanMove = false;
    GuestRoundTest.SendOwnRoleRequestOnce();
    local.CanMove = true;
    GuestRoundTest.SendOwnRoleRequestOnce();
    Require(Calls.RoleRequests.Count == 1, "Temporarily paused gameplay consumed an attempt.");
}

static void KillDisabled()
{
    foreach (var role in new[] { RoleTypes.Crewmate, RoleTypes.Judge, RoleTypes.Impostor })
    {
        var local = SetUp();
        local.Data.Role = NewRole(role);
        Require(GuestRoundTest.KillRequestBlockReason != "", $"{role} had an enabled kill action.");
        for (var click = 0; click < 20; click++) GuestRoundTest.SendOwnKillTestOnce();
        Require(Calls.KillRequests == 0, $"{role} sent a kill action.");
        Require(HudManager.Instance.KillButtonReads == 0, "Judge build inspected a native kill target.");
        Require(GuestRoundTest.KillRequestStatus.Contains("disabled", StringComparison.OrdinalIgnoreCase), "Disabled kill status was not explicit.");
    }
}

static void SentIsNotRemoteSuccess()
{
    SetUp();
    GuestRoundTest.SendOwnRoleRequestOnce();
    Require(GuestRoundTest.RoleRequestStatus.Contains("SENT", StringComparison.OrdinalIgnoreCase), "Status did not record that the native call returned.");
    Require(GuestRoundTest.RoleRequestStatus.Contains("unverified", StringComparison.OrdinalIgnoreCase), "Sent status claimed confirmed remote acceptance.");
    Require(GuestRoundTest.StatusText.Contains("unverified", StringComparison.OrdinalIgnoreCase), "Bound status claimed remote success.");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static class Identities { public static long Next = 100; }
