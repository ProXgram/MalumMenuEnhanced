// Deliberately minimal offline substitutes. Production handler code is linked
// unchanged; none of these types open sockets or invoke the installed game.
using AmongUs.GameOptions;

namespace AmongUs.GameOptions
{
    public enum RoleTypes { Crewmate, Engineer, Scientist, Impostor, Shapeshifter, Phantom, Viper }
    public enum RoleTeamTypes { Crewmate, Impostor }
    public sealed class GameOptionsManager
    {
        public static GameOptionsManager Instance = new();
        public object CurrentGameOptions = new();
    }
}

public class GameObjectStub
{
    public bool Destroyed;
    public IntPtr Pointer;
    public static implicit operator bool(GameObjectStub value) => value is not null && !value.Destroyed;
    public static bool operator !(GameObjectStub value) => !(bool)value;
}

public sealed class RoleBehaviour : GameObjectStub
{
    public RoleTeamTypes TeamType;
    public RoleTypes Role;
}

public sealed class NetworkedPlayerInfo
{
    public bool IsDead;
    public bool Disconnected;
    public RoleBehaviour Role;
    public RoleTypes RoleType => Role.Role;
}

public sealed class PlayerControl : GameObjectStub
{
    public static PlayerControl LocalPlayer;
    public NetworkedPlayerInfo Data;
    public void RpcMurderPlayer() { }
}

public sealed class KillButton
{
    public void DoClick() { }
}

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type declaringType, string methodName) { }
    }
}

public sealed class RoleManager
{
    private static long _rolePointer = 10_000;
    public static RoleManager Instance = new();
    public int SetRoleCalls;
    public RoleTypes? ThrowBeforeRole;
    public RoleTypes? ThrowAfterRole;
    public RoleTypes? IgnoreRole;

    public void SetRole(PlayerControl player, RoleTypes role)
    {
        SetRoleCalls++;
        if (role == ThrowBeforeRole) throw new InvalidOperationException("Simulated native setter failure before assignment.");
        if (role == IgnoreRole) return;
        player.Data.Role = MakeRole(role);
        if (role == ThrowAfterRole) throw new InvalidOperationException("Simulated native setter failure after assignment.");
    }

    public static RoleBehaviour MakeRole(RoleTypes role) => new()
    {
        Pointer = new IntPtr(++_rolePointer),
        Role = role,
        TeamType = role is RoleTypes.Impostor or RoleTypes.Shapeshifter or RoleTypes.Phantom or RoleTypes.Viper
            ? RoleTeamTypes.Impostor : RoleTeamTypes.Crewmate
    };
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
        public static bool alwaysImpostor;
        public static bool setFakeRole;
    }

    public static class PlayerPickMenu
    {
        public static PickMenu playerpickMenu;
        public static Action customAction;
    }

    public sealed class PickMenu
    {
        public int CloseCalls;
        public void Close() => CloseCalls++;
    }

    public static class MalumMenu
    {
        public static TestLogger Log = new();
    }

    public static class ConsoleUI
    {
        public static readonly List<string> Messages = [];
        public static void Log(string message) => Messages.Add(message);
    }

    public sealed class TestLogger
    {
        public readonly List<string> Info = [];
        public readonly List<string> Warning = [];
        public readonly List<string> Errors = [];
        public void LogInfo(object message) => Info.Add(message.ToString());
        public void LogWarning(object message) => Warning.Add(message.ToString());
        public void LogError(object message) => Errors.Add(message.ToString());
        public IEnumerable<string> Entries => Info.Concat(Warning).Concat(Errors);
    }
}
