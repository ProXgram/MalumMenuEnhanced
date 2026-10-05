// Offline substitutes for native identities and properties. Production code
// is linked unchanged. Any modeled network action fails the harness.
using AmongUs.GameOptions;

namespace AmongUs.GameOptions
{
    public enum RoleTypes { Crewmate, Judge, Impostor }
    public enum RoleTeamTypes { Crewmate, Impostor }
    public sealed class GameOptionsManager
    {
        public static GameOptionsManager Instance = new();
        public object CurrentGameOptions = new();
    }
}

public class UnityObjectStub
{
    public bool Destroyed;
    public IntPtr Pointer;
    public static implicit operator bool(UnityObjectStub value) => value is not null && !value.Destroyed;
    public static bool operator !(UnityObjectStub value) => !(bool)value;
    public T TryCast<T>() where T : UnityObjectStub => this as T;
}

public class RoleBehaviour : UnityObjectStub
{
    public RoleTypes Role;
    public RoleTeamTypes TeamType;
    public PlayerControl Player;
}

public sealed class JudgeRole : RoleBehaviour
{
    public bool HasAnOverruleUse;
    public bool HasAlreadyOverruledThisMeeting;
    // Other native-state stand-ins are sentinels: this feature may touch none.
    public ushort OverruleNonce = 42;
    public byte TargetPlayerId = 7;
    public bool TaskGateUnlocked;
    public int RequiredTasks = 5;

    public void OnMeetingStart() => HasAlreadyOverruledThisMeeting = false;
    public void ConsumeOverruleVotesUsage() => HasAnOverruleUse = false;
}

public sealed class NetworkedPlayerInfo
{
    public bool IsDead;
    public bool Disconnected;
    public RoleBehaviour Role;
    public RoleTypes RoleType => Role.Role;
}

public sealed class PlayerControl : UnityObjectStub
{
    public static PlayerControl LocalPlayer;
    public bool AmOwner = true;
    public byte PlayerId = 1;
    public NetworkedPlayerInfo Data;

    public void RpcSetRole(RoleTypes _) => NetworkActions.Forbidden();
    public void RpcQueueOverruleVotes() => NetworkActions.Forbidden();
}

public sealed class MeetingHud : UnityObjectStub
{
    public static MeetingHud Instance;
    public void RpcQueueOverruleVotes() => NetworkActions.Forbidden();
    public void RpcVotingComplete() => NetworkActions.Forbidden();
}

public static class NetworkActions
{
    public static int Attempts;
    public static void Forbidden()
    {
        Attempts++;
        throw new InvalidOperationException("Offline harness forbids all network actions.");
    }
}

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type declaringType, string methodName) { }
    }
}

namespace MalumMenu
{
    public static class Utils
    {
        public static bool isClient;
        public static bool isLobby;
        public static bool isNormalGame;
        public static bool isFreePlay;
        public static bool isInGame;
        public static bool isHost;
    }

    public static class CheatToggles
    {
        public static bool infiniteJudge;
    }

    public static class ConsoleUI
    {
        public static readonly List<string> Messages = [];
        public static void Log(string message) => Messages.Add(message);
    }

    public static class MalumMenu
    {
        public static TestLogger Log = new();
    }

    public sealed class TestLogger
    {
        public readonly List<string> Info = [];
        public readonly List<string> Warning = [];
        public readonly List<string> Errors = [];
        public void LogInfo(object message) => Info.Add(message.ToString());
        public void LogWarning(object message) => Warning.Add(message.ToString());
        public void LogError(object message) => Errors.Add(message.ToString());
    }
}
