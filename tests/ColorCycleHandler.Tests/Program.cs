using MalumMenu;
using UnityEngine;
using Plugin = MalumMenu.MalumMenu;

int total = 0, failures = 0;
foreach (var guard in new (string Name, Action Change)[]
{
    ("in round", () => { Utils.isLobby = false; Utils.isInGame = true; }),
    ("simultaneous round/lobby flags", () => Utils.isInGame = true),
    ("Freeplay", () => Utils.isFreePlay = true),
    ("not client", () => Utils.isClient = false),
    ("not owned", () => PlayerControl.LocalPlayer.AmOwner = false),
    ("disconnected", () => PlayerControl.LocalPlayer.Data.Disconnected = true),
    ("dead", () => PlayerControl.LocalPlayer.Data.IsDead = true),
    ("missing data", () => PlayerControl.LocalPlayer.Data = null),
    ("missing player", () => PlayerControl.LocalPlayer = null),
    ("missing client", () => AmongUsClient.Instance = null),
    ("destroyed client", () => AmongUsClient.Instance.Destroyed = true),
    ("unfocused", () => Application.isFocused = false),
    ("panic", () => Plugin.isPanicked = true),
    ("disguised", () => { PlayerControl.LocalPlayer.CurrentOutfitType = PlayerOutfitType.Shapeshift; PlayerControl.LocalPlayer.DisguisedOutfit = new() { ColorId = 4 }; }),
})
{
    Run(guard.Name + " denies lobby-cycle start", () =>
    {
        SetUp(); var own = PlayerControl.LocalPlayer; guard.Change();
        ColorCycleHandler.Start(ColorCycleMode.LobbyCycle); Tick(10); Tick(20);
        Require(own.Requests.Count == 0 && ColorCycleHandler.Mode == ColorCycleMode.Off, "ineligible start requested color or armed mode");
    });
    Run(guard.Name + " stops an armed lobby cycle before request", () =>
    {
        SetUp(); var own = PlayerControl.LocalPlayer; ColorCycleHandler.Start(ColorCycleMode.LobbyCycle);
        guard.Change(); Tick(10); Tick(20);
        Require(own.Requests.Count == 0 && ColorCycleHandler.Mode == ColorCycleMode.Off, "context transition sent a color request");
    });
}

Run("available lobby color is requested once without claimed acceptance", () =>
{
    SetUp(); var own = PlayerControl.LocalPlayer; var other = AddPlayer(2); ColorCycleHandler.Start(ColorCycleMode.LobbyCycle);
    Tick(2.9); Require(own.Requests.Count == 0, "request sent before default interval");
    Tick(3); Require(own.Requests.Count == 1 && own.Requests[0].Color == 3, "request reused selected or occupied color");
    Require(other.Requests.Count == 0 && own.Data.DefaultOutfit.ColorId == 1 && own.cosmetics.Colors.Count == 0, "request fabricated acceptance or edited another player");
    Require(ColorCycleHandler.StatusText.Contains("requested", StringComparison.OrdinalIgnoreCase), "status claims more than a request");
    ColorCycleHandler.Stop(); Require(own.Requests.Count == 1, "stop sent a restore RPC");
});
Run("all occupied colors wait instead of sending invalid requests", () =>
{
    SetUp(4); AddPlayer(0); AddPlayer(2); AddPlayer(3); ColorCycleHandler.Start(ColorCycleMode.LobbyCycle);
    Tick(10); Tick(20); Require(PlayerControl.LocalPlayer.Requests.Count == 0, "occupied palette still produced request");
});
Run("disconnected and destroyed players do not reserve colors", () =>
{
    SetUp(); AddPlayer(2).Data.Disconnected = true; AddPlayer(2).Destroyed = true;
    ColorCycleHandler.Start(ColorCycleMode.LobbyCycle); Tick(3);
    Require(PlayerControl.LocalPlayer.Requests.Single().Color == 2, "stale actor reserved an otherwise available color");
});
foreach (bool lobby in new[] { false, true })
    Run((lobby ? "lobby" : "preview") + " honors the shorter palette and missing palette", () =>
    {
        SetUp(6); Palette.ShadowColors = new object[3]; PlayerControl.LocalPlayer.Data.DefaultOutfit.ColorId = 2;
        var own = PlayerControl.LocalPlayer; ColorCycleHandler.Start(lobby ? ColorCycleMode.LobbyCycle : ColorCycleMode.LocalRainbow);
        Tick(5); Tick(10);
        Require(lobby ? own.Requests.All(request => request.Color < 3) : own.cosmetics.Colors.All(color => color < 3), "palette mismatch accessed unavailable color");
        int changes = lobby ? own.Requests.Count : own.cosmetics.Colors.Count;
        Palette.PlayerColors = null; Tick(20);
        Require((lobby ? own.Requests.Count : own.cosmetics.Colors.Count) == changes, "missing palette still produced a change");
    });
