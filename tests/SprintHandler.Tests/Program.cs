using MalumMenu;
using UnityEngine;
using Plugin = MalumMenu.MalumMenu;

int total = 0, failed = 0;
var blocked = new (string Name, Action Change)[]
{
    ("toggle off", () => CheatToggles.sprint = false),
    ("panic", () => Plugin.isPanicked = true),
    ("automation active", () => MovementAutomation.Active = true),
    ("unfocused application", () => Application.isFocused = false),
    ("released key", () => Input.Pressed.Clear()),
    ("not a client", () => Utils.isClient = false),
    ("not in game or practice", () => Utils.isInGame = false),
    ("meeting", () => Utils.isMeeting = true),
    ("exile", () => Utils.isExiling = true),
    ("missing local player", () => PlayerControl.LocalPlayer = null),
    ("destroyed local player", () => PlayerControl.LocalPlayer.Destroyed = true),
    ("unowned local player", () => PlayerControl.LocalPlayer.AmOwner = false),
    ("missing player data", () => PlayerControl.LocalPlayer.Data = null),
    ("dead player", () => PlayerControl.LocalPlayer.Data.IsDead = true),
    ("disconnected player", () => PlayerControl.LocalPlayer.Data.Disconnected = true),
    ("movement blocked", () => PlayerControl.LocalPlayer.CanMove = false),
    ("in vent", () => PlayerControl.LocalPlayer.inVent = true),
    ("on ladder", () => PlayerControl.LocalPlayer.onLadder = true),
    ("on moving platform", () => PlayerControl.LocalPlayer.inMovingPlat = true),
    ("missing physics", () => PlayerControl.LocalPlayer.MyPhysics = null),
    ("destroyed physics", () => PlayerControl.LocalPlayer.MyPhysics.Destroyed = true),
    ("unassigned key", () => Plugin.sprintKeybind.Value = "None"),
    ("missing ship", () => ShipStatus.Instance = null),
    ("destroyed ship", () => ShipStatus.Instance.Destroyed = true),
    ("missing HUD", () => HudManager.Existing = null),
    ("destroyed HUD", () => HudManager.Existing.Destroyed = true),
    ("intro", () => HudManager.Existing.IsIntroDisplayed = true),
    ("chat open", () => HudManager.Existing.Chat.IsOpenOrOpening = true),
    ("menu open", () => MenuUI.isGUIActive = true),
};

foreach (var scenario in blocked)
{
    Run(scenario.Name + " does not apply sprint", () =>
    {
        SetUp(); var physics = PlayerControl.LocalPlayer.MyPhysics;
        scenario.Change();
        for (int frame = 0; frame < 4; frame++) SprintHandler.Tick();
        Equal(physics.PeekSpeed, 1.75f, "ineligible context changed speed");
        Require(physics.Writes == 0 && !SprintHandler.IsSprinting, "ineligible context acquired sprint state");
        Require(HudManager.Creations == 0, "eligibility check created a gameplay HUD");
    });
    Run(scenario.Name + " restores an already active sprint", () =>
    {
        SetUp(); var physics = PlayerControl.LocalPlayer.MyPhysics;
        SprintHandler.Tick(); Equal(physics.PeekSpeed, 3.5f, "fixture never sprinted");
        scenario.Change(); SprintHandler.Tick();
        if (!physics.Destroyed) Equal(physics.PeekSpeed, 1.75f, "blocked transition did not restore captured speed");
        Require(!SprintHandler.IsSprinting, "blocked transition retained active state");
        Require(HudManager.Creations == 0, "blocked transition created a gameplay HUD");
    });
}

Run("repeated frames never compound captured speed", () =>
{
    SetUp(); var physics = PlayerControl.LocalPlayer.MyPhysics;
    for (int frame = 0; frame < 100; frame++) { SprintHandler.Tick(); Equal(physics.PeekSpeed, 3.5f, "speed compounded"); }
    Require(SprintHandler.IsSprinting, "sprint never remained active");
    Input.Pressed.Clear(); SprintHandler.Tick(); Equal(physics.PeekSpeed, 1.75f, "release failed to restore");
});
foreach (float baseline in new[] { 1.234567f, -1.234567f, 0f })
    Run("exact signed speed is restored: " + baseline, () =>
    {
        SetUp(baseline); var physics = PlayerControl.LocalPlayer.MyPhysics;
        SprintHandler.Tick(); Input.Pressed.Clear(); SprintHandler.Tick();
        Require(BitConverter.SingleToInt32Bits(physics.PeekSpeed) == BitConverter.SingleToInt32Bits(baseline), "restoration changed captured float bits");
        SprintHandler.Reset(); Equal(physics.PeekSpeed, baseline, "idle reset changed speed");
    });

