using MalumMenu;

var cases = new (string Name, Action Arrange)[]
{
    ("Toggle off", () => CheatToggles.automaticTasks = false),
    ("AI walking mode owns task completion", () => MovementAutomation.Mode = MovementMode.AiTasks),
    ("Main menu with auto-loading enabled", () => { Utils.isClient = false; Utils.isInGame = false; PlayerControl.LocalPlayer = null; ShipStatus.Instance = null; }),
    ("Lobby without an active round", () => Utils.isInGame = false),
    ("Missing local player", () => PlayerControl.LocalPlayer = null),
    ("Destroyed local player", () => PlayerControl.LocalPlayer.Destroyed = true),
    ("Missing player data", () => PlayerControl.LocalPlayer.Data = null),
    ("Unowned local player", () => PlayerControl.LocalPlayer.AmOwner = false),
    ("Disconnected local player", () => PlayerControl.LocalPlayer.Data.Disconnected = true),
    ("Missing ship", () => ShipStatus.Instance = null),
    ("Destroyed ship", () => ShipStatus.Instance.Destroyed = true),
    ("Non-Normal game mode", () => Utils.isNormalGame = false),
    ("Missing game-options manager", () => GameOptionsManager.Instance = null),
    ("Missing current game options", () => GameOptionsManager.Instance.CurrentGameOptions = null),
};

var failures = 0;
var total = 0;
foreach (var scenario in cases)
    Run($"{scenario.Name} never acquires gameplay HUD", () =>
    {
        SetUp();
        scenario.Arrange();
        for (var tick = 0; tick < 10; tick++) AutomaticTasksHandler.Tick();
        Require(HudManager.Acquisitions == 0, $"HUD singleton was acquired {HudManager.Acquisitions} times.");
        Require(HudManager.Creations == 0, "Gameplay HUD was created outside eligible gameplay.");
    });

Run("Normal gameplay reaches HUD and assigned-task check", () =>
{
    SetUp();
    AutomaticTasksHandler.Tick();
    Require(HudManager.Acquisitions == 1 && HudManager.Creations == 1, "Eligible Normal gameplay never reached its HUD.");
    Require(PlayerControl.LocalPlayer.TaskListReads > 0, "Eligible gameplay never reached its assigned tasks.");
});

Run("Freeplay reaches HUD without Started state", () =>
{
    SetUp();
    Utils.isInGame = false;
    Utils.isFreePlay = true;
    AutomaticTasksHandler.Tick();
    Require(HudManager.Acquisitions == 1, "Eligible practice required an online Started state.");
    Require(PlayerControl.LocalPlayer.TaskListReads > 0, "Eligible practice never reached its assigned tasks.");
});

foreach (var pause in new[] { "intro", "meeting", "exile" })
    Run($"Eligible gameplay pauses tasks during {pause}", () =>
    {
        SetUp();
        HudManager.Existing = new() { IsIntroDisplayed = pause == "intro" };
        Utils.isMeeting = pause == "meeting";
        Utils.isExiling = pause == "exile";
        AutomaticTasksHandler.Tick();
        Require(HudManager.Acquisitions == 1, "Eligible paused context failed to acquire the gameplay HUD.");
        Require(PlayerControl.LocalPlayer.TaskListReads == 0, "Paused context read tasks for completion.");
        Require(AutomaticTasksHandler.StatusText == "Paused during intro or meeting.", "Paused status was lost.");
    });

Run("Eligible gameplay with unavailable HUD waits without touching tasks", () =>
{
    SetUp();
    HudManager.ReturnMissing = true;
    AutomaticTasksHandler.Tick();
    Require(HudManager.Acquisitions == 1, "Eligible context did not check HUD availability.");
    Require(PlayerControl.LocalPlayer.TaskListReads == 0, "Missing HUD allowed task handling.");
});

Run("Returning from gameplay to main menu stops HUD acquisition", () =>
{
    SetUp();
    AutomaticTasksHandler.Tick();
    var beforeMenu = HudManager.Acquisitions;
    Utils.isClient = false;
    Utils.isInGame = false;
    PlayerControl.LocalPlayer = null;
    ShipStatus.Instance = null;
    HudManager.Existing = null;
    for (var tick = 0; tick < 10; tick++) AutomaticTasksHandler.Tick();
    Require(HudManager.Acquisitions == beforeMenu, "Scene exit kept acquiring gameplay HUD.");
    Require(HudManager.Creations == 1, "Main menu recreated HUD after scene exit.");
});

Console.WriteLine($"{total - failures}/{total} tests passed.");
return failures == 0 ? 0 : 1;

void Run(string name, Action action)
{
    total++;
    try
    {
        action();
        Require(MalumMenu.MalumMenu.Log.Errors.Count == 0, "Handler threw and silently entered its error state.");
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
    }
}

static void SetUp()
{
    MovementAutomation.Mode = MovementMode.Off;
    AutomaticTasksHandler.Reset();
    CheatToggles.automaticTasks = true;
    Utils.isClient = true;
    Utils.isInGame = true;
    Utils.isFreePlay = false;
    Utils.isNormalGame = true;
    Utils.isMeeting = false;
    Utils.isExiling = false;
    PlayerControl.LocalPlayer = new() { Pointer = new IntPtr(10) };
    ShipStatus.Instance = new() { Pointer = new IntPtr(20) };
    HudManager.Acquisitions = 0;
    HudManager.Creations = 0;
    HudManager.Existing = null;
    HudManager.ReturnMissing = false;
    GameOptionsManager.Instance = new() { CurrentGameOptions = new() };
    MalumMenu.MalumMenu.Log = new();
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
