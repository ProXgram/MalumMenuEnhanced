// These local types replace game/Unity/config APIs. No game or network library
// is referenced; destroyed physics throws if the handler tries to access it.
public class UnityObjectStub
{
    private static int _nextPointer = 100;
    public IntPtr Pointer = new(++_nextPointer);
    public bool Destroyed;
    public static implicit operator bool(UnityObjectStub value) => value is not null && !value.Destroyed;
    public static bool operator !(UnityObjectStub value) => !(bool)value;
}

public sealed class PlayerPhysics : UnityObjectStub
{
    private float _speed;
    public int Reads;
    public int Writes;
    public float PeekSpeed => _speed;
    public float Speed
    {
        get { if (Destroyed) throw new InvalidOperationException("Destroyed physics read"); Reads++; return _speed; }
        set { if (Destroyed) throw new InvalidOperationException("Destroyed physics write"); Writes++; _speed = value; }
    }
    public PlayerPhysics(float speed = 1.75f) => _speed = speed;
}

public sealed class NetworkedPlayerInfo { public bool IsDead; public bool Disconnected; }
public sealed class PlayerControl : UnityObjectStub
{
    public static PlayerControl LocalPlayer;
    public bool AmOwner = true;
    public NetworkedPlayerInfo Data = new();
    public bool CanMove = true;
    public bool inVent;
    public bool onLadder;
    public bool inMovingPlat;
    public PlayerPhysics MyPhysics = new();
}
public sealed class ShipStatus : UnityObjectStub { public static ShipStatus Instance; }
public sealed class ChatController : UnityObjectStub { public bool IsOpenOrOpening; }
public sealed class HudManager : UnityObjectStub
{
    public static HudManager Existing;
    public static int Acquisitions;
    public static int Creations;
    public static bool InstanceExists => Existing;
    public static HudManager Instance
    {
        get { Acquisitions++; if (!Existing) { Existing = new(); Creations++; } return Existing; }
    }
    public bool IsIntroDisplayed;
    public ChatController Chat = new();
}

namespace UnityEngine
{
    public enum KeyCode { None, LeftShift, RightShift, A, F1, Mouse0 }
    public static class Application { public static bool isFocused = true; }
    public static class Input
    {
        public static readonly HashSet<KeyCode> Pressed = [];
        public static KeyCode LastRequested;
        public static int Calls;
        public static bool GetKey(KeyCode key) { LastRequested = key; Calls++; return Pressed.Contains(key); }
    }
    public static class Mathf
    {
        public static float Abs(float value) => Math.Abs(value);
        public static float Min(float x, float y) => Math.Min(x, y);
        public static float Max(float x, float y) => Math.Max(x, y);
        public static float Sign(float value) => value >= 0 ? 1f : -1f;
        public static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);
    }
}
namespace BepInEx.Configuration
{
    public sealed class ConfigEntry<T> { public T Value; public ConfigEntry(T value) => Value = value; }
}
namespace MalumMenu
{
    public static class MovementAutomation { public static bool Active; }
    public static class Utils
    {
        public static bool isClient;
        public static bool isInGame;
        public static bool isFreePlay;
        public static bool isLobby;
        public static bool isMeeting;
        public static bool isExiling;
        public static UnityEngine.KeyCode StringToKeycode(string value) =>
            Enum.TryParse<UnityEngine.KeyCode>(value, true, out var key) ? key : UnityEngine.KeyCode.None;
    }
    public static class CheatToggles { public static bool sprint; }
    public static class MenuUI { public static bool isGUIActive; }
    public static class MalumMenu
    {
        public static bool isPanicked;
        public static BepInEx.Configuration.ConfigEntry<string> sprintKeybind = new("LeftShift");
        public static BepInEx.Configuration.ConfigEntry<float> sprintMultiplier = new(2f);
        public static TestLogger Log = new();
    }
    public sealed class TestLogger
    {
        public List<string> Errors = [];
        public List<string> Warnings = [];
        public void LogInfo(object _) { }
        public void LogWarning(object message) => Warnings.Add(message.ToString());
        public void LogError(object message) => Errors.Add(message.ToString());
    }
}