foreach (var test in new (float Baseline, float Multiplier, float Expected)[] { (2f, 0f, 2f), (2f, 99f, 16f), (2f, 2.5f, 5f), (8f, 4f, 32f), (-8f, 4f, -32f), (8f, 8f, 40f), (-8f, 8f, -40f), (2f, float.NaN, 4f), (2f, float.PositiveInfinity, 4f), (2f, float.NegativeInfinity, 4f) })
    Run($"multiplier {test.Multiplier} respects configured limits and signed cap", () =>
    {
        SetUp(test.Baseline); Plugin.sprintMultiplier.Value = test.Multiplier;
        var physics = PlayerControl.LocalPlayer.MyPhysics;
        SprintHandler.Tick(); Equal(physics.PeekSpeed, test.Expected, "wrong multiplier or magnitude cap");
        SprintHandler.Reset(); Equal(physics.PeekSpeed, test.Baseline, "clamped sprint did not restore baseline");
    });

Run("eight-times sprint exceeds the previous speed cap and restores on release", () =>
{
    SetUp(3.25f); Plugin.sprintMultiplier.Value = 8f;
    var physics = PlayerControl.LocalPlayer.MyPhysics;
    for (int frame = 0; frame < 100; frame++)
    {
        SprintHandler.Tick(); Equal(physics.PeekSpeed, 26f, "eight-times sprint was clipped or compounded");
    }
    Input.Pressed.Clear(); SprintHandler.Tick();
    Require(BitConverter.SingleToInt32Bits(physics.PeekSpeed) == BitConverter.SingleToInt32Bits(3.25f), "release changed the original speed bits");
    Require(!SprintHandler.IsSprinting, "release retained the faster sprint snapshot");
});

