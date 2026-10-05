using UnityEngine;

// Local contracts only. The harness references no game, network or Unity library.
public class NativeObject
{
    private static int _next = 10;
    public IntPtr Pointer = new(++_next);
    public bool Destroyed;
    public static implicit operator bool(NativeObject value) => value is not null && !value.Destroyed;
    public static bool operator !(NativeObject value) => !(bool)value;
}

public sealed class Transform { public Vector3 position; }
public sealed class Collider2D : NativeObject
{
    private bool _enabled = true;
    public int EnabledWrites;
    public bool enabled
    {
        get { if (Destroyed) throw new InvalidOperationException("Destroyed collider read"); return _enabled; }
        set { if (Destroyed) throw new InvalidOperationException("Destroyed collider write"); _enabled = value; EnabledWrites++; }
    }
}
public sealed class PlayerData
{
    public bool IsDead;
    public bool Disconnected;
    public string PlayerName = "Fixture player";
}
public sealed class PlayerControl : NativeObject
{
    public static PlayerControl LocalPlayer;
    public static List<PlayerControl> AllPlayerControls = [];
    public bool AmOwner;
    public byte PlayerId;
    public PlayerData Data = new();
    public bool CanMove = true;
    public bool inVent, onLadder, inMovingPlat;
    public PlayerPhysics MyPhysics;
    public Collider2D Collider = new();
    public CustomNetworkTransform NetTransform;
    public Transform transform = new();
    public Vector2 TruePositionOffset;
    public Vector2 GetTruePosition() => (Vector2)transform.position + TruePositionOffset;
    public PlayerControl(byte id, bool owner, float speed = 1.75f)
    {
        PlayerId = id; AmOwner = owner; Data.PlayerName = "Player " + id;
        MyPhysics = new(this, speed); NetTransform = new(this);
    }
}
public sealed class PlayerPhysics : NativeObject
{
    public PlayerControl myPlayer;
    public bool AmOwner;
    private float _speed;
    public int SpeedWrites, MovementWrites;
    public Vector2 LastInput, PhysicalVelocity;
    public float PeekSpeed => _speed;
    public float SpeedFactor = 1f;
    public float TrueSpeed => Speed * SpeedFactor;
    public float Speed
    {
        get { if (Destroyed) throw new InvalidOperationException("Destroyed speed read"); return _speed; }
        set { if (Destroyed) throw new InvalidOperationException("Destroyed speed write"); _speed = value; SpeedWrites++; }
    }
    public PlayerPhysics(PlayerControl player, float speed)
    { myPlayer = player; AmOwner = player.AmOwner; _speed = speed; }
    public void SetNormalizedVelocity(Vector2 direction)
    {
        if (Destroyed) throw new InvalidOperationException("Destroyed movement write");
        if (!AmOwner || !myPlayer || !myPlayer.AmOwner) throw new InvalidOperationException("Foreign movement write");
        if (!float.IsFinite(direction.x) || !float.IsFinite(direction.y) || direction.sqrMagnitude > 1.00001f)
            throw new InvalidOperationException("Unbounded movement input");
        MovementWrites++; LastInput = direction;
        // Model signed-speed multiplication to exercise inverted-control compensation.
        // This is a local contract, not proof of native GameAssembly execution.
        PhysicalVelocity = direction * TrueSpeed;
    }
}
public sealed class CustomNetworkTransform(PlayerControl owner) : NativeObject
{
    public readonly List<(double Time, Vector2 Position, Vector2 From)> Snaps = [];
    public void RpcSnapTo(Vector2 point)
    {
        if (Destroyed) throw new InvalidOperationException("Destroyed transform snap");
        if (!owner.AmOwner) throw new InvalidOperationException("Foreign snap");
        Snaps.Add((Time.realtimeSinceStartupAsDouble, point, owner.transform.position));
        owner.transform.position = point;
    }
}
public sealed class ShipStatus : NativeObject { public static ShipStatus Instance; }
public sealed class ChatController : NativeObject { public bool IsOpenOrOpening; }
public interface IVirtualJoystick { Vector2 DeltaL { get; } }
public sealed class FixtureJoystick : IVirtualJoystick { public Vector2 Value; public Vector2 DeltaL => Value; }
public sealed class HudManager : NativeObject
{
    public static HudManager Existing;
    public static bool InstanceExists => Existing;
    public static HudManager Instance => Existing ?? throw new InvalidOperationException("Unexpected HUD creation");
    public bool IsIntroDisplayed;
    public ChatController Chat = new();
    public IVirtualJoystick joystick = new FixtureJoystick();
}

