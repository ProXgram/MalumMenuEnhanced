public class UnityObjectStub
{
    private static int _pointer;
    public IntPtr Pointer = new(++_pointer);
    public bool Destroyed;
    public static implicit operator bool(UnityObjectStub value) => value is not null && !value.Destroyed;
    public static bool operator !(UnityObjectStub value) => !(bool)value;
    public T TryCast<T>() where T : UnityObjectStub => this as T;
}
public sealed class ShipStatus : UnityObjectStub { public static ShipStatus Instance; }
public sealed class GameObject { public int layer = 8; }
public sealed class PlayerControl : UnityObjectStub
{
    public static PlayerControl LocalPlayer;
    public bool AmOwner = true;
    public UnityEngine.Collider2D Collider;
    public GameObject gameObject = new();
    public UnityEngine.Vector2 TruePosition;
    public UnityEngine.Vector2 GetTruePosition() => TruePosition;
}
public static class Constants
{
    public const int ShipAndObjectsMask = (1 << 9) | (1 << 10);
    public const int ShipAndAllObjectsMask = ShipAndObjectsMask | (1 << 11);
}

namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    public sealed class Il2CppReferenceArray<T>(int length)
    {
        private readonly T[] _values = new T[length];
        public T this[int index] { get => _values[index]; set => _values[index] = value; }
    }
    public sealed class Il2CppStructArray<T>(int length)
    {
        private readonly T[] _values = new T[length];
        public T this[int index] { get => _values[index]; set => _values[index] = value; }
    }
}

namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => default;
        public float sqrMagnitude => x * x + y * y;
        public float magnitude => MathF.Sqrt(sqrMagnitude);
        public Vector2 normalized => magnitude > 0f ? this / magnitude : zero;
        public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.x - b.x, a.y - b.y);
        public static Vector2 operator /(Vector2 a, float divisor) => new(a.x / divisor, a.y / divisor);
        public static Vector2 operator *(Vector2 a, float multiplier) => new(a.x * multiplier, a.y * multiplier);
        public void Normalize() { this = normalized; }
        public static float Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;
        public override string ToString() => $"({x},{y})";
    }
    public struct Vector3(float x, float y, float z = 0)
    {
        public float x = x, y = y, z = z;
        public static explicit operator Vector2(Vector3 value) => new(value.x, value.y);
    }
    public struct Bounds { public Vector3 center; public Vector3 extents; }
    public class Collider2D : UnityObjectStub
    {
        public bool isTrigger;
        public Rectangle Rectangle;
        public PlayerControl Owner;
        public float WorldRadius = 0.16f;
        public bool BoundsUnavailable;
        public Vector2 CenterOffset;
        public GameObject gameObject = new();
        public Transform transform = new();
        public Bounds bounds => Owner == null || BoundsUnavailable ? default : new()
        {
            center = new(Owner.TruePosition.x + CenterOffset.x, Owner.TruePosition.y + CenterOffset.y),
            extents = new(WorldRadius, WorldRadius),
        };
        public Vector2 ClosestPoint(Vector2 point) => Rectangle == null ? point :
            new(Math.Clamp(point.x, Rectangle.MinX, Rectangle.MaxX), Math.Clamp(point.y, Rectangle.MinY, Rectangle.MaxY));
    }
    public sealed class Transform { public Vector3 lossyScale = new(1, 1, 1); }
    public sealed class CircleCollider2D : Collider2D { public float radius => WorldRadius; }
    public struct ContactFilter2D
    {
        public bool useTriggers;
        public int Mask;
        public void SetLayerMask(int mask) => Mask = mask;
    }
    public struct RaycastHit2D { public Collider2D collider; }
    public static class Time { public static double realtimeSinceStartupAsDouble; }
    public sealed class Rectangle
    {
        public float MinX, MinY, MaxX, MaxY;
        public int Layer;
        public bool Enabled = true;
        public readonly Collider2D Collider = new();
        public Rectangle(float minX, float minY, float maxX, float maxY, int layer = 9)
        { MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY; Layer = layer; Collider.Rectangle = this; }
    }
    public static class Physics2D
    {
        public static readonly List<Rectangle> Obstacles = [];
        public static readonly HashSet<int> Masks = [];
        public static int Overlaps, Sweeps;
        public static int CollisionMask = Constants.ShipAndObjectsMask;
        public static int LastCollisionLayer;
        public static readonly List<float> Radii = [];
        public static readonly List<Vector2> Centers = [];
        public static bool Throw;
        public static int Queries => Overlaps + Sweeps;
        public static int GetLayerCollisionMask(int layer) { LastCollisionLayer = layer; return CollisionMask; }
        public static int OverlapCircle(Vector2 point, float radius, ContactFilter2D filter,
            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Collider2D> results)
        {
            if (Throw) throw new InvalidOperationException("synthetic physics failure");
            Overlaps++; Masks.Add(filter.Mask); Radii.Add(radius); Centers.Add(point);
            var found = Obstacles.FirstOrDefault(rect => rect.Enabled && (filter.Mask & (1 << rect.Layer)) != 0 &&
                (filter.useTriggers || !rect.Collider.isTrigger) && OverlapsRect(point, radius, rect));
            results[0] = found?.Collider; return found == null ? 0 : 1;
        }
        public static int CircleCast(Vector2 from, float radius, Vector2 direction, ContactFilter2D filter,
            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<RaycastHit2D> results, float distance)
        {
            if (Throw) throw new InvalidOperationException("synthetic physics failure");
            Sweeps++; Masks.Add(filter.Mask); Radii.Add(radius);
            var found = Obstacles.FirstOrDefault(rect => rect.Enabled && (filter.Mask & (1 << rect.Layer)) != 0 &&
                (filter.useTriggers || !rect.Collider.isTrigger) && Intersects(from, from + direction * distance, radius, rect));
            results[0] = new() { collider = found?.Collider }; return found == null ? 0 : 1;
        }
        public static Collider2D[] OverlapCircleAll(Vector2 point, float radius, int layerMask)
        {
            if (Throw) throw new InvalidOperationException("synthetic physics failure");
            Overlaps++; Masks.Add(layerMask); Radii.Add(radius); Centers.Add(point);
            return Obstacles.Where(rect => rect.Enabled && (layerMask & (1 << rect.Layer)) != 0 && OverlapsRect(point, radius, rect)).Select(rect => rect.Collider).ToArray();
        }
        public static Collider2D OverlapCircle(Vector2 point, float radius, int layerMask)
        {
            if (Throw) throw new InvalidOperationException("synthetic physics failure");
            Overlaps++; Masks.Add(layerMask);
            return Obstacles.FirstOrDefault(rect => rect.Enabled && (layerMask & (1 << rect.Layer)) != 0 && OverlapsRect(point, radius, rect))?.Collider;
        }
        public static RaycastHit2D CircleCast(Vector2 from, float radius, Vector2 direction, float distance, int layerMask)
        {
            if (Throw) throw new InvalidOperationException("synthetic physics failure");
            Sweeps++; Masks.Add(layerMask);
            var to = from + direction * distance;
            return new() { collider = Obstacles.FirstOrDefault(rect => rect.Enabled && (layerMask & (1 << rect.Layer)) != 0 && Intersects(from, to, radius, rect))?.Collider };
        }
        public static bool Clear(Vector2 from, Vector2 to) =>
            !Obstacles.Any(rect => rect.Enabled && (Constants.ShipAndObjectsMask & (1 << rect.Layer)) != 0 && Intersects(from, to, 0.16f, rect));
        public static bool Stand(Vector2 point) =>
            !Obstacles.Any(rect => rect.Enabled && (Constants.ShipAndObjectsMask & (1 << rect.Layer)) != 0 && OverlapsRect(point, 0.16f, rect));
        private static bool OverlapsRect(Vector2 point, float radius, Rectangle rect)
        {
            float x = point.x - Math.Clamp(point.x, rect.MinX, rect.MaxX);
            float y = point.y - Math.Clamp(point.y, rect.MinY, rect.MaxY);
            return x * x + y * y <= radius * radius;
        }
        // Conservatively sweep an expanded rectangle. This is deterministic
        // synthetic collision geometry, not an implementation of Unity physics.
        private static bool Intersects(Vector2 from, Vector2 to, float radius, Rectangle rect)
        {
            float lower = 0, upper = 1;
            bool Axis(float origin, float delta, float min, float max)
            {
                if (Math.Abs(delta) < 0.000001f) return origin >= min && origin <= max;
                float a = (min - origin) / delta, b = (max - origin) / delta;
                if (a > b) (a, b) = (b, a);
                lower = Math.Max(lower, a); upper = Math.Min(upper, b);
                return lower <= upper;
            }
            return Axis(from.x, to.x - from.x, rect.MinX - radius, rect.MaxX + radius) &&
                Axis(from.y, to.y - from.y, rect.MinY - radius, rect.MaxY + radius);
        }
    }
}