foreach (float baseline in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
    Run("nonfinite baseline is not captured or overwritten: " + baseline, () =>
    {
        SetUp(baseline); var physics = PlayerControl.LocalPlayer.MyPhysics;
        SprintHandler.Tick(); SprintHandler.Reset();
        Require(!SprintHandler.IsSprinting && physics.Writes == 0, "invalid baseline was acquired or overwritten");
        Require(BitConverter.SingleToInt32Bits(physics.PeekSpeed) == BitConverter.SingleToInt32Bits(baseline), "invalid baseline changed");
    });
Run("multiplier changes while held use the original snapshot", () =>
{
    SetUp(2f); Plugin.sprintMultiplier.Value = 4f;
    var physics = PlayerControl.LocalPlayer.MyPhysics; SprintHandler.Tick();
    Equal(physics.PeekSpeed, 8f, "initial four-times multiplier was wrong");
    Plugin.sprintMultiplier.Value = 8f;
    for (int frame = 0; frame < 100; frame++)
    {
        SprintHandler.Tick(); Equal(physics.PeekSpeed, 16f, "raising multiplier while held compounded boosted speed");
    }
    Plugin.sprintMultiplier.Value = 3f; SprintHandler.Tick(); Equal(physics.PeekSpeed, 6f, "new multiplier compounded boosted speed");
    Plugin.sprintMultiplier.Value = 1f; SprintHandler.Tick(); Equal(physics.PeekSpeed, 2f, "minimum multiplier did not use baseline");
    SprintHandler.Reset(); Equal(physics.PeekSpeed, 2f, "configuration change changed restore snapshot");
});
Run("physics replacement restores old and captures replacement's own baseline", () =>
{
    SetUp(); var old = PlayerControl.LocalPlayer.MyPhysics; SprintHandler.Tick();
    var replacement = new PlayerPhysics(3.25f); PlayerControl.LocalPlayer.MyPhysics = replacement;
    SprintHandler.Tick(); Equal(old.PeekSpeed, 1.75f, "replacement transition lost old snapshot");
    Equal(replacement.PeekSpeed, 6.5f, "replacement reused previous physics snapshot");
    SprintHandler.Reset(); Equal(replacement.PeekSpeed, 3.25f, "replacement did not restore its own snapshot");
});
Run("new ship context restores before recapturing current native speed", () =>
{
    SetUp(); var physics = PlayerControl.LocalPlayer.MyPhysics; SprintHandler.Tick();
    ShipStatus.Instance = new(); SprintHandler.Tick(); Equal(physics.PeekSpeed, 3.5f, "new ship context recaptured boosted speed");
    SprintHandler.Reset(); Equal(physics.PeekSpeed, 1.75f, "new ship context retained boosted restore snapshot");
});

Run("Freeplay permits sprint without an active network round", () =>
{
    SetUp(); Utils.isInGame = false; Utils.isFreePlay = true;
    SprintHandler.Tick(); Equal(PlayerControl.LocalPlayer.MyPhysics.PeekSpeed, 3.5f, "Freeplay was blocked");
});
Run("lobby without a ship supports boost and exact release", () =>
{
    SetUp(-1.75f); Utils.isInGame = false; Utils.isLobby = true; ShipStatus.Instance = null;
    var physics = PlayerControl.LocalPlayer.MyPhysics;
    for (int frame = 0; frame < 10; frame++) SprintHandler.Tick();
    Equal(physics.PeekSpeed, -3.5f, "lobby required a ship or compounded its snapshot");
    Require(SprintHandler.IsSprinting, "eligible lobby never sprinted");
    Input.Pressed.Clear(); SprintHandler.Tick(); Equal(physics.PeekSpeed, -1.75f, "lobby release did not restore original signed speed");
    Require(!SprintHandler.IsSprinting, "lobby release retained active snapshot");
});
Run("lobby to round restores before acquiring the new ship context", () =>
{
    SetUp(); Utils.isInGame = false; Utils.isLobby = true; ShipStatus.Instance = null;
    var physics = PlayerControl.LocalPlayer.MyPhysics; SprintHandler.Tick(); Equal(physics.PeekSpeed, 3.5f, "fixture never sprinted in lobby");
    Utils.isLobby = false; Utils.isInGame = true; ShipStatus.Instance = new();
    SprintHandler.Tick(); Equal(physics.PeekSpeed, 3.5f, "round captured boosted lobby speed");
    SprintHandler.Reset(); Equal(physics.PeekSpeed, 1.75f, "round retained boosted lobby restore value");
});
Run("round to lobby without ship restores before recapturing", () =>
{
    SetUp(); var physics = PlayerControl.LocalPlayer.MyPhysics; SprintHandler.Tick();
    Utils.isInGame = false; Utils.isLobby = true; ShipStatus.Instance = null;
    SprintHandler.Tick(); Equal(physics.PeekSpeed, 3.5f, "returning lobby compounded or disabled sprint");
    Input.Pressed.Clear(); SprintHandler.Tick(); Equal(physics.PeekSpeed, 1.75f, "returning lobby lost original snapshot");
});
Run("lobby with chat open does not acquire sprint", () =>
{
    SetUp(); Utils.isInGame = false; Utils.isLobby = true; ShipStatus.Instance = null;
    var physics = PlayerControl.LocalPlayer.MyPhysics; HudManager.Existing.Chat.IsOpenOrOpening = true;
    SprintHandler.Tick(); Equal(physics.PeekSpeed, 1.75f, "chat-open lobby applied sprint");
    Require(physics.Writes == 0 && !SprintHandler.IsSprinting, "chat-open lobby acquired a snapshot");
});
Run("opening lobby chat cleans up the active boost", () =>
{
    SetUp(); Utils.isInGame = false; Utils.isLobby = true; ShipStatus.Instance = null;
    var physics = PlayerControl.LocalPlayer.MyPhysics; SprintHandler.Tick(); Equal(physics.PeekSpeed, 3.5f, "fixture never sprinted in lobby");
    HudManager.Existing.Chat.IsOpenOrOpening = true; SprintHandler.Tick();
    Equal(physics.PeekSpeed, 1.75f, "lobby chat opening did not restore speed");
    Require(!SprintHandler.IsSprinting, "lobby chat retained active sprint");
});
Run("leaving a shipless lobby cleans up speed", () =>
{
    SetUp(); Utils.isInGame = false; Utils.isLobby = true; ShipStatus.Instance = null;
    var physics = PlayerControl.LocalPlayer.MyPhysics; SprintHandler.Tick();
    Equal(physics.PeekSpeed, 3.5f, "fixture never sprinted in lobby");
    Utils.isLobby = false; SprintHandler.Tick(); Equal(physics.PeekSpeed, 1.75f, "main menu retained lobby boost");
    Require(!SprintHandler.IsSprinting, "main menu retained lobby snapshot");
});
foreach (var guard in new (string Name, Action Change)[]
{
    ("dead", () => PlayerControl.LocalPlayer.Data.IsDead = true),
    ("disconnected", () => PlayerControl.LocalPlayer.Data.Disconnected = true),
    ("not owned", () => PlayerControl.LocalPlayer.AmOwner = false),
    ("unable to move", () => PlayerControl.LocalPlayer.CanMove = false),
})
    foreach (bool alreadyActive in new[] { false, true })
        Run($"lobby player {guard.Name} is blocked and cleaned up (active={alreadyActive})", () =>
        {
            SetUp(); Utils.isInGame = false; Utils.isLobby = true; ShipStatus.Instance = null;
            var physics = PlayerControl.LocalPlayer.MyPhysics;
            if (alreadyActive) { SprintHandler.Tick(); Equal(physics.PeekSpeed, 3.5f, "fixture never sprinted in lobby"); }
            guard.Change(); SprintHandler.Tick(); Equal(physics.PeekSpeed, 1.75f, "lobby exception bypassed player guard");
            Require(!SprintHandler.IsSprinting, "blocked lobby player retained sprint snapshot");
        });
Run("configured key controls sprint", () =>
{
    SetUp(); Plugin.sprintKeybind.Value = "RightShift";
    SprintHandler.Tick(); Require(!SprintHandler.IsSprinting, "default key still activated configured sprint");
    Input.Pressed.Add(KeyCode.RightShift); SprintHandler.Tick();
    Require(SprintHandler.IsSprinting && Input.LastRequested == KeyCode.RightShift, "configured key was ignored");
});
Run("reset restores captured physics without changing replacement physics", () =>
{
    SetUp(); var old = PlayerControl.LocalPlayer.MyPhysics;
    SprintHandler.Tick(); var replacement = new PlayerPhysics(4.25f);
    PlayerControl.LocalPlayer.MyPhysics = replacement; SprintHandler.Reset();
    Equal(old.PeekSpeed, 1.75f, "old physics not restored");
    Equal(replacement.PeekSpeed, 4.25f, "reset wrote replacement physics");
    Require(replacement.Writes == 0 && !SprintHandler.IsSprinting, "reset touched replacement or retained sprint");
});
Run("player replacement reset restores only the original player", () =>
{
    SetUp(); var old = PlayerControl.LocalPlayer.MyPhysics; SprintHandler.Tick();
    var replacement = new PlayerControl { MyPhysics = new(6.5f) }; PlayerControl.LocalPlayer = replacement;
    SprintHandler.Reset(); Equal(old.PeekSpeed, 1.75f, "old player physics not restored");
    Require(replacement.MyPhysics.Writes == 0, "reset wrote a new player");
});
Run("destroyed physics is discarded safely on reset", () =>
{
    SetUp(); var physics = PlayerControl.LocalPlayer.MyPhysics; SprintHandler.Tick(); physics.Destroyed = true;
    int writes = physics.Writes, reads = physics.Reads;
    SprintHandler.Reset(); SprintHandler.Reset();
    Require(physics.Reads == reads && physics.Writes == writes && !SprintHandler.IsSprinting, "reset accessed destroyed native physics");
});
Run("inactive reset never invents or writes a speed snapshot", () =>
{
    SetUp(); var physics = PlayerControl.LocalPlayer.MyPhysics;
    SprintHandler.Reset(); SprintHandler.Reset(); Require(physics.Writes == 0, "idle reset wrote physics");
});

Console.WriteLine($"{total - failed}/{total} tests passed; no game/network assemblies referenced.");
return failed == 0 ? 0 : 1;

void SetUp(float baseline = 1.75f)
{
    SprintHandler.Reset();
    MovementAutomation.Active = false;
    Plugin.Log = new(); Plugin.isPanicked = false;
    Plugin.sprintKeybind = new("LeftShift"); Plugin.sprintMultiplier = new(2f);
    CheatToggles.sprint = true; MenuUI.isGUIActive = false;
    Utils.isClient = true; Utils.isInGame = true; Utils.isFreePlay = false; Utils.isLobby = false; Utils.isMeeting = false; Utils.isExiling = false;
    Application.isFocused = true; Input.Pressed.Clear(); Input.Pressed.Add(KeyCode.LeftShift); Input.Calls = 0;
    PlayerControl.LocalPlayer = new() { MyPhysics = new(baseline) }; ShipStatus.Instance = new();
    HudManager.Existing = new(); HudManager.Acquisitions = 0; HudManager.Creations = 0;
}
void Run(string name, Action test)
{
    total++;
    try { test(); Require(Plugin.Log.Errors.Count == 0 && Plugin.Log.Warnings.Count == 0, "handler logged an exception"); Console.WriteLine("PASS " + name); }
    catch (Exception error) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + error.Message); }
}
void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
void Equal(float actual, float expected, string message) => Require(actual == expected, message + $": expected {expected}, got {actual}");
