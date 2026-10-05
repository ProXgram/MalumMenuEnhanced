// The HUD getter models the native singleton's side effect: asking for Instance
// can create gameplay UI. No installed game, Unity, or network code is invoked.
public class UnityObjectStub
{
    public bool Destroyed;
    public IntPtr Pointer;
    public static implicit operator bool(UnityObjectStub value) => value is not null && !value.Destroyed;
    public static bool operator !(UnityObjectStub value) => !(bool)value;
    public T TryCast<T>() where T : UnityObjectStub => this as T;
}

public enum RoleTeamTypes { Crewmate, Impostor }

public sealed class RoleBehaviour : UnityObjectStub
{
    public RoleTeamTypes TeamType = RoleTeamTypes.Crewmate;
    public bool TasksCountTowardProgress = true;
}

public sealed class NetworkedPlayerInfo
{
    public bool Disconnected;
    public RoleBehaviour Role = new();
    public TaskInfo FindTaskById(uint id) => null;
}

public sealed class TaskInfo { public bool Complete; }

public sealed class PlayerControl : UnityObjectStub
{
    public static PlayerControl LocalPlayer;
    public bool AmOwner = true;
    public NetworkedPlayerInfo Data = new();
    public int TaskListReads;
    public List<PlayerTask> myTasks
    {
        get { TaskListReads++; return []; }
    }
}

public sealed class ShipStatus : UnityObjectStub
{
    public static ShipStatus Instance;
}

public sealed class HudManager : UnityObjectStub
{
    public static int Acquisitions;
    public static int Creations;
    public static HudManager Existing;
    public static bool ReturnMissing;
    public static HudManager Instance
    {
        get
        {
            Acquisitions++;
            if (ReturnMissing) return null;
            if (Existing == null) { Creations++; Existing = new(); }
            return Existing;
        }
    }
    public bool IsIntroDisplayed;
}

public class PlayerTask : UnityObjectStub
{
    public uint Id;
    public PlayerControl Owner;
    public bool IsComplete;
    public static bool TaskIsEmergency(PlayerTask _) => false;
}

public sealed class NormalPlayerTask : PlayerTask
{
    public int MaxStep;
    public int taskStep;
    public string TaskType;
    public void NextStep() => throw new InvalidOperationException("Task stepping is outside this HUD acquisition regression.");
}

public sealed class GameOptionsManager
{
    public static GameOptionsManager Instance;
    public object CurrentGameOptions;
}

namespace UnityEngine
{
    public static class Time { public static double realtimeSinceStartupAsDouble; }
}

namespace MalumMenu
{
    public enum MovementMode { Off, AiTasks }
    public static class MovementAutomation { public static MovementMode Mode; }
    public static class Utils
    {
        public static bool isClient;
        public static bool isInGame;
        public static bool isFreePlay;
        public static bool isNormalGame;
        public static bool isMeeting;
        public static bool isExiling;
    }
    public static class CheatToggles { public static bool automaticTasks; }
    public static class MalumMenu { public static TestLogger Log = new(); }
    public sealed class TestLogger
    {
        public List<string> Errors = [];
        public void LogInfo(object _) { }
        public void LogError(object message) => Errors.Add(message.ToString());
    }
}