namespace UnityEngine
{
    public struct Vector2(float x, float y)
    {
        public float x = x, y = y;
        public static Vector2 zero => new(0, 0);
        public static Vector2 down => new(0, -1);
        public float sqrMagnitude => x * x + y * y;
        public float magnitude => MathF.Sqrt(sqrMagnitude);
        public Vector2 normalized => magnitude > 0 ? this / magnitude : zero;
        public void Normalize() => this = normalized;
        public static float Distance(Vector2 a, Vector2 b) => (a - b).magnitude;
        public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.x - b.x, a.y - b.y);
        public static Vector2 operator -(Vector2 a) => new(-a.x, -a.y);
        public static Vector2 operator *(Vector2 a, float scale) => new(a.x * scale, a.y * scale);
        public static Vector2 operator /(Vector2 a, float scale) => new(a.x / scale, a.y / scale);
        public override string ToString() => $"({x}, {y})";
    }
    public struct Vector3(float x, float y, float z = 0)
    {
        public float x = x, y = y, z = z;
        public static implicit operator Vector2(Vector3 point) => new(point.x, point.y);
        public static implicit operator Vector3(Vector2 point) => new(point.x, point.y, 0);
    }
    public static class Application { public static bool isFocused; }
    public static class Time { public static double realtimeSinceStartupAsDouble; public static float fixedDeltaTime = 0.02f; }
    public static class Mathf
    {
        public static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);
        public static float Abs(float value) => MathF.Abs(value);
        public static float Min(float a, float b) => MathF.Min(a, b);
        public static float Max(float a, float b) => MathF.Max(a, b);
        public static float Atan2(float y, float x) => MathF.Atan2(y, x);
        public static float Cos(float value) => MathF.Cos(value);
        public static float Sin(float value) => MathF.Sin(value);
    }
}
namespace MalumMenu
{
    public static class Utils { public static bool isClient, isLobby, isInGame, isFreePlay, isMeeting, isExiling; }
    public static class MenuUI { public static bool isGUIActive; }
    public static class CheatToggles { public static bool automaticTasks, noClip; }
    public sealed class Config<T>(T value) { public T Value = value; }
    public sealed class FixtureLogger
    {
        public void LogInfo(object message) { }
        public void LogWarning(object message) { }
    }
    public static class MalumMenu
    {
        public static FixtureLogger Log = new();
        public static bool isPanicked;
        public static Config<float> stuntMultiplier = new(2f);
        public static Config<float> targetMovementMultiplier = new(3f);
        public static Config<float> lagInterval = new(0.75f);
        public static Config<float> lagJumpDistance = new(1.25f);
        public static Config<float> yoyoInterval = new(1f);
    }
    public static class SprintHandler
    {
        public static int Resets;
        public static Action OnReset;
        public static void Reset() { Resets++; OnReset?.Invoke(); }
    }
    public static class NavigationRouter
    {
        public static string Diagnostics => "Offline router fixture";
        public static bool FloorClear = true, Blocked;
        public static bool IsMovingRoutePending;
        public static bool MovingRouteFailed;
        public static bool IsRecovering;
        public static float SteeringDistance = float.PositiveInfinity;
        public static int Calls, Resets, RecoveryCalls, MovingCalls;
        public static Vector2? ForcedDirection;
        public static Vector2 RecoveryDirection;
        public static Vector2 LastFrom, LastTo, LastFloorTest;
        public static Func<Vector2, bool> StandPredicate;
        public static Func<Vector2, Vector2, bool> TravelPredicate;
        public static Func<Vector2, bool> MovingGoalUnreachable;
        public static readonly List<(Vector2 From, Vector2 To, bool Clear)> TravelChecksRecorded = [];
        public static readonly List<Vector2> MovingDestinations = [];
        public static readonly List<Vector2> CompletedFailedGoals = [];
        public static int BlockedMovingGoals;
        public static int StandChecks, TravelChecks;
        public static bool CanStand(Vector2 point)
        {
            LastFloorTest = point;
            StandChecks++;
            return FloorClear && float.IsFinite(point.x) && float.IsFinite(point.y) && (StandPredicate?.Invoke(point) ?? true);
        }
        public static bool CanTravel(Vector2 from, Vector2 to)
        {
            TravelChecks++;
            bool clear = CanStand(from) && CanStand(to) && (TravelPredicate?.Invoke(from, to) ?? true);
            TravelChecksRecorded.Add((from, to, clear));
            return clear;
        }
        public static Vector2 Direction(Vector2 from, Vector2 to, out string status)
        {
            Calls++; LastFrom = from; LastTo = to; status = Blocked ? "Route blocked" : "Route ready";
            return Blocked ? Vector2.zero : ForcedDirection ?? (to - from).normalized;
        }
        public static Vector2 DirectionToMovingTarget(Vector2 from, Vector2 to, out string status)
        {
            IsRecovering = false;
            MovingRouteFailed = false;
            MovingCalls++;
            LastFrom = from; LastTo = to;
            MovingDestinations.Add(to);
            if (!CanStand(to))
            {
                BlockedMovingGoals++;
                status = "Moving destination blocked";
                return Vector2.zero;
            }
            if (!CanStand(from)) return RecoverDirection(from, out status);
            if (MovingGoalUnreachable?.Invoke(to) ?? false)
            {
                MovingRouteFailed = !IsMovingRoutePending;
                status = IsMovingRoutePending ? "Preparing moving route" : "Completed route has no reachable join";
                if (MovingRouteFailed) CompletedFailedGoals.Add(to);
                return Vector2.zero;
            }
            return Direction(from, to, out status);
        }
        public static void Reset() { Resets++; IsMovingRoutePending = false; MovingRouteFailed = false; IsRecovering = false; }
        public static Vector2 RecoverDirection(Vector2 from, out string status)
        {
            RecoveryCalls++;
            IsRecovering = true;
            status = "Recovering from obstacle";
            return RecoveryDirection;
        }
    }
    public static class AiTasksHandler
    {
        public static bool Available = true, IsFinished, NativeActionAllowed = true;
        public static bool RequireGroundBody;
        public static Vector2 Goal = new(8f, 0f);
        public static float ArrivalDistance = 0.12f;
        public static int Resets, Arrivals, NativeSteps, Skips, AlternateRequests;
        public static bool HasAlternate;
        public static void Reset() { Resets++; IsFinished = false; }
        public static bool TryGetDestination(out Vector2 goal, out float arrivalDistance, out string status)
        {
            goal = Goal; arrivalDistance = ArrivalDistance;
            if ((!Utils.isInGame && !Utils.isFreePlay) || !ShipStatus.Instance)
            { status = "AI waiting for round"; return false; }
            if (RequireGroundBody && (!PlayerControl.LocalPlayer.Collider || !PlayerControl.LocalPlayer.Collider.enabled))
            { status = "AI collision geometry unavailable"; return false; }
            status = IsFinished ? "AI finished" : Available ? "AI going" : "AI waiting";
            return Available && !IsFinished;
        }
        public static void OnArrived() { Arrivals++; if (NativeActionAllowed) NativeSteps++; }
        public static void SkipCurrent(string _) => Skips++;
        public static bool TryAlternateApproach() { AlternateRequests++; return HasAlternate; }
    }
}
