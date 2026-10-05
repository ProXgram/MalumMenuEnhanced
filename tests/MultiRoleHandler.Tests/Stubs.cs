namespace UnityEngine
{
    public class Object
    {
        private static long _nextPointer;
        public IntPtr Pointer { get; } = new(Interlocked.Increment(ref _nextPointer));
        public bool Destroyed { get; set; }
        public static implicit operator bool(Object value) => value is not null && !value.Destroyed;
        public static readonly List<Object> DestroyCalls = new();
        public static bool ThrowOnInstantiate;
        public static T Instantiate<T>(T prefab) where T : Object => Instantiate(prefab, null, false);
        public static T Instantiate<T>(T prefab, Transform parent) where T : Object => Instantiate(prefab, parent, false);
        public static T Instantiate<T>(T prefab, Transform parent, bool worldPositionStays) where T : Object
        {
            if (ThrowOnInstantiate) throw new InvalidOperationException("test instantiate failure");
            if (prefab is VitalsMinigame vitals)
            {
                var clone = new VitalsMinigame
                {
                    ThrowOnBegin = vitals.ThrowOnBegin,
                    ThrowOnForceClose = vitals.ThrowOnForceClose,
                    transform = new Transform { parent = parent }
                };
                VitalsMinigame.Instances.Add(clone);
                return (T)(Object)clone;
            }
            if (prefab is DetectiveRole detective)
            {
                var clone = new DetectiveRole
                {
                    Role = detective.Role, notesPrefab = detective.notesPrefab,
                    notesPageInfos = detective.notesPageInfos, deadPlayers = detective.deadPlayers,
                    Player = detective.Player, AbilityDistance = detective.AbilityDistance,
                    buttonManager = detective.buttonManager, secondaryButtonManager = detective.secondaryButtonManager,
                    abilityInfo = detective.abilityInfo, meetingAbilityInfo = detective.meetingAbilityInfo
                };
                clone.gameObject.Components.Add(clone);
                DetectiveRole.Instances.Add(clone);
                return (T)(Object)clone;
            }
            if (prefab is GameObject gameObject)
            {
                var clone = new GameObject();
                var source = gameObject.GetComponent<DetectiveNotesMinigame>();
                if (source is not null)
                {
                    var notes = new DetectiveNotesMinigame
                    {
                        gameObject = clone,
                        transform = new Transform { parent = parent },
                        ThrowOnBegin = source.ThrowOnBegin,
                        ThrowOnForceClose = source.ThrowOnForceClose,
                        ThrowOnOpenPage = source.ThrowOnOpenPage
                    };
                    clone.Components.Add(notes);
                    DetectiveNotesMinigame.Instances.Add(notes);
                }
                var suspectSource = gameObject.GetComponent<DetectiveNotesSuspectInterface>();
                if (suspectSource is not null)
                {
                    var view = new DetectiveNotesSuspectInterface
                    {
                        gameObject = clone, transform = new Transform { parent = parent }
                    };
                    clone.Components.Add(view);
                }
                return (T)(Object)clone;
            }
            if (prefab is SpriteRenderer sprite)
            {
                var clone = new SpriteRenderer
                {
                    transform = new Transform { parent = parent }, color = sprite.color,
                    enabled = sprite.enabled
                };
                clone.gameObject.Components.Add(clone);
                SpriteRenderer.Instances.Add(clone);
                return (T)(Object)clone;
            }
            throw new InvalidOperationException("unmodeled prefab type");
        }
        public static void Destroy(Object value)
        {
            DestroyCalls.Add(value);
            if (value is not null) value.Destroyed = true;
            if (value is GameObject gameObject)
                foreach (var component in gameObject.Components) component.Destroyed = true;
        }
    }
    public class GameObject : Object
    {
        public bool activeSelf = true;
        public readonly List<Object> Components = new();
        public void SetActive(bool active) => activeSelf = active;
        public T GetComponent<T>() where T : Object => Components.OfType<T>().FirstOrDefault();
    }
    public class Transform : Object
    {
        public Transform parent;
        public Vector3 localPosition;
        public Vector3 localScale = new(1, 1, 1);
        public Vector3 position
        {
            get => parent is null ? localPosition : parent.position + localPosition;
            set => localPosition = parent is null ? value : value - parent.position;
        }
        public bool IsChildOf(Transform other)
        {
            for (var current = this; current is not null; current = current.parent)
                if (ReferenceEquals(current, other)) return true;
            return false;
        }
    }
    public class Camera : Object
    {
        public static Camera main;
        public Transform transform = new();
    }
    public struct Vector3(float x, float y, float z)
    {
        public float x = x, y = y, z = z;
        public static Vector3 operator /(Vector3 value, float divisor) => new(value.x / divisor, value.y / divisor, value.z / divisor);
        public static Vector3 operator +(Vector3 left, Vector3 right) => new(left.x + right.x, left.y + right.y, left.z + right.z);
        public static Vector3 operator -(Vector3 left, Vector3 right) => new(left.x - right.x, left.y - right.y, left.z - right.z);
    }
    public readonly struct Vector2(float x, float y)
    {
        public readonly float x = x, y = y;
        public float magnitude => MathF.Sqrt(x * x + y * y);
        public static Vector2 operator -(Vector2 left, Vector2 right) => new(left.x - right.x, left.y - right.y);
        public static Vector2 operator /(Vector2 value, float divisor) => new(value.x / divisor, value.y / divisor);
        public static float Distance(Vector2 left, Vector2 right) => (left - right).magnitude;
    }
    public static class Mathf
    {
        public static float Abs(float value) => MathF.Abs(value);
        public static float Sign(float value) => MathF.Sign(value);
        public static int Clamp(int value, int minimum, int maximum) => Math.Clamp(value, minimum, maximum);
    }
    public static class Time { public static float unscaledTime; }
    public enum KeyCode { Escape }
    public static class Input
    {
        public static readonly HashSet<KeyCode> Down = new();
        public static bool GetKeyDown(KeyCode key) => Down.Contains(key);
    }
    public readonly struct Color(float r, float g, float b, float a = 1f)
    {
        public readonly float r = r, g = g, b = b, a = a;
    }
    public class SpriteRenderer : Object
    {
        public static readonly List<SpriteRenderer> Instances = new();
        public Transform transform = new();
        public GameObject gameObject = new();
        public Color color;
        public bool enabled = true;
        public Material material = new();
        public Sprite sprite;
    }
    public class Sprite : Object { }
    public class Material : Object
    {
        public readonly Dictionary<int, Color> Colors = new();
        public void SetColor(int key, Color value) => Colors[key] = value;
    }
}

