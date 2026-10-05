using AmongUs.GameOptions;
using MalumMenu;

var tests = new (string Name, Action Run)[]
{
    ("Practice Judge rearms a spent use at the next meeting", SpentUseRearms),
    ("Unused native charge is preserved when disabled", NativeChargePreserved),
    ("Disabled setting cannot grant a use", DisabledCannotGrant),
    ("Practice captures its Judge while disabled for later enable", EnableAfterIntro),
    ("Tick never refills or bypasses native consumption", TickDoesNotRefill),
    ("Native consume patch never itself grants another use", ConsumeDoesNotRefill),
    ("Disable removes only an unused feature-granted use", DisableRemovesGrant),
    ("Consumed grant stays spent when disabled", ConsumedGrantDisable),
    ("Disable then reenable can grant at a later meeting", ReenableAtLaterMeeting),
    ("Online crew changed to Judge cannot receive a refill", FakeJudgeRejected),
    ("Stale practice-role callbacks cannot affect a replacement", RoleReplacementInvalidates),
    ("Stale practice-player callbacks cannot affect a replacement", PlayerReplacementInvalidates),
    ("Death invalidates even if alive flag is later restored", DeathInvalidates),
    ("Disconnect invalidates even if connected flag returns", DisconnectInvalidates),
    ("Practice death is monitored while setting is disabled", DisabledStillInvalidates),
    ("Foreign meeting and consume callbacks cannot alter the local grant", ForeignCallbacksIgnored),
    ("Reset preserves an original native charge", ResetDoesNotEditRoles),
    ("Practice reset can bind a new owned Judge", NewRoundRebinds),
    ("Unavailable and unowned practice contexts cannot refill", UnavailableIntroRejected),
    ("Normal Freeplay and a connected owner are required for refill", GameplayGates),
    ("Meeting refill leaves native meeting lock and task gate unchanged", StateSentinelsPreserved),
    ("Freeplay captures practice Judge without a round intro", FreeplayCapture),
    ("Freeplay follows legitimate practice role replacements", FreeplayRoleReplacement),
    ("Freeplay rejects non-Judge and dead practice contexts", FreeplayInvalidContexts),
    ("Practice death invalidation survives alive flag restoration", FreeplayDeathInvalidates),
    ("Leaving practice clears its captured identity", LeavingFreeplayClearsBinding),
    ("Online guest's native initial use is preserved and never refilled", OnlineGuestNeverRefills),
    ("Online host's native initial use is preserved and never refilled", OnlineHostNeverRefills),
    ("Leaving practice removes only an unused feature-granted charge", ExitPracticeClearsGrantedUse),
    ("Leaving practice preserves original native and refunded charges", ExitPracticePreservesNativeUse),
    ("Native renewed use is preserved after feature use was consumed", NativeRenewalPreserved),
    ("Actual meeting prefix preserves the native once-per-meeting lock", ActualMeetingPatch),
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Require(NetworkActions.Attempts == 0, "Feature attempted a network action.");
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

static JudgeRole SetUp(bool charge = false, bool enabled = true, bool intro = true, bool freeplay = true)
{
    InfiniteJudgeHandler.Reset();
    Utils.isClient = true;
    Utils.isLobby = false;
    Utils.isNormalGame = true;
    Utils.isFreePlay = freeplay;
    Utils.isInGame = !freeplay;
    Utils.isHost = false;
    CheatToggles.infiniteJudge = enabled;
    GameOptionsManager.Instance = new();
    MalumMenu.MalumMenu.Log = new();
    ConsoleUI.Messages.Clear();
    MeetingHud.Instance = null;
    NetworkActions.Attempts = 0;
    var role = NewJudge(charge);
    PlayerControl.LocalPlayer = NewPlayer(role);
    if (intro)
    {
        if (freeplay) InfiniteJudgeHandler.Tick();
        else InfiniteJudgeHandler.OnRoundIntro();
    }
    return role;
}

static JudgeRole NewJudge(bool charge = false) => new()
{
    Pointer = NextPointer(),
    Role = RoleTypes.Judge,
    TeamType = RoleTeamTypes.Crewmate,
    Player = PlayerControl.LocalPlayer,
    HasAnOverruleUse = charge
};

static RoleBehaviour NewCrew() => new()
{
    Pointer = NextPointer(), Role = RoleTypes.Crewmate, TeamType = RoleTeamTypes.Crewmate
};

static PlayerControl NewPlayer(RoleBehaviour role)
{
    var player = new PlayerControl { Pointer = NextPointer(), Data = new() { Role = role } };
    role.Player = player;
    return player;
}

static IntPtr NextPointer() => new(Interlocked.Increment(ref Identities.Next));

static void SpentUseRearms()
{
    var role = SetUp();
    InfiniteJudgeHandler.RefillForMeeting(role);
    Require(role.HasAnOverruleUse, "Captured practice Judge did not receive the next-meeting use.");
    role.ConsumeOverruleVotesUsage();
    InfiniteJudgeHandler.OnUseConsumed(role);
    Require(!role.HasAnOverruleUse, "Consume callback rearmed the charge.");
    InfiniteJudgeHandler.RefillForMeeting(role);
    Require(role.HasAnOverruleUse, "A later meeting failed to rearm the spent use.");
}

static void NativeChargePreserved()
{
    var role = SetUp(charge: true);
    InfiniteJudgeHandler.RefillForMeeting(role);
    CheatToggles.infiniteJudge = false;
    InfiniteJudgeHandler.Tick();
    Require(role.HasAnOverruleUse, "Disable removed an original native charge.");
}

static void DisabledCannotGrant()
{
    var role = SetUp(enabled: false);
    InfiniteJudgeHandler.RefillForMeeting(role);
    InfiniteJudgeHandler.Tick();
    Require(!role.HasAnOverruleUse, "Disabled setting granted a charge.");
}

static void EnableAfterIntro()
{
    var role = SetUp(enabled: false);
    InfiniteJudgeHandler.Tick();
    CheatToggles.infiniteJudge = true;
    InfiniteJudgeHandler.Tick();
    Require(!role.HasAnOverruleUse, "Enable granted before a meeting.");
    InfiniteJudgeHandler.RefillForMeeting(role);
    Require(role.HasAnOverruleUse, "Practice did not preserve Judge identity while disabled.");
}

static void TickDoesNotRefill()
{
    var role = SetUp();
    for (var tick = 0; tick < 100; tick++) InfiniteJudgeHandler.Tick();
    Require(!role.HasAnOverruleUse, "Tick granted a use before a meeting.");
    InfiniteJudgeHandler.RefillForMeeting(role);
    role.ConsumeOverruleVotesUsage();
    InfiniteJudgeHandler.OnUseConsumed(role);
    for (var tick = 0; tick < 100; tick++) InfiniteJudgeHandler.Tick();
    Require(!role.HasAnOverruleUse, "Tick rearmed after the native consume.");
}

static void ConsumeDoesNotRefill()
{
    var role = SetUp(charge: true);
    role.ConsumeOverruleVotesUsage();
    JudgeRole_ConsumeOverruleVotesUsage_InfiniteJudge.Postfix(role);
    Require(!role.HasAnOverruleUse, "Actual consume postfix refilled an original use.");
    InfiniteJudgeHandler.RefillForMeeting(role);
    role.ConsumeOverruleVotesUsage();
    JudgeRole_ConsumeOverruleVotesUsage_InfiniteJudge.Postfix(role);
    Require(!role.HasAnOverruleUse, "Actual consume postfix refilled a granted use.");
}

static void DisableRemovesGrant()
{
    var role = SetUp();
    InfiniteJudgeHandler.RefillForMeeting(role);
    CheatToggles.infiniteJudge = false;
    InfiniteJudgeHandler.Tick();
    Require(!role.HasAnOverruleUse, "Disable retained the unused feature-granted charge.");
}

static void ConsumedGrantDisable()
{
    var role = SetUp();
    InfiniteJudgeHandler.RefillForMeeting(role);
    role.ConsumeOverruleVotesUsage();
    InfiniteJudgeHandler.OnUseConsumed(role);
    CheatToggles.infiniteJudge = false;
    InfiniteJudgeHandler.Tick();
    Require(!role.HasAnOverruleUse, "Disable restored a charge the native method had consumed.");
}

static void ReenableAtLaterMeeting()
{
    var role = SetUp();
    InfiniteJudgeHandler.RefillForMeeting(role);
    CheatToggles.infiniteJudge = false;
    InfiniteJudgeHandler.Tick();
    CheatToggles.infiniteJudge = true;
    InfiniteJudgeHandler.Tick();
    Require(!role.HasAnOverruleUse, "Reenable granted a charge before native meeting start.");
    InfiniteJudgeHandler.RefillForMeeting(role);
    Require(role.HasAnOverruleUse, "Reenable failed to preserve captured role identity.");
}

static void FakeJudgeRejected()
{
    SetUp(intro: false, freeplay: false);
    PlayerControl.LocalPlayer.Data.Role = NewCrew();
    InfiniteJudgeHandler.OnRoundIntro();
    var fake = NewJudge();
    PlayerControl.LocalPlayer.Data.Role = fake;
    InfiniteJudgeHandler.Tick();
    InfiniteJudgeHandler.RefillForMeeting(fake);
    Require(!fake.HasAnOverruleUse, "A Judge created in an online round was refilled.");
}

static void RoleReplacementInvalidates()
{
    var original = SetUp();
    var replacement = NewJudge();
    PlayerControl.LocalPlayer.Data.Role = replacement;
    InfiniteJudgeHandler.Tick();
    InfiniteJudgeHandler.RefillForMeeting(original);
    Require(!original.HasAnOverruleUse && !replacement.HasAnOverruleUse, "Stale role callback granted to an old or current role.");
    InfiniteJudgeHandler.RefillForMeeting(replacement);
    Require(replacement.HasAnOverruleUse, "Current owned practice replacement was not eligible.");
}

static void PlayerReplacementInvalidates()
{
    var original = SetUp();
    var originalPlayer = PlayerControl.LocalPlayer;
    var replacement = NewJudge();
    PlayerControl.LocalPlayer = NewPlayer(replacement);
    InfiniteJudgeHandler.Tick();
    InfiniteJudgeHandler.RefillForMeeting(original);
    Require(!original.HasAnOverruleUse && !replacement.HasAnOverruleUse, "Stale player callback granted a charge.");
    Require(original.Player.Pointer == originalPlayer.Pointer, "Fixture lost original role ownership.");
    InfiniteJudgeHandler.RefillForMeeting(replacement);
    Require(replacement.HasAnOverruleUse, "Current owned practice player was not eligible.");
}

static void DeathInvalidates()
{
    var role = SetUp();
    PlayerControl.LocalPlayer.Data.IsDead = true;
    InfiniteJudgeHandler.Tick();
    PlayerControl.LocalPlayer.Data.IsDead = false;
    InfiniteJudgeHandler.Tick();
    InfiniteJudgeHandler.RefillForMeeting(role);
    Require(!role.HasAnOverruleUse, "Changing the alive flag revived captured Judge eligibility.");
}

static void DisconnectInvalidates()
{
    var role = SetUp();
    PlayerControl.LocalPlayer.Data.Disconnected = true;
    InfiniteJudgeHandler.Tick();
    PlayerControl.LocalPlayer.Data.Disconnected = false;
    InfiniteJudgeHandler.Tick();
    InfiniteJudgeHandler.RefillForMeeting(role);
    Require(!role.HasAnOverruleUse, "Reconnecting restored an invalidated Judge capture.");
}

static void DisabledStillInvalidates()
{
    var role = SetUp(enabled: false);
    PlayerControl.LocalPlayer.Data.IsDead = true;
    InfiniteJudgeHandler.Tick();
    PlayerControl.LocalPlayer.Data.IsDead = false;
    CheatToggles.infiniteJudge = true;
    InfiniteJudgeHandler.Tick();
    InfiniteJudgeHandler.RefillForMeeting(role);
    Require(!role.HasAnOverruleUse, "Disabled monitoring allowed a fake-alive flag to restore eligibility.");
}

static void ForeignCallbacksIgnored()
{
    var role = SetUp();
    var foreign = NewJudge();
    InfiniteJudgeHandler.RefillForMeeting(foreign);
    Require(!foreign.HasAnOverruleUse && !role.HasAnOverruleUse, "Foreign meeting callback refilled a role.");
    InfiniteJudgeHandler.RefillForMeeting(role);
    InfiniteJudgeHandler.OnUseConsumed(foreign);
    CheatToggles.infiniteJudge = false;
    InfiniteJudgeHandler.Tick();
    Require(!role.HasAnOverruleUse, "Foreign consume callback cleared the own-grant marker.");
}

static void ResetDoesNotEditRoles()
{
    var role = SetUp(charge: true);
    InfiniteJudgeHandler.Reset();
    Require(role.HasAnOverruleUse, "Reset removed an original native charge.");
    Utils.isFreePlay = false;
    Utils.isInGame = true;
    role.HasAnOverruleUse = false;
    InfiniteJudgeHandler.RefillForMeeting(role);
    Require(!role.HasAnOverruleUse, "Reset enabled a refill in a network round.");
}

static void NewRoundRebinds()
{
    var old = SetUp();
    var replacement = NewJudge();
    PlayerControl.LocalPlayer = NewPlayer(replacement);
    InfiniteJudgeHandler.Tick();
    InfiniteJudgeHandler.Reset();
    InfiniteJudgeHandler.Tick();
    InfiniteJudgeHandler.RefillForMeeting(replacement);
    Require(replacement.HasAnOverruleUse, "Practice reset failed to capture a fresh owned Judge.");
    InfiniteJudgeHandler.RefillForMeeting(old);
    Require(!old.HasAnOverruleUse, "Prior round's Judge was refilled.");
}

static void UnavailableIntroRejected()
{
    foreach (var unavailable in new[] { "missing player", "destroyed player", "missing data", "unowned", "dead", "disconnected", "missing role", "destroyed role" })
    {
        var role = SetUp(intro: false);
        var player = PlayerControl.LocalPlayer;
        switch (unavailable)
        {
            case "missing player": PlayerControl.LocalPlayer = null; break;
            case "destroyed player": player.Destroyed = true; break;
            case "missing data": player.Data = null; break;
            case "unowned": player.AmOwner = false; break;
            case "dead": player.Data.IsDead = true; break;
            case "disconnected": player.Data.Disconnected = true; break;
            case "missing role": player.Data.Role = null; break;
            case "destroyed role": role.Destroyed = true; break;
        }
        InfiniteJudgeHandler.Tick();
        InfiniteJudgeHandler.RefillForMeeting(role);
        Require(!role.HasAnOverruleUse, $"Unavailable practice context '{unavailable}' received a charge.");
    }
}

static void GameplayGates()
{
    foreach (var missing in new[] { "client", "practice", "normal mode", "owner" })
    {
        var role = SetUp();
        switch (missing)
        {
            case "client": Utils.isClient = false; break;
            case "practice": Utils.isFreePlay = false; break;
            case "normal mode": Utils.isNormalGame = false; break;
            case "owner": PlayerControl.LocalPlayer.AmOwner = false; break;
        }
        InfiniteJudgeHandler.RefillForMeeting(role);
        Require(!role.HasAnOverruleUse, $"Missing {missing} accepted a meeting refill.");
    }
}

static void StateSentinelsPreserved()
{
    foreach (var taskGateUnlocked in new[] { false, true })
    {
        var role = SetUp();
        role.HasAlreadyOverruledThisMeeting = true;
        role.TaskGateUnlocked = taskGateUnlocked;
        var nonce = role.OverruleNonce;
        var target = role.TargetPlayerId;
        var threshold = role.RequiredTasks;
        InfiniteJudgeHandler.RefillForMeeting(role);
        Require(role.HasAnOverruleUse, "Valid spent charge failed to rearm.");
        Require(role.HasAlreadyOverruledThisMeeting, "Feature cleared the current-meeting lock.");
        Require(role.TaskGateUnlocked == taskGateUnlocked && role.RequiredTasks == threshold, "Feature changed task unlock state.");
        Require(role.OverruleNonce == nonce && role.TargetPlayerId == target, "Feature changed nonce or target.");
    }
}

static void FreeplayCapture()
{
    var role = SetUp(intro: false);
    Utils.isInGame = false;
    Utils.isFreePlay = true;
    InfiniteJudgeHandler.Tick();
    Require(!role.HasAnOverruleUse, "Freeplay capture granted a charge outside a meeting.");
    InfiniteJudgeHandler.RefillForMeeting(role);
    Require(role.HasAnOverruleUse, "Freeplay required a round intro or Started state.");
}

static void FreeplayRoleReplacement()
{
    var original = SetUp(intro: false);
    Utils.isInGame = false;
    Utils.isFreePlay = true;
    InfiniteJudgeHandler.Tick();
    PlayerControl.LocalPlayer.Data.Role = NewCrew();
    InfiniteJudgeHandler.Tick();
    var replacement = NewJudge();
    PlayerControl.LocalPlayer.Data.Role = replacement;
    InfiniteJudgeHandler.Tick();
    InfiniteJudgeHandler.RefillForMeeting(replacement);
    Require(replacement.HasAnOverruleUse, "A legitimate new practice Judge was not captured.");
    InfiniteJudgeHandler.RefillForMeeting(original);
    Require(!original.HasAnOverruleUse, "Old practice role was refilled after replacement.");
}

static void FreeplayInvalidContexts()
{
    var role = SetUp(intro: false);
    Utils.isFreePlay = true;
    PlayerControl.LocalPlayer.Data.IsDead = true;
    InfiniteJudgeHandler.Tick();
    InfiniteJudgeHandler.RefillForMeeting(role);
    Require(!role.HasAnOverruleUse, "Dead practice Judge was refilled.");
    role = SetUp(intro: false);
    Utils.isFreePlay = true;
    PlayerControl.LocalPlayer.Data.Role = NewCrew();
    InfiniteJudgeHandler.Tick();
    InfiniteJudgeHandler.RefillForMeeting(role);
    Require(!role.HasAnOverruleUse, "Practice crew assignment enabled an unrelated Judge.");
}

static void FreeplayDeathInvalidates()
{
    var role = SetUp(intro: false);
    Utils.isFreePlay = true;
    InfiniteJudgeHandler.Tick();
    PlayerControl.LocalPlayer.Data.IsDead = true;
    InfiniteJudgeHandler.Tick();
    PlayerControl.LocalPlayer.Data.IsDead = false;
    InfiniteJudgeHandler.Tick();
    InfiniteJudgeHandler.RefillForMeeting(role);
    Require(!role.HasAnOverruleUse, "Practice recaptured the same invalidated role after a fake-alive flag.");
}

static void LeavingFreeplayClearsBinding()
{
    var role = SetUp(intro: false);
    Utils.isFreePlay = true;
    InfiniteJudgeHandler.Tick();
    Utils.isFreePlay = false;
    InfiniteJudgeHandler.Tick();
    InfiniteJudgeHandler.RefillForMeeting(role);
    Require(!role.HasAnOverruleUse, "Practice identity leaked into online gameplay.");
    InfiniteJudgeHandler.OnRoundIntro();
    InfiniteJudgeHandler.RefillForMeeting(role);
    Require(!role.HasAnOverruleUse, "A real round intro reenabled online refills.");
}

static void OnlineGuestNeverRefills() => OnlineNeverRefills(host: false);

static void OnlineHostNeverRefills() => OnlineNeverRefills(host: true);

static void OnlineNeverRefills(bool host)
{
    var role = SetUp(charge: true, freeplay: false);
    Utils.isHost = host;
    InfiniteJudgeHandler.OnRoundIntro();
    JudgeRole_OnMeetingStart_InfiniteJudge.Prefix(role);
    role.OnMeetingStart();
    Require(role.HasAnOverruleUse, "Feature removed an online player's initial native use.");
    role.HasAlreadyOverruledThisMeeting = true;
    role.ConsumeOverruleVotesUsage();
    JudgeRole_ConsumeOverruleVotesUsage_InfiniteJudge.Postfix(role);
    for (var meeting = 0; meeting < 5; meeting++)
    {
        InfiniteJudgeHandler.Tick();
        JudgeRole_OnMeetingStart_InfiniteJudge.Prefix(role);
        Require(!role.HasAnOverruleUse, $"Online {(host ? "host" : "guest")} received an extra use.");
        role.OnMeetingStart();
    }
    CheatToggles.infiniteJudge = false;
    InfiniteJudgeHandler.Tick();
    CheatToggles.infiniteJudge = true;
    InfiniteJudgeHandler.Tick();
    JudgeRole_OnMeetingStart_InfiniteJudge.Prefix(role);
    Require(!role.HasAnOverruleUse, "Online toggle cycling rearmed a consumed native use.");
    Require(MalumMenu.MalumMenu.Log.Info.Count == 0, "Online context logged a successful practice grant.");
}

static void ExitPracticeClearsGrantedUse()
{
    foreach (var exitCallback in new[] { "tick", "meeting", "round intro" })
    {
        var role = SetUp();
        InfiniteJudgeHandler.RefillForMeeting(role);
        Require(role.HasAnOverruleUse, "Practice fixture did not grant a use.");
        role.HasAlreadyOverruledThisMeeting = true;
        Utils.isFreePlay = false;
        Utils.isInGame = true;
        if (exitCallback == "meeting") JudgeRole_OnMeetingStart_InfiniteJudge.Prefix(role);
        else if (exitCallback == "round intro") InfiniteJudgeHandler.OnRoundIntro();
        else InfiniteJudgeHandler.Tick();
        Require(!role.HasAnOverruleUse, "Unused practice-granted charge leaked into network gameplay.");
        Require(role.HasAlreadyOverruledThisMeeting, "Exit cleanup changed native per-meeting state.");
        InfiniteJudgeHandler.OnRoundIntro();
        InfiniteJudgeHandler.RefillForMeeting(role);
        Require(!role.HasAnOverruleUse, "Online intro recreated a practice-granted use.");
    }
}

static void ExitPracticePreservesNativeUse()
{
    var original = SetUp(charge: true);
    InfiniteJudgeHandler.RefillForMeeting(original);
    Utils.isFreePlay = false;
    Utils.isInGame = true;
    InfiniteJudgeHandler.Tick();
    Require(original.HasAnOverruleUse, "Leaving practice removed its original native use.");
    var refunded = SetUp();
    InfiniteJudgeHandler.RefillForMeeting(refunded);
    refunded.ConsumeOverruleVotesUsage();
    JudgeRole_ConsumeOverruleVotesUsage_InfiniteJudge.Postfix(refunded);
    refunded.HasAnOverruleUse = true;
    Utils.isFreePlay = false;
    Utils.isInGame = true;
    InfiniteJudgeHandler.Tick();
    Require(refunded.HasAnOverruleUse, "Leaving practice removed an independently refunded native use.");
}

static void NativeRenewalPreserved()
{
    var role = SetUp();
    InfiniteJudgeHandler.RefillForMeeting(role);
    role.ConsumeOverruleVotesUsage();
    JudgeRole_ConsumeOverruleVotesUsage_InfiniteJudge.Postfix(role);
    // Native code can refund a losing Judge's consumed charge. It must not
    // be mistaken for an outstanding charge granted by this feature.
    role.HasAnOverruleUse = true;
    CheatToggles.infiniteJudge = false;
    InfiniteJudgeHandler.Tick();
    Require(role.HasAnOverruleUse, "Disable removed a later independently renewed native charge.");
}

static void ActualMeetingPatch()
{
    var role = SetUp();
    role.HasAlreadyOverruledThisMeeting = true;
    JudgeRole_OnMeetingStart_InfiniteJudge.Prefix(role);
    Require(role.HasAnOverruleUse, "Actual native meeting prefix did not rearm.");
    Require(role.HasAlreadyOverruledThisMeeting, "Prefix bypassed native lock clearing.");
    role.OnMeetingStart();
    Require(!role.HasAlreadyOverruledThisMeeting, "Native meeting-start stand-in did not own its lock reset.");
    role.HasAlreadyOverruledThisMeeting = true;
    role.ConsumeOverruleVotesUsage();
    JudgeRole_ConsumeOverruleVotesUsage_InfiniteJudge.Postfix(role);
    InfiniteJudgeHandler.Tick();
    Require(!role.HasAnOverruleUse && role.HasAlreadyOverruledThisMeeting, "Feature allowed a same-meeting second use.");
    JudgeRole_OnMeetingStart_InfiniteJudge.Prefix(role);
    role.OnMeetingStart();
    Require(role.HasAnOverruleUse && !role.HasAlreadyOverruledThisMeeting, "Next native meeting did not restore normal eligibility.");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static class Identities
{
    public static long Next = 100;
}