Run("lobby palette indices remain bounded by byte protocol", () =>
{
    SetUp(300); PlayerControl.LocalPlayer.Data.DefaultOutfit.ColorId = 255;
    ColorCycleHandler.Start(ColorCycleMode.LobbyCycle); Tick(3);
    Require(PlayerControl.LocalPlayer.Requests.Single().Color == 0, "color index did not wrap within protocol range");
});
Run("large time leap produces one request and no catch-up burst", () =>
{
    SetUp(); var own = PlayerControl.LocalPlayer; ColorCycleHandler.Start(ColorCycleMode.LobbyCycle);
    Tick(10000); for (int tick = 0; tick < 50; tick++) Tick(10000);
    Require(own.Requests.Count == 1, "forward leap replayed missed requests");
    Tick(10002.99); Require(own.Requests.Count == 1, "post-leap cooldown was shortened");
    Tick(10003); Require(own.Requests.Count == 2, "post-leap cooldown never completed");
});
Run("backward clock never sends early or loops missed requests", () =>
{
    SetUp(); Time.realtimeSinceStartupAsDouble = 100; ColorCycleHandler.Start(ColorCycleMode.LobbyCycle);
    for (int tick = 0; tick < 20; tick++) Tick(0);
    Require(PlayerControl.LocalPlayer.Requests.Count == 0, "clock rewind caused early requests");
    Tick(103); Require(PlayerControl.LocalPlayer.Requests.Count <= 1, "clock recovery caused burst");
});
foreach (float interval in new[] { 0f, float.NaN, float.PositiveInfinity })
    Run("lobby interval keeps a safe floor/fallback: " + interval, () =>
    {
        SetUp(); Plugin.lobbyColorInterval.Value = interval; ColorCycleHandler.Start(ColorCycleMode.LobbyCycle);
        Tick(1.99); Require(PlayerControl.LocalPlayer.Requests.Count == 0, "unsafe interval produced rapid request");
        Tick(3); Require(PlayerControl.LocalPlayer.Requests.Count == 1, "interval fallback never requested color");
        for (int tick = 0; tick < 10; tick++) Tick(3.1);
        Require(PlayerControl.LocalPlayer.Requests.Count == 1, "interval floor failed on repeated ticks");
    });
foreach (double time in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.MaxValue })
    Run("nonfinite clock does not produce requests: " + time, () =>
    {
        SetUp(); var own = PlayerControl.LocalPlayer; ColorCycleHandler.Start(ColorCycleMode.LobbyCycle);
        for (int tick = 0; tick < 10; tick++) Tick(time);
        Require(own.Requests.Count == 0, "nonfinite clock bypassed rate limit");
        SetUp(); own = PlayerControl.LocalPlayer; Time.realtimeSinceStartupAsDouble = time;
        ColorCycleHandler.Start(ColorCycleMode.LobbyCycle); for (int tick = 0; tick < 10; tick++) Tick(time);
        Require(own.Requests.Count == 0, "nonfinite start clock armed a request loop");
    });
Run("local preview never writes the assigned outfit or sends requests", () =>
{
    SetUp(); var own = PlayerControl.LocalPlayer; var other = AddPlayer(2);
    ColorCycleHandler.Start(ColorCycleMode.LocalRainbow); Tick(0.61); Tick(1.22); Tick(10000);
    Require(own.cosmetics.Colors.Count == 3 && own.Requests.Count == 0, "preview used network or replayed missed frames");
    Require(own.Data.DefaultOutfit.ColorId == 1 && other.cosmetics.Colors.Count == 0, "preview changed assigned data or another player");
    ColorCycleHandler.Stop(); Require(own.cosmetics.DisplayedColor == 1 && own.Requests.Count == 0, "preview stop failed local-only restoration");
});
Run("restoration uses latest accepted outfit rather than stale captured color", () =>
{
    SetUp(); var own = PlayerControl.LocalPlayer; ColorCycleHandler.Start(ColorCycleMode.LocalRainbow); Tick(1);
    own.Data.DefaultOutfit.ColorId = 5; ColorCycleHandler.Stop();
    Require(own.cosmetics.DisplayedColor == 5, "restore overwrote later accepted color");
});
foreach (var type in new[] { PlayerOutfitType.Shapeshift, PlayerOutfitType.MushroomMixup })
    Run(type + " pauses preview and restores current disguise appearance", () =>
    {
        SetUp(); var own = PlayerControl.LocalPlayer; ColorCycleHandler.Start(ColorCycleMode.LocalRainbow); Tick(1);
        own.CurrentOutfitType = type; own.DisguisedOutfit = new() { ColorId = 4 }; Tick(2); Tick(10);
        Require(own.cosmetics.DisplayedColor == 4 && own.cosmetics.Colors.Count == 2, "preview overrode disguise");
        Require(own.Data.DefaultOutfit.ColorId == 1 && own.Requests.Count == 0, "disguise pause changed actual color");
        own.CurrentOutfitType = PlayerOutfitType.Default; Tick(10.7);
        Require(own.cosmetics.Colors.Count == 3, "preview never resumed after disguise ended");
    });