namespace AmongUs.GameOptions
{
    public enum RoleTypes { Crewmate, Impostor, Scientist, Engineer, Tracker, Detective }
}

[Flags]
public enum MurderResultFlags { Succeeded = 1, FailedError = 2, FailedProtected = 4, DecisionByHost = 8 }

public class RoleBehaviour : UnityEngine.Object
{
    public AmongUs.GameOptions.RoleTypes Role;
    public T TryCast<T>() where T : RoleBehaviour => this as T;
}
public class ScientistRole : RoleBehaviour
{
    public VitalsMinigame VitalsPrefab;
}
public class DetectiveRole : RoleBehaviour
{
    public static readonly List<DetectiveRole> Instances = new();
    public UnityEngine.GameObject gameObject = new();
    public bool enabled = true;
    public PlayerControl Player;
    public UnityEngine.GameObject notesPrefab;
    public Il2CppSystem.Collections.Generic.List<DetectiveNotesPageInfo> notesPageInfos = new();
    public Il2CppSystem.Collections.Generic.List<NetworkedPlayerInfo> deadPlayers = new();
    public object buttonManager, secondaryButtonManager, abilityInfo, meetingAbilityInfo;
    public PlayerControl currentTarget;
    public DetectiveNotesMinigame notesMinigame;
    public int currentNotesIndex;
    public float AbilityDistance = 2f;
    public float GetAbilityDistance() => AbilityDistance;
    public void SetAbilityInfo() { }
    public string GetPlayerLocation(byte playerID, byte victimID) => "Native unmodified location";
}
public class DetectiveNotesPageInfo(NetworkedPlayerInfo victim)
{
    public NetworkedPlayerInfo victim = victim;
    public NetworkedPlayerInfo victimPlayer = victim;
    public string Location;
    public Il2CppSystem.Collections.Generic.List<DetectiveSuspect> suspects = new();
    public void SetLocation(string location) => Location = location;
}
public class DetectiveSuspect(NetworkedPlayerInfo player, object ability)
{
    public NetworkedPlayerInfo playerInfo = player;
    public object Ability = ability;
}
public class RoleManager : UnityEngine.Object
{
    public static RoleManager Instance;
    public List<RoleBehaviour> AllRoles = new();
}
public class GameOptionsManager
{
    public static GameOptionsManager Instance;
    public object CurrentGameOptions = new();
}
public class ShipStatus : UnityEngine.Object
{
    public static ShipStatus Instance;
    public float MapScale = 1f;
    public UnityEngine.Transform transform = new();
}
public class PlainShipRoom : UnityEngine.Object { public string RoomId; }
public class NetworkedPlayerInfo : UnityEngine.Object
{
    public class PlayerOutfit
    {
        public NetworkedPlayerInfo Owner;
        public string PlayerName;
        public int ColorId;
    }
    public Dictionary<PlayerOutfitType, PlayerOutfit> Outfits = new();
    public bool ThrowOnDefaultOutfitLookup;
    private string _playerName;
    public NetworkedPlayerInfo() => Outfits.Add(PlayerOutfitType.Default, new PlayerOutfit { Owner = this });
    private RoleBehaviour _role;
    public RoleBehaviour Role { get => _role; set { RoleAssignments++; _role = value; } }
    public int RoleAssignments;
    public bool IsDead;
    public bool Disconnected;
    public string PlayerName
    {
        get
        {
            if (ThrowOnDefaultOutfitLookup) throw new InvalidOperationException("unsafe native default-outfit indexer path");
            return _playerName;
        }
        set { _playerName = value; if (Outfits is not null && Outfits.TryGetValue(PlayerOutfitType.Default, out var outfit) && outfit is not null) outfit.PlayerName = value; }
    }
    public byte PlayerId;
    public PlayerControl Object;
    public string ColorName = "Blue";
    public string GetPlayerColorString(bool _ = false)
    {
        if (ThrowOnDefaultOutfitLookup) throw new InvalidOperationException("unsafe native default-outfit indexer path");
        return ColorName;
    }
}
public class PlayerControl : UnityEngine.Object
{
    public static PlayerControl LocalPlayer;
    public static List<PlayerControl> AllPlayerControls = new();
    public bool AmOwner;
    public PlayerOutfitType CurrentOutfitType = PlayerOutfitType.Default;
    public bool CanMove { get; set; } = true;
    private NetworkedPlayerInfo _data;
    private byte _playerId;
    public PlayerControl() => Data = new();
    public NetworkedPlayerInfo Data
    {
        get => _data;
        set { _data = value; if (value is not null) { value.Object = this; value.PlayerId = _playerId; } }
    }
    public byte PlayerId { get => _playerId; set { _playerId = value; if (_data is not null) _data.PlayerId = value; } }
    public ColliderObject Collider = new();
    public bool inVent;
    public bool onLadder;
    public bool inMovingPlat;
    public UnityEngine.Vector2 Position;
    public bool ThrowOnPosition;
    public UnityEngine.Vector2 GetTruePosition()
    {
        if (ThrowOnPosition) throw new InvalidOperationException("test position failure");
        return Position;
    }
    public void MurderPlayer(PlayerControl target, MurderResultFlags flags) { }
}
public class ColliderObject : UnityEngine.Object { public bool enabled = true; }
public class ChatController : UnityEngine.Object { public bool IsOpenOrOpening; }
public class HudManager : UnityEngine.Object
{
    public static HudManager Instance;
    public static bool InstanceExists => Instance;
    public bool IsIntroDisplayed;
    public ChatController Chat = new();
    public MapBehaviour Map = new();
    public readonly List<MapOptions> MapCalls = new();
    public Action OnToggleMap;
    public Func<bool> CanRenderMap;
    public bool ThrowOnToggleMap;
    public void ToggleMapVisible(MapOptions options)
    {
        OnToggleMap?.Invoke();
        if (ThrowOnToggleMap) throw new InvalidOperationException("test map-toggle failure");
        MapCalls.Add(options);
        if (CanRenderMap is not null && !CanRenderMap()) return;
        MapBehaviour.Instance = Map;
        Map.IsOpen = !Map.IsOpen;
    }
}
public class MapBehaviour : UnityEngine.Object
{
    public static MapBehaviour Instance;
    public UnityEngine.Transform transform = new();
    public bool IsOpen;
    public bool ThrowOnClose;
    public int CloseCalls;
    public UnityEngine.SpriteRenderer HerePoint = new();
    public void Close()
    {
        bool runOriginal = MalumMenu.MultiRoleDetectiveKeepOtherMaps.Prefix(this);
        if (runOriginal)
        {
            CloseCalls++;
            if (ThrowOnClose) throw new InvalidOperationException("test map-close failure");
            IsOpen = false;
        }
        MalumMenu.MultiRoleDetectiveKeepOtherMaps.Postfix(this, runOriginal);
    }
    public void FixedUpdate() { }
}
public class MapOptions
{
    public enum Modes { Normal }
    public Modes Mode;
}
public class Minigame : UnityEngine.Object
{
    public static Minigame Instance;
    public UnityEngine.GameObject gameObject = new();
    public UnityEngine.Transform transform = new();
    public int CloseCalls;
    public virtual void ForceClose()
    {
        CloseCalls++;
        Destroyed = true;
        if (ReferenceEquals(Instance, this)) Instance = null;
    }
}
public class VitalsMinigame : Minigame
{
    public static readonly List<VitalsMinigame> Instances = new();
    public bool ThrowOnBegin;
    public bool ThrowOnForceClose;
    public int BeginCalls;
    public object LastBeginPlayer;
    public TextObject BatteryText = new();
    public void Begin(object player)
    {
        BeginCalls++;
        LastBeginPlayer = player;
        Instance = this;
        if (ThrowOnBegin) throw new InvalidOperationException("test begin failure");
    }
    public override void ForceClose()
    {
        if (ThrowOnForceClose)
        {
            CloseCalls++;
            throw new InvalidOperationException("test close failure");
        }
        base.ForceClose();
    }
}
public class TextObject : UnityEngine.Object { public UnityEngine.GameObject gameObject = new(); public string text; }

