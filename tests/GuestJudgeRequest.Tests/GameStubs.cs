// Recorder stubs only: these methods never connect to a game or network.
using AmongUs.GameOptions;

namespace AmongUs.GameOptions
{
    public enum RoleTypes { Crewmate, Engineer, Scientist, Judge, Impostor }
    public enum RoleTeamTypes { Crewmate, Impostor, Unknown }
    public sealed class GameOptionsManager
    {
        public static GameOptionsManager Instance;
        public object CurrentGameOptions;
    }
}

public class UnityObjectStub
{
    public IntPtr Pointer;
    public bool Destroyed;
    public static implicit operator bool(UnityObjectStub value) => value is not null && !value.Destroyed;
    public static bool operator !(UnityObjectStub value) => !(bool)value;
}

public sealed class RoleBehaviour : UnityObjectStub
{
    public RoleTypes Role;
    public RoleTeamTypes TeamType;
    public bool IsValidTarget(NetworkedPlayerInfo _) => true;
}

public sealed class NetworkedPlayerInfo
{
    public byte PlayerId;
    public int OwnerId;
    public bool IsDead;
    public bool Disconnected;
    public RoleBehaviour Role;
    public RoleTypes RoleType => Role.Role;
}

public sealed class PlayerControl : UnityObjectStub
{
    public static PlayerControl LocalPlayer;
    public bool AmOwner = true;
    public bool CanMove = true;
    public byte PlayerId;
    public NetworkedPlayerInfo Data;

    public void RpcSetRole(RoleTypes role, bool canOverride)
    {
        Calls.RoleRequests.Add(new(this, role, canOverride));
        Calls.OnRoleRequest?.Invoke();
    }

    public void RpcMurderPlayer(PlayerControl target, bool _) => Calls.ForbiddenKill();
}

public readonly record struct RoleRequest(PlayerControl Actor, RoleTypes Role, bool CanOverride);

public static class Calls
{
    public static readonly List<RoleRequest> RoleRequests = [];
    public static Action OnRoleRequest;
    public static int KillRequests;
    public static void ForbiddenKill()
    {
        KillRequests++;
        throw new InvalidOperationException("Kill calls must be excluded in the Judge experiment.");
    }
}

public sealed class AmongUsClient : UnityObjectStub
{
    public static AmongUsClient Instance;
    public int GameId;
    public int HostId;
    public bool IsGamePublic;
}

public sealed class ShipStatus : UnityObjectStub { public static ShipStatus Instance; }

public sealed class GameData : UnityObjectStub
{
    public static GameData Instance;
    public List<NetworkedPlayerInfo> AllPlayers;
}

public sealed class HudManager : UnityObjectStub
{
    public static HudManager Instance;
    public bool IsIntroDisplayed;
    public int KillButtonReads;
    public KillButton KillButton
    {
        get { KillButtonReads++; return new(); }
    }
}

public sealed class KillButton : UnityObjectStub
{
    public bool isActiveAndEnabled = true;
    public StubGameObject gameObject = new();
    public bool IsOnCooldown;
    public PlayerControl Target;
}

public sealed class StubGameObject { public bool activeInHierarchy = true; }

namespace InnerNet
{
    public static class InnerNetClient
    {
        public const int ServerOwned = -2;
        public const int HostInherit = -1;
    }
}

namespace MalumMenu
{
    public static class Utils
    {
        public static bool isOnlineGame;
        public static bool isInGame;
        public static bool isHost;
        public static bool isNormalGame;
        public static bool isMeeting;
        public static bool isExiling;
    }

    public static class CheatToggles
    {
        public static bool alwaysImpostor;
        public static bool setFakeRole;
        public static bool killReach;
        public static bool killAnyone;
        public static bool killVanished;
        public static bool noKillCd;
    }

    public static class LocalImpostorHandler
    {
        public static bool BlockOwnKill(PlayerControl _) => throw new InvalidOperationException("Judge build entered a kill helper.");
    }

    public static class MalumMenu { public static TestLogger Log = new(); }
    public sealed class TestLogger
    {
        public readonly List<string> Info = [];
        public readonly List<string> Errors = [];
        public void LogInfo(object message) => Info.Add(message.ToString());
        public void LogError(object message) => Errors.Add(message.ToString());
    }
}
