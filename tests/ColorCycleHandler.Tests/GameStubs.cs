// Closed local contracts only. CmdCheckColor records attempts and never changes
// the assigned outfit, simulating neither a host response nor acceptance.
public class NativeObject
{
    private static int _next = 100;
    public IntPtr Pointer = new(++_next);
    public bool Destroyed;
    public static implicit operator bool(NativeObject value) => value is not null && !value.Destroyed;
    public static bool operator !(NativeObject value) => !(bool)value;
}
public enum PlayerOutfitType { Default, Shapeshift, MushroomMixup }
public sealed class PlayerOutfit { public int ColorId; }
public sealed class PlayerData
{
    public bool IsDead, Disconnected;
    public PlayerOutfit DefaultOutfit = new() { ColorId = 1 };
}
public sealed class CosmeticsLayer : NativeObject
{
    public readonly List<int> Colors = [];
    public int DisplayedColor = 1;
    public void SetColor(int color)
    {
        if (Destroyed) throw new InvalidOperationException("Destroyed cosmetic write");
        if (color < 0 || color >= Math.Min(Palette.PlayerColors?.Length ?? 0, Palette.ShadowColors?.Length ?? 0))
            throw new InvalidOperationException("Invalid palette access");
        Colors.Add(color); DisplayedColor = color;
    }
}
public sealed class PlayerControl : NativeObject
{
    public static PlayerControl LocalPlayer;
    public static List<PlayerControl> AllPlayerControls = [];
    public bool AmOwner;
    public PlayerData Data = new();
    public CosmeticsLayer cosmetics = new();
    public PlayerOutfitType CurrentOutfitType;
    public PlayerOutfit DisguisedOutfit;
    public PlayerOutfit CurrentOutfit => CurrentOutfitType == PlayerOutfitType.Default ? Data?.DefaultOutfit : DisguisedOutfit;
    public readonly List<(double Time, byte Color)> Requests = [];
    public void CmdCheckColor(byte color)
    {
        if (!AmOwner || Pointer != LocalPlayer?.Pointer || Data == null || Data.Disconnected || Data.IsDead ||
            !MalumMenu.Utils.isLobby || MalumMenu.Utils.isInGame || MalumMenu.Utils.isFreePlay || CurrentOutfitType != PlayerOutfitType.Default)
            throw new InvalidOperationException("Invalid owner/lobby request");
        if (color >= Math.Min(Palette.PlayerColors?.Length ?? 0, Palette.ShadowColors?.Length ?? 0))
            throw new InvalidOperationException("Invalid color request");
        Requests.Add((UnityEngine.Time.realtimeSinceStartupAsDouble, color));
    }
}
public sealed class AmongUsClient : NativeObject { public static AmongUsClient Instance; }
public sealed class ShipStatus : NativeObject { public static ShipStatus Instance; }
public static class Palette
{
    public static object[] PlayerColors;
    public static object[] ShadowColors;
}
namespace UnityEngine
{
    public static class Application { public static bool isFocused; }
    public static class Time { public static double realtimeSinceStartupAsDouble; }
    public static class Mathf
    {
        public static int Min(int a, int b) => Math.Min(a, b);
        public static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);
    }
}
namespace BepInEx.Configuration
{
    public sealed class ConfigEntry<T> { public T Value; public ConfigEntry(T value) => Value = value; }
}
namespace MalumMenu
{
    public static class Utils { public static bool isClient, isLobby, isInGame, isFreePlay; }
    public static class MalumMenu
    {
        public static bool isPanicked;
        public static BepInEx.Configuration.ConfigEntry<float> rainbowColorInterval = new(0.6f);
        public static BepInEx.Configuration.ConfigEntry<float> lobbyColorInterval = new(3f);
        public static TestLogger Log = new();
    }
    public sealed class TestLogger
    {
        public readonly List<string> Warnings = [];
        public void LogWarning(object message) => Warnings.Add(message.ToString());
    }
}