public class DetectiveNotesMinigame : Minigame
{
    public static readonly List<DetectiveNotesMinigame> Instances = new();
    public bool ThrowOnBegin, ThrowOnForceClose, ThrowOnOpenPage;
    public int BeginCalls, OpenPageCalls;
    public int NativeCloseCalls;
    public int currentPageIndex;
    public int NativePageSetupCalls;
    public bool ThrowOnNativePageSetup;
    public NetworkedPlayerInfo NativeVictimRendered;
    public DetectiveNotesSuspectContainer[] suspectContainers = [new(), new(), new(), new()];
    public UnityEngine.GameObject noSuspectsPostIt = new();
    public object LastBeginPlayer;
    public DetectiveRole Associated;
    public DetectiveNotesPageInfo Page;
    public UnityEngine.GameObject mapFadeBackground = new();
    public void SetAssociatedDetective(DetectiveRole detective) => Associated = detective;
    public void OpenVictimLocationMap() { }
    public void SetUpCurrentPage()
    {
            NativePageSetupCalls++;
            if (Associated is not null && currentPageIndex >= 0 && currentPageIndex < Associated.notesPageInfos.Count)
            {
                var page = Associated.notesPageInfos[currentPageIndex];
                NativeVictimRendered = page.victimPlayer;
                if (page.suspects.Count > 0)
                    throw new InvalidOperationException("unsafe native struct-argument suspect rendering reached");
                if (ThrowOnNativePageSetup) throw new InvalidOperationException("test native page setup failure");
                noSuspectsPostIt.SetActive(true);
            }
            MalumMenu.MultiRoleDetectiveSafePage.Postfix(this);
    }
    public void OpenExistingPage(DetectiveRole detective, DetectiveNotesPageInfo page)
    {
        OpenPageCalls++; Associated = detective; Page = page;
        currentPageIndex = detective.notesPageInfos.IndexOf(page);
        if (ThrowOnOpenPage) throw new InvalidOperationException("test notebook page failure");
    }
    public void Begin(object player)
    {
        BeginCalls++; LastBeginPlayer = player; Instance = this;
        if (ThrowOnBegin) throw new InvalidOperationException("test notebook begin failure");
        SetUpCurrentPage();
    }
    public void Close()
    {
        NativeCloseCalls++;
        bool scoped = MalumMenu.MultiRoleDetectiveHandler.BeginNotesClose(this);
        try
        {
            if (MapBehaviour.Instance && MapBehaviour.Instance.IsOpen &&
                MalumMenu.MultiRoleDetectiveHandler.MayCloseMap(MapBehaviour.Instance)) MapBehaviour.Instance.Close();
        }
        finally { if (scoped) MalumMenu.MultiRoleDetectiveHandler.EndNotesClose(); }
    }
    public override void ForceClose()
    {
        if (ThrowOnForceClose) { CloseCalls++; throw new InvalidOperationException("test notebook close failure"); }
        base.ForceClose();
    }
}
public enum PlayerOutfitType { Default, Alternate }
public static class PlayerMaterial
{
    public enum MaskType { None, SimpleUI, ComplexUI }
    public const int BackColor = 1, BodyColor = 2, VisorColor = 3;
}
public static class Palette
{
    public static UnityEngine.Color VisorColor = new(0.5f, 0.7f, 0.8f);
    public static UnityEngine.Color[] PlayerColors = new UnityEngine.Color[18];
    public static string GetColorName(int colorId)
    {
        if (colorId < 0 || colorId >= PlayerColors.Length) throw new IndexOutOfRangeException("test unsafe native palette index");
        return "Color " + colorId;
    }
}
public class PlayerRenderer : UnityEngine.Object
{
    public int MaskLayer;
    public NetworkedPlayerInfo RenderedPlayer;
    public NetworkedPlayerInfo.PlayerOutfit RenderedOutfit;
    public UnityEngine.GameObject gameObject = new();
    public bool IncludePet, ForceAlive, IsDead;
    public int DataUpdateCalls, OutfitUpdateCalls;
    public object UpdateCallback;
    public void SetMaskLayer(int layer) => MaskLayer = layer;
    public void UpdateFromPlayerData(NetworkedPlayerInfo info, PlayerOutfitType _, PlayerMaterial.MaskType __, bool includePet, object callback, bool forceAlive)
    {
        DataUpdateCalls++;
        RenderedPlayer = info; IncludePet = includePet; UpdateCallback = callback; ForceAlive = forceAlive;
    }
    public void UpdateFromPlayerOutfit(NetworkedPlayerInfo.PlayerOutfit outfit, PlayerMaterial.MaskType _, bool isDead, bool includePet, object callback, bool forceAlive)
    {
        OutfitUpdateCalls++; RenderedOutfit = outfit; RenderedPlayer = outfit.Owner;
        IsDead = isDead; IncludePet = includePet; UpdateCallback = callback; ForceAlive = forceAlive;
    }
}
public class DetectiveNotesSuspectInterface : UnityEngine.Object
{
    public UnityEngine.GameObject gameObject = new();
    public UnityEngine.Transform transform = new();
    public UnityEngine.GameObject container = new(), deadImage = new();
    public PlayerRenderer player = new();
    public TextObject playerName = new(), playerColor = new(), locationName = new(), numberedText = new();
    public int ClearCalls;
    public void Clear()
    {
        ClearCalls++; container.SetActive(false); player.RenderedPlayer = null;
        playerName.text = playerColor.text = locationName.text = numberedText.text = null;
    }
    public void SetPlayerInfo(DetectiveSuspect suspect, NetworkedPlayerInfo victim, int index, int maskLayer) { }
}
public class DetectiveNotesSuspectContainer : UnityEngine.Object
{
    public UnityEngine.GameObject gameObject = new();
    public UnityEngine.Transform transform = new();
    public UnityEngine.SpriteRenderer BGSprite = new();
    public UnityEngine.Sprite activeBGSprite = new(), inactiveBGSprite = new();
    public TextObject SuspectPlaceholderText = new();
    public UnityEngine.GameObject SuspectPrefab = new();
    public DetectiveNotesSuspectInterface suspectInterface;
    public int MaskLayer;
    public DetectiveNotesSuspectContainer()
    {
        SuspectPrefab.Components.Add(new DetectiveNotesSuspectInterface { gameObject = SuspectPrefab });
    }
}

