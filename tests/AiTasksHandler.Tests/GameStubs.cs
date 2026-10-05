public class UnityObjectStub
{
    private static int _nextPointer = 100;
    public bool Destroyed;
    public IntPtr Pointer = new(++_nextPointer);
    public static implicit operator bool(UnityObjectStub value) => value is not null && !value.Destroyed;
    public static bool operator !(UnityObjectStub value) => !(bool)value;
    public T TryCast<T>() where T : UnityObjectStub => this as T;
}

public enum RoleTeamTypes { Crewmate, Impostor }
public enum TaskTypes { FixWiring, SwipeCard, InspectSample, RunDiagnostics, RebootWifi, DevelopPhotos, MonitorMushroom }
public sealed class RoleBehaviour : UnityObjectStub
{
    public RoleTeamTypes TeamType = RoleTeamTypes.Crewmate;
    public bool TasksCountTowardProgress = true;
}
public sealed class TaskInfo { public uint Id; public bool Complete; }
public sealed class NetworkedPlayerInfo
{
    public bool Disconnected, IsDead;
    public int FindTaskCalls;
    public RoleBehaviour Role = new();
    public readonly Dictionary<uint, TaskInfo> Assigned = new();
    public List<TaskInfo> Tasks => Assigned.Values.ToList();
    public TaskInfo FindTaskById(uint id)
    {
        FindTaskCalls++;
        return Assigned.GetValueOrDefault(id);
    }
}
public sealed class PlayerControl : UnityObjectStub
{
    public static PlayerControl LocalPlayer;
    public bool AmOwner = true, CanMove = true;
    public bool inVent, onLadder, inMovingPlat;
    public NetworkedPlayerInfo Data = new();
    public List<PlayerTask> myTasks = new();
    public UnityEngine.Vector2 Position;
    public object Collider = new();
    public UnityEngine.Vector2 GetTruePosition() => Position;
}
public class PlayerTask : UnityObjectStub
{
    public uint Id;
    public PlayerControl Owner;
    public bool Emergency;
    public Minigame MinigamePrefab;
    public static bool TaskIsEmergency(PlayerTask task) => task.Emergency;
}
public sealed class NormalPlayerTask : PlayerTask
{
    public enum TimerState { NotStarted, Started, Finished }
    public int MaxStep = 1, taskStep, StepsCalled;
    public bool FailAdvance, ThrowAdvance;
    public TaskTypes TaskType = TaskTypes.FixWiring;
    public TimerState TimerStarted;
    public float TaskTimer;
    public bool IsComplete => taskStep >= MaxStep;
    public bool ValidConsole(Console console) => console.ValidAtStep == taskStep && console.TaskId == Id;
    public void NextStep()
    {
        StepsCalled++;
        if (ThrowAdvance) throw new InvalidOperationException("Injected native failure");
        if (FailAdvance) return;
        taskStep++;
        if (IsComplete) Owner.Data.Assigned[Id].Complete = true;
    }
}
public sealed class Console : UnityObjectStub
{
    public int ValidAtStep;
    public uint TaskId;
    public bool onlyFromBelow, checkWalls;
    public float UsableDistance = 1f;
    public bool CanBeUsed = true, CouldBeUsed = true;
    public Func<UnityEngine.Vector2, bool> UseCheck = _ => true;
    public UnityEngine.Transform transform = new();
    public float CanUse(NetworkedPlayerInfo data, out bool canUse, out bool couldUse)
    {
        var distance = UnityEngine.Vector2.Distance(PlayerControl.LocalPlayer.Position, transform.position);
        couldUse = CouldBeUsed;
        canUse = CanBeUsed && UseCheck(PlayerControl.LocalPlayer.Position) && distance <= UsableDistance;
        return distance;
    }
}
public sealed class ShipStatus : UnityObjectStub
{
    public static ShipStatus Instance;
    public Console[] AllConsoles;
}
public sealed class HudManager : UnityObjectStub
{
    public static bool InstanceExists = true;
    public static int Acquisitions;
    public static HudManager Existing = new();
    public static HudManager Instance { get { Acquisitions++; return Existing; } }
    public bool IsIntroDisplayed;
    public ChatController Chat = new();
}
public sealed class ChatController : UnityObjectStub { public bool IsOpenOrOpening; }
public class Minigame : UnityObjectStub { public static Minigame Instance; }
public sealed class SampleMinigame : Minigame { public float TimePerStep = 60; }
public sealed class DiagnosticGame : Minigame { public float TimePerStep = 90; }
public sealed class WifiGame : Minigame { public const int WaitDuration = 60; }
public sealed class GameOptionsManager
{
    public static GameOptionsManager Instance = new();
    public object CurrentGameOptions = new();
}
public static class PhysicsHelpers
{
    public static bool Blocked;
    public static bool AnythingBetween(object collider, UnityEngine.Vector2 from, UnityEngine.Vector2 to, int mask, bool flag) => Blocked;
}
public static class Constants { public static int ShipOnlyMask = 1; }

namespace UnityEngine
{
    public static class Time { public static double realtimeSinceStartupAsDouble; }
    public static class Application { public static bool isFocused = true; }
    public sealed class Transform { public Vector2 position; }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public float sqrMagnitude => x * x + y * y;
        public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.x - b.x, a.y - b.y);
        public static Vector2 operator *(Vector2 a, float n) => new(a.x * n, a.y * n);
        public static float Distance(Vector2 a, Vector2 b) => MathF.Sqrt((a - b).sqrMagnitude);
    }
    public static class Mathf
    {
        public const float PI = MathF.PI;
        public static float Min(float a, float b) => MathF.Min(a, b);
        public static float Max(float a, float b) => MathF.Max(a, b);
        public static float Sin(float a) => MathF.Sin(a);
        public static float Cos(float a) => MathF.Cos(a);
    }
}

namespace MalumMenu
{
    public static class Utils
    {
        public static bool isClient = true, isInGame = true, isFreePlay, isNormalGame = true, isMeeting, isExiling;
    }
    public static class MenuUI { public static bool isGUIActive; }
    public static class MalumMenu { public static bool isPanicked; public static TestLogger Log = new(); }
    public sealed class TestLogger { public List<string> Warnings = new(); public void LogWarning(string message) => Warnings.Add(message); }
    public static class NavigationRouter
    {
        public static Func<UnityEngine.Vector2, bool> StandCheck = _ => true;
        public static bool CanStand(UnityEngine.Vector2 point) => StandCheck(point);
    }
}