Run("ownership loss never recolors a now foreign player", () =>
{
    SetUp(); var own = PlayerControl.LocalPlayer; ColorCycleHandler.Start(ColorCycleMode.LocalRainbow); Tick(1);
    own.AmOwner = false; int changes = own.cosmetics.Colors.Count; Tick(2); ColorCycleHandler.Stop();
    Require(own.cosmetics.Colors.Count == changes && ColorCycleHandler.Mode == ColorCycleMode.Off, "cleanup edited foreign cosmetics");
});
Run("player replacement restores original preview and leaves new actor unchanged", () =>
{
    SetUp(); var old = PlayerControl.LocalPlayer; ColorCycleHandler.Start(ColorCycleMode.LocalRainbow); Tick(1);
    var replacement = new PlayerControl { AmOwner = true }; PlayerControl.LocalPlayer = replacement; Tick(2);
    Require(old.cosmetics.DisplayedColor == 1 && replacement.cosmetics.Colors.Count == 0 && replacement.Requests.Count == 0, "replacement inherited a preview or old state");
});
Run("cosmetic replacement and destruction never redirect cleanup", () =>
{
    SetUp(); var own = PlayerControl.LocalPlayer; var old = own.cosmetics;
    ColorCycleHandler.Start(ColorCycleMode.LocalRainbow); Tick(1); own.cosmetics = new(); Tick(2);
    Require(own.cosmetics.Colors.Count == 0 && ColorCycleHandler.Mode == ColorCycleMode.Off, "replacement cosmetics were changed");
    SetUp(); own = PlayerControl.LocalPlayer; ColorCycleHandler.Start(ColorCycleMode.LocalRainbow); Tick(1);
    own.cosmetics.Destroyed = true; ColorCycleHandler.Stop(); Require(own.cosmetics.Colors.Count == 1, "destroyed cosmetic was accessed");
});
Run("client and ship changes restore preview and stop the mode", () =>
{
    foreach (bool changeShip in new[] { false, true })
    {
        SetUp(); var own = PlayerControl.LocalPlayer; ColorCycleHandler.Start(ColorCycleMode.LocalRainbow); Tick(1);
        if (changeShip) ShipStatus.Instance = new(); else AmongUsClient.Instance = new(); Tick(2);
        Require(own.cosmetics.DisplayedColor == 1 && ColorCycleHandler.Mode == ColorCycleMode.Off, "changed game kept previous color preview");
    }
});
Run("switching to lobby mode restores preview without an extra request", () =>
{
    SetUp(); var own = PlayerControl.LocalPlayer; ColorCycleHandler.Start(ColorCycleMode.LocalRainbow); Tick(1);
    ColorCycleHandler.Start(ColorCycleMode.LobbyCycle); Require(own.cosmetics.DisplayedColor == 1 && own.Requests.Count == 0, "mode switch did not restore locally");
    Tick(3.9); Require(own.Requests.Count == 0, "mode switch reused preview's shorter timer");
    Tick(4); Require(own.Requests.Count == 1, "lobby mode did not retain independent cooldown");
});

Console.WriteLine($"{total - failures}/{total} tests passed; actual handler linked, all requests recorded locally.");
return failures == 0 ? 0 : 1;

void SetUp(int colors = 6)
{
    ColorCycleHandler.Stop(); Plugin.Log = new(); Plugin.isPanicked = false;
    Plugin.rainbowColorInterval = new(0.6f); Plugin.lobbyColorInterval = new(3f);
    Application.isFocused = true; Time.realtimeSinceStartupAsDouble = 0;
    Utils.isClient = true; Utils.isLobby = true; Utils.isInGame = false; Utils.isFreePlay = false;
    Palette.PlayerColors = new object[colors]; Palette.ShadowColors = new object[colors];
    AmongUsClient.Instance = new(); ShipStatus.Instance = null;
    PlayerControl.LocalPlayer = new() { AmOwner = true }; PlayerControl.AllPlayerControls = [PlayerControl.LocalPlayer];
}
PlayerControl AddPlayer(int color) { var other = new PlayerControl(); other.Data.DefaultOutfit.ColorId = color; PlayerControl.AllPlayerControls.Add(other); return other; }
void Tick(double time) { Time.realtimeSinceStartupAsDouble = time; ColorCycleHandler.Tick(); }
void Run(string name, Action test) { total++; try { test(); Require(Plugin.Log.Warnings.Count == 0, "handler caught/logged an exception"); Console.WriteLine("PASS " + name); } catch (Exception error) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + error.Message); } }
void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