namespace Il2CppSystem.Collections.Generic
{
    public class List<T> : System.Collections.Generic.List<T> { }
}

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class HarmonyPatch : Attribute
    {
        public Type DeclaringType;
        public string MethodName;
        public HarmonyPatch(Type type, string methodName) { DeclaringType = type; MethodName = methodName; }
        public HarmonyPatch(Type type, string methodName, Type[] _) : this(type, methodName) { }
        public HarmonyPatch(Type type, string methodName, MethodType _) : this(type, methodName) { }
    }
    public enum MethodType { Normal, Getter, Setter }
    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPriority(int priority) : Attribute { public int Priority = priority; }
    public static class Priority { public const int First = 0; }
}

namespace MalumMenu
{
    public static class MenuUI { public static bool isGUIActive; }
    public static class CheatToggles { public static bool multiRole; }
    public static class Utils
    {
        // isHost deliberately exists but stays false in every guest fixture.
        public static bool isHost, isInGame, isFreePlay, isNormalGame, isMeeting, isExiling;
        public static PlainShipRoom CurrentRoom;
        public static Func<UnityEngine.Vector2, PlainShipRoom> RoomResolver;
        public static PlainShipRoom GetRoomFromPosition(UnityEngine.Vector2 position) => RoomResolver is null ? CurrentRoom : RoomResolver(position);
    }
    public class TestLog
    {
        public readonly List<string> Warnings = new();
        public readonly List<string> Information = new();
        public void LogWarning(string message) => Warnings.Add(message);
        public void LogInfo(string message) => Information.Add(message);
    }
    public static class MalumMenu
    {
        public static bool isPanicked;
        public static TestLog Log = new();
    }
}
