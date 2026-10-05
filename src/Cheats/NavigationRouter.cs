using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace MalumMenu;

/// <summary>Returns ground movement only; never changes position or sends messages.</summary>
public static class NavigationRouter
{
    internal const float Clearance = 0.16f;
    internal const float CellSize = 0.4f;
    internal const int MaximumExpanded = 6000;
    internal const int MovingExpandedPerStep = 32;
    internal const int MovingQueriesPerStep = 128;
    private const float ArrivalDistance = 0.08f;
    private const float GeometryOffsetResetDistance = 0.1f;
    private const double RetrySeconds = 0.75;
    private static readonly Il2CppReferenceArray<Collider2D> OverlapResult = new(1);
    private static readonly Il2CppStructArray<RaycastHit2D> SweepResult = new(1);
    private static List<Vector2> _path;
    private static int _next;
    private static Vector2 _plannedGoal;
    private static double _lastAttempt = double.NegativeInfinity;
    private static IntPtr _ship;
    private static Geometry _cachedGeometry;
    private static GridPlanner.MovingSearch _movingSearch;
    public static bool IsMovingRoutePending => _movingSearch != null && !_movingSearch.Finished;
    public static float SteeringDistance { get; private set; }
    public static bool IsRecovering { get; private set; }
    public static bool MovingRouteFailed { get; private set; }
    public static Action<string> FailureTrace { get; set; }
    public static long GeometryResetCount { get; private set; }
    public static string LastFailure { get; private set; } = "none";
    public static string GeometryStatus { get; private set; } = "not inspected";
    public static string LastNativeException { get; private set; } = "none";
    private static Geometry _diagnosticGeometry;
    private static Vector2 _diagnosticFrom, _diagnosticGoal;
    private static string _diagnosticContact = "none";
    private static int _diagnosticExpanded, _diagnosticDiscovered;
    private static long _lastContactDiagnostic, _lastExceptionDiagnostic;
    private static bool _physicsQueryFailed;
    private static double _movingSearchStartedAt;
    private static long _movingSearchGeometryResets, _lastFailureTrace;
    public static string Diagnostics => "reason=" + LastFailure + "; geometry=" + GeometryStatus +
        "; mask=0x" + _diagnosticGeometry.Mask.ToString("X") +
        "; radius=" + Number(_diagnosticGeometry.Radius) +
        "; offset=" + PointText(_diagnosticGeometry.Offset) +
        "; from=" + PointText(_diagnosticFrom) + "; goal=" + PointText(_diagnosticGoal) +
        "; contact=" + _diagnosticContact + "; expanded=" + _diagnosticExpanded +
        "; discovered=" + _diagnosticDiscovered + "; pending=" + IsMovingRoutePending +
        "; lastException=" + LastNativeException + "; geometryResets=" + GeometryResetCount;

    private struct Geometry
    {
        internal float Radius;
        internal Vector2 Offset;
        internal int Mask;
        internal IntPtr Collider;
        internal ContactFilter2D Filter;
    }

    private static void RefreshGeometry(IntPtr shipPointer, Geometry geometry)
    {
        // Render/body interpolation translates the observed center slightly
        // between walking frames. It must not restart a multi-frame search.
        // Every returned movement segment is still checked with current geometry.
        if (_ship != shipPointer || _cachedGeometry.Collider != geometry.Collider ||
            Math.Abs(_cachedGeometry.Radius - geometry.Radius) > 0.001f ||
            (_cachedGeometry.Offset - geometry.Offset).sqrMagnitude > GeometryOffsetResetDistance * GeometryOffsetResetDistance ||
            _cachedGeometry.Mask != geometry.Mask)
        { GeometryResetCount++; Reset(); }
        _ship = shipPointer;
        _cachedGeometry = geometry;
    }

    public static void Reset()
    {
        _path = null;
        _next = 0;
        _lastAttempt = double.NegativeInfinity;
        _plannedGoal = default;
        _ship = IntPtr.Zero;
        _cachedGeometry = default;
        _movingSearch = null;
        SteeringDistance = 0f;
        IsRecovering = false;
        MovingRouteFailed = false;
    }

    private static bool TryGeometry(out Geometry geometry)
    {
        geometry = default;
        try
        {
            geometry.Radius = Clearance;
            geometry.Mask = Constants.ShipAndObjectsMask;
            GeometryStatus = "fallback: no owning collider";
            var local = PlayerControl.LocalPlayer;
            if (local && local.AmOwner && local.Collider)
            {
                var collider = local.Collider;
                geometry.Collider = collider.Pointer;
                var bounds = collider.bounds;
                if (!float.IsFinite(bounds.extents.x) || !float.IsFinite(bounds.extents.y) ||
                    bounds.extents.x <= 0f || bounds.extents.y <= 0f)
                { _diagnosticGeometry = geometry; GeometryStatus = "invalid world bounds"; LastFailure = "geometry: invalid world bounds"; return false; }
                float bodyRadius;
                var circle = collider.TryCast<CircleCollider2D>();
                if (circle)
                {
                    var scale = circle.transform.lossyScale;
                    bodyRadius = circle.radius * Math.Max(Math.Abs(scale.x), Math.Abs(scale.y));
                    GeometryStatus = "native circle";
                }
                else
                {
                    // A bounding circle encloses corners of non-circular bodies.
                    float x = bounds.extents.x, y = bounds.extents.y;
                    bodyRadius = (float)Math.Sqrt(x * x + y * y);
                    GeometryStatus = "bounding circle for noncircular body";
                }
                if (!float.IsFinite(bodyRadius) || bodyRadius <= 0f || bodyRadius > 2f)
                { _diagnosticGeometry = geometry; GeometryStatus = "invalid body radius"; LastFailure = "geometry: invalid body radius"; return false; }
                geometry.Radius = Math.Max(Clearance, bodyRadius + 0.025f);
                geometry.Offset = (Vector2)bounds.center - local.GetTruePosition();
                if (!Valid(geometry.Offset))
                { _diagnosticGeometry = geometry; GeometryStatus = "invalid center offset"; LastFailure = "geometry: invalid center offset"; return false; }
                geometry.Collider = collider.Pointer;
                geometry.Mask = Physics2D.GetLayerCollisionMask(collider.gameObject.layer) & Constants.ShipAndAllObjectsMask;
            }
            geometry.Filter = new ContactFilter2D { useTriggers = false };
            geometry.Filter.SetLayerMask(geometry.Mask);
            _diagnosticGeometry = geometry;
            return true;
        }
        catch (Exception error) { GeometryStatus = "native geometry exception"; RecordException("geometry", error); return false; }
    }

    public static bool CanStand(Vector2 point) =>
        Valid(point) && TryGeometry(out var geometry) && Stand(point, geometry);

    private static bool Stand(Vector2 point, Geometry geometry)
    {
        _physicsQueryFailed = false;
        try { return Physics2D.OverlapCircle(point + geometry.Offset, geometry.Radius, geometry.Filter, OverlapResult) == 0; }
        catch (Exception error) { _physicsQueryFailed = true; RecordException("overlap", error); return false; }
    }

    public static bool CanTravel(Vector2 from, Vector2 to) =>
        ValidTrip(from, to) && TryGeometry(out var geometry) &&
        Stand(from, geometry) && Stand(to, geometry) && Sweep(from, to, geometry);

    private static bool Sweep(Vector2 from, Vector2 to, Geometry geometry)
    {
        _physicsQueryFailed = false;
        try
        {
            var offset = to - from;
            float distance = offset.magnitude;
            return distance <= 0.001f || Physics2D.CircleCast(from + geometry.Offset, geometry.Radius,
                offset / distance, geometry.Filter, SweepResult, distance) == 0;
        }
        catch (Exception error) { _physicsQueryFailed = true; RecordException("sweep", error); return false; }
    }

    /// <summary>Short outward walking from existing contacts; never snaps or crosses a new obstacle.</summary>
    public static Vector2 RecoverDirection(Vector2 from, out string status)
    {
        SteeringDistance = 0f;
        IsRecovering = false;
        status = "Waiting for open floor";
        if (!Valid(from) || !TryGeometry(out var geometry)) return Vector2.zero;
        try
        {
            var center = from + geometry.Offset;
            var contacts = new List<Collider2D>();
            var normals = new List<Vector2>();
            Vector2 away = Vector2.zero;
            foreach (var collider in Physics2D.OverlapCircleAll(center, geometry.Radius, geometry.Mask))
            {
                if (!collider || collider.isTrigger) continue;
                if (contacts.Count >= 16) { LastFailure = "recovery: contact limit"; return Vector2.zero; }
                var normal = center - collider.ClosestPoint(center);
                // Deeply inside a map collider has no reliable outward normal.
                if (normal.sqrMagnitude < 0.000001f) { LastFailure = "recovery: deeply embedded contact"; return Vector2.zero; }
                normal.Normalize();
                contacts.Add(collider); normals.Add(normal); away += normal;
            }
            if (contacts.Count == 0 || away.sqrMagnitude < 0.000001f)
            { LastFailure = contacts.Count == 0 ? "recovery: no contact normal" : "recovery: opposing contacts"; return Vector2.zero; }
            float angle = (float)Math.Atan2(away.y, away.x);
            // Prefer the shortest exit and the summed contact normal. Adjacent
            // directions are permitted only when moving away from every contact.
            for (int distanceStep = 1; distanceStep <= 4; distanceStep++)
                for (int directionStep = 0; directionStep < 16; directionStep++)
                {
                    int turn = directionStep == 0 ? 0 : ((directionStep + 1) / 2) * ((directionStep & 1) == 1 ? 1 : -1);
                    float bearing = angle + turn * ((float)Math.PI / 8f);
                    var direction = new Vector2((float)Math.Cos(bearing), (float)Math.Sin(bearing));
                    bool outward = true;
                    foreach (var normal in normals)
                        if (Vector2.Dot(direction, normal) < 0.1f) { outward = false; break; }
                    if (!outward) continue;
                    float distance = distanceStep * 0.15f;
                    var destination = from + direction * distance;
                    if (!Stand(destination, geometry)) continue;
                    bool clear = true;
                    int samples = (int)Math.Ceiling(distance / 0.05f);
                    for (int sample = 1; sample <= samples && clear; sample++)
                    {
                        var probe = center + direction * (distance * sample / samples);
                        foreach (var obstacle in Physics2D.OverlapCircleAll(probe, geometry.Radius, geometry.Mask))
                        {
                            if (!obstacle || obstacle.isTrigger) continue;
                            int index = contacts.FindIndex(contact => contact.Pointer == obstacle.Pointer);
                            if (index < 0 ||
                                (probe - obstacle.ClosestPoint(probe)).sqrMagnitude + 0.000001f <
                                (center - obstacle.ClosestPoint(center)).sqrMagnitude)
                            { clear = false; break; }
                        }
                    }
                    if (!clear) continue;
                    _path = null;
                    status = "Moving away from an item";
                    LastFailure = "none";
                    SteeringDistance = distance;
                    IsRecovering = true;
                    return direction;
                }
        }
        catch (Exception error) { RecordException("recovery", error); return Vector2.zero; }
        LastFailure = "recovery: no safe outward direction";
        return Vector2.zero;
    }

    public static Vector2 Direction(Vector2 from, Vector2 to, out string status)
    {
        MovingRouteFailed = false;
        SteeringDistance = 0f; IsRecovering = false;
        _diagnosticFrom = from; _diagnosticGoal = to;
        status = "Waiting for a clear ground route";
        if (!ValidTrip(from, to)) { LastFailure = "invalid trip"; Reset(); status = "Destination is unavailable"; return Vector2.zero; }
        try
        {
            if (!TryGeometry(out var geometry)) return Vector2.zero;
            var ship = ShipStatus.Instance;
            var shipPointer = ship ? ship.Pointer : IntPtr.Zero;
            RefreshGeometry(shipPointer, geometry);
            double now = Time.realtimeSinceStartupAsDouble;
            if (!double.IsFinite(now)) { LastFailure = "nonfinite clock"; Reset(); return Vector2.zero; }
            if (now < _lastAttempt) { _path = null; _lastAttempt = double.NegativeInfinity; }
            if (!Stand(from, geometry)) { RecordBlocked("start", geometry); _path = null; return RecoverDirection(from, out status); }
            if (!Stand(to, geometry)) { RecordBlocked("goal", geometry); _path = null; return Vector2.zero; }
            var offset = to - from;
            if (offset.sqrMagnitude <= ArrivalDistance * ArrivalDistance)
            { LastFailure = "at destination"; _path = null; status = "At destination"; return Vector2.zero; }
            if (Sweep(from, to, geometry))
            { LastFailure = "none"; _path = null; status = "Direct ground route"; return offset.normalized; }
            if (_path != null && (to - _plannedGoal).sqrMagnitude > CellSize * CellSize) _path = null;
            if (_path != null)
            {
                var direction = Follow(from, geometry);
                if (direction.sqrMagnitude > 0f) { LastFailure = "none"; status = "Following ground route"; return direction; }
                _path = null;
            }
            if (now - _lastAttempt < RetrySeconds) { LastFailure = "static planner retry cooldown"; return Vector2.zero; }
            _lastAttempt = now;
            _plannedGoal = to;
            var result = GridPlanner.Plan(from, to, point => Stand(point, geometry), (a, b) => Sweep(a, b, geometry));
            _diagnosticExpanded = result.Expanded; _diagnosticDiscovered = result.Discovered;
            _path = result.Path; _next = 1;
            if (_path == null) { LastFailure = "static planner found no route"; return Vector2.zero; }
            var movement = Follow(from, geometry);
            if (movement.sqrMagnitude > 0f) { LastFailure = "none"; status = "Following ground route"; }
            else _path = null;
            return movement;
        }
        catch (Exception error) { RecordException("static routing", error); _path = null; return Vector2.zero; }
    }

    /// <summary>Replans moving goals in bounded main-thread steps while checking live movement.</summary>
    public static Vector2 DirectionToMovingTarget(Vector2 from, Vector2 to, out string status)
    {
        MovingRouteFailed = false;
        SteeringDistance = 0f; IsRecovering = false;
        _diagnosticFrom = from; _diagnosticGoal = to;
        status = "Planning a ground route";
        if (!ValidTrip(from, to)) { LastFailure = "invalid trip"; Reset(); status = "Destination is unavailable"; return Vector2.zero; }
        try
        {
            if (!TryGeometry(out var geometry)) { Reset(); return Vector2.zero; }
            var ship = ShipStatus.Instance;
            var shipPointer = ship ? ship.Pointer : IntPtr.Zero;
            RefreshGeometry(shipPointer, geometry);
            double now = Time.realtimeSinceStartupAsDouble;
            if (!double.IsFinite(now)) { LastFailure = "nonfinite clock"; Reset(); return Vector2.zero; }
            if (now < _lastAttempt) { _path = null; _movingSearch = null; _lastAttempt = double.NegativeInfinity; }
            if (!Stand(from, geometry))
            { RecordBlocked("start", geometry); _path = null; _movingSearch = null; return RecoverDirection(from, out status); }
            if (!Stand(to, geometry))
            { RecordBlocked("goal", geometry); _path = null; _movingSearch = null; status = "Destination is blocked"; return Vector2.zero; }
            var offset = to - from;
            if (offset.sqrMagnitude <= ArrivalDistance * ArrivalDistance)
            { LastFailure = "at destination"; _path = null; _movingSearch = null; status = "At destination"; return Vector2.zero; }
            if (Sweep(from, to, geometry))
            { LastFailure = "none"; _path = null; _movingSearch = null; SteeringDistance = offset.magnitude; status = "Direct ground route"; return offset.normalized; }

            var movement = _path == null ? Vector2.zero : Follow(from, geometry);
            if (movement.sqrMagnitude == 0f) _path = null;
            bool needsPlan = _path == null || (to - _plannedGoal).sqrMagnitude > CellSize * CellSize;
            string completedFailure = null;
            if (_movingSearch == null && needsPlan && now - _lastAttempt >= RetrySeconds)
            {
                _lastAttempt = now;
                _movingSearchStartedAt = now;
                _movingSearchGeometryResets = GeometryResetCount;
                // Hold this search goal until completion. Restarting for every
                // network update can prevent a moving player's route finishing.
                _movingSearch = new GridPlanner.MovingSearch(from, to,
                    point => Stand(point, _cachedGeometry), (a, b) => Sweep(a, b, _cachedGeometry));
            }
            if (_movingSearch != null)
            {
                var search = _movingSearch;
                search.Step();
                _diagnosticExpanded = search.Result.Expanded; _diagnosticDiscovered = search.Discovered;
                if (search.Finished)
                {
                    _movingSearch = null;
                    _lastAttempt = now;
                    if (search.Result.Path != null)
                    {
                        // The actor may have continued along an older route
                        // during the search. A nearest point across furniture
                        // is not a usable join; try bounded visible alternatives.
                        int join = ReachableJoin(search.Result.Path, from,
                            point => Stand(point, geometry), (a, b) => Sweep(a, b, geometry));
                        if (join >= 0)
                        {
                            _path = search.Result.Path; _next = join; _plannedGoal = search.Goal;
                            movement = (_path[join] - from).normalized;
                        }
                        else completedFailure = "completed moving search: no reachable join";
                    }
                    else completedFailure = "completed moving search: no path";
                    if (completedFailure != null)
                    {
                        MovingRouteFailed = movement.sqrMagnitude == 0f;
                        TraceFailedSearch(search, completedFailure, from, to, now, !MovingRouteFailed);
                    }
                }
            }
            if (movement.sqrMagnitude > 0f)
            {
                LastFailure = "none";
                SteeringDistance = _path != null && _next < _path.Count ? (_path[_next] - from).magnitude : 0f;
                status = _movingSearch == null ? "Following ground route" : "Following ground route; planning ahead";
            }
            else if (_movingSearch == null) { LastFailure = completedFailure ?? "moving planner found no usable route or cooling down"; status = "Waiting for a clear ground route"; }
            else LastFailure = "moving planner pending";
            return movement;
        }
        catch (Exception error) { RecordException("moving routing", error); _path = null; _movingSearch = null; return Vector2.zero; }
    }

    private static string Number(float value) => value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
    private static string PointText(Vector2 point) => "(" + Number(point.x) + "," + Number(point.y) + ")";
    private static void TraceFailedSearch(GridPlanner.MovingSearch search, string outcome,
        Vector2 from, Vector2 goal, double now, bool retainedMovement)
    {
        if (FailureTrace == null) return;
        long timestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        if (timestamp - _lastFailureTrace < System.Diagnostics.Stopwatch.Frequency) return;
        _lastFailureTrace = timestamp;
        try
        {
            FailureTrace("Navigation " + outcome + "; expanded=" + search.Result.Expanded +
                "; discovered=" + search.Discovered + "; searchFrom=" + PointText(search.Origin) +
                "; searchGoal=" + PointText(search.Goal) + "; currentFrom=" + PointText(from) +
                "; currentGoal=" + PointText(goal) + "; elapsed=" +
                (now - _movingSearchStartedAt).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                "; retainedMovement=" + retainedMovement + "; geometryResetsDuringSearch=" +
                (GeometryResetCount - _movingSearchGeometryResets) + "; " + Diagnostics);
        }
        catch { } // Diagnostic consumers must not change movement decisions.
    }
    private static void RecordBlocked(string point, Geometry geometry)
    {
        if (_physicsQueryFailed) return;
        LastFailure = point + " clearance overlaps a solid collider";
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (now - _lastContactDiagnostic < System.Diagnostics.Stopwatch.Frequency) return;
        _lastContactDiagnostic = now;
        try
        {
            var collider = OverlapResult[0];
            _diagnosticContact = !collider ? "overlap reported; no returned collider" :
                collider.GetType().Name + "; layer=" + collider.gameObject.layer +
                "; ownBody=" + (collider.Pointer == geometry.Collider) + "; trigger=" + collider.isTrigger;
        }
        catch { _diagnosticContact = "contact metadata unavailable"; }
    }
    private static void RecordException(string stage, Exception error)
    {
        try
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (now - _lastExceptionDiagnostic < System.Diagnostics.Stopwatch.Frequency) return;
            _lastExceptionDiagnostic = now;
            string message = error.Message ?? "";
            if (message.Length > 180) message = message.Substring(0, 180);
            LastNativeException = stage + " exception " + error.GetType().Name + ": " + message.Replace('\r', ' ').Replace('\n', ' ');
            LastFailure = LastNativeException;
        }
        catch { LastNativeException = stage + " exception: message unavailable"; }
    }

    private static int ClosestWaypoint(List<Vector2> path, Vector2 from)
    {
        int nearest = 1;
        float distance = float.PositiveInfinity;
        for (int i = 1; i < path.Count; i++)
        {
            float candidate = (path[i] - from).sqrMagnitude;
            if (candidate < distance) { nearest = i; distance = candidate; }
        }
        return nearest;
    }

    internal static int ReachableJoin(List<Vector2> path, Vector2 from,
        Func<Vector2, bool> walkable, Func<Vector2, Vector2, bool> clear)
    {
        if (path == null || path.Count < 2 || walkable == null || clear == null) return -1;
        int nearest = ClosestWaypoint(path, from);
        // Six checked attempts cover adjacent points, earlier corners and the
        // original first step without scanning the path with native physics.
        int[] candidates = { nearest, nearest - 1, nearest + 1, nearest - 3, nearest - 7, 1 };
        for (int attempt = 0; attempt < candidates.Length; attempt++)
        {
            int index = candidates[attempt];
            if (index < 1 || index >= path.Count) continue;
            while (index < path.Count && (path[index] - from).sqrMagnitude <= ArrivalDistance * ArrivalDistance) index++;
            if (index >= path.Count) continue;
            bool duplicate = false;
            for (int previous = 0; previous < attempt; previous++)
                if (candidates[previous] == index) { duplicate = true; break; }
            candidates[attempt] = index;
            if (!duplicate && walkable(path[index]) && clear(from, path[index])) return index;
        }
        return -1;
    }

    private static Vector2 Follow(Vector2 from, Geometry geometry)
    {
        while (_next < _path.Count && (from - _path[_next]).sqrMagnitude <= ArrivalDistance * ArrivalDistance) _next++;
        if (_next >= _path.Count || !Stand(_path[_next], geometry) || !Sweep(from, _path[_next], geometry)) return Vector2.zero;
        int best = _next;
        for (int i = _next + 1; i < _path.Count && i <= _next + 5; i++)
        {
            if ((from - _path[i]).sqrMagnitude > 1.44f) break;
            if (Stand(_path[i], geometry) && Sweep(from, _path[i], geometry)) best = i;
        }
        _next = best;
        return (_path[best] - from).normalized;
    }

    private static bool Valid(Vector2 point) =>
        float.IsFinite(point.x) && float.IsFinite(point.y) && Math.Abs(point.x) <= 2048f && Math.Abs(point.y) <= 2048f;
    private static bool ValidTrip(Vector2 from, Vector2 to) =>
        Valid(from) && Valid(to) && (from - to).sqrMagnitude <= 160f * 160f;
    internal sealed class SearchResult
    {
        internal List<Vector2> Path;
        internal int Expanded;
        internal int Discovered;
    }

    internal static class GridPlanner
    {
        private sealed class Node
        {
            internal (int X, int Y) Key;
            internal float Cost;
            internal Node Parent;
        }

        internal sealed class MovingSearch
        {
            private readonly Vector2 _from;
            private readonly Func<Vector2, bool> _walkable;
            private readonly Func<Vector2, Vector2, bool> _clear;
            private readonly int _minX, _maxX, _minY, _maxY;
            private readonly Dictionary<(int X, int Y), bool> _occupancy = new();
            private readonly Dictionary<((int X, int Y) A, (int X, int Y) B), bool> _edges = new();
            private readonly Dictionary<(int X, int Y), Node> _nodes = new();
            private readonly HashSet<(int X, int Y)> _closed = new();
            private readonly PriorityQueue<Node, float> _open = new();
            private Node _current;
            private int _neighbor;
            private bool _goalChecked;
            private int _queries;
            internal readonly Vector2 Goal;
            internal Vector2 Origin => _from;
            internal int Discovered => _nodes.Count;
            internal readonly SearchResult Result = new();
            internal bool Finished { get; private set; }

            internal MovingSearch(Vector2 from, Vector2 goal,
                Func<Vector2, bool> walkable, Func<Vector2, Vector2, bool> clear)
            {
                _from = from; Goal = goal; _walkable = walkable; _clear = clear;
                if (!ValidTrip(from, goal) || walkable == null || clear == null) { Finished = true; return; }
                int goalX = (int)Math.Round((goal.x - from.x) / CellSize);
                int goalY = (int)Math.Round((goal.y - from.y) / CellSize);
                _minX = Math.Min(0, goalX) - 30; _maxX = Math.Max(0, goalX) + 30;
                _minY = Math.Min(0, goalY) - 30; _maxY = Math.Max(0, goalY) + 30;
                var first = new Node { Key = (0, 0), Cost = 0f };
                _nodes[first.Key] = first; _occupancy[first.Key] = false;
                _open.Enqueue(first, (goal - from).magnitude);
            }

            internal void Step()
            {
                if (Finished) return;
                _queries = 0;
                int expanded = 0;
                long began = System.Diagnostics.Stopwatch.GetTimestamp();
                long timeBudget = Math.Max(1L, System.Diagnostics.Stopwatch.Frequency / 500L);
                while (_queries < MovingQueriesPerStep &&
                    System.Diagnostics.Stopwatch.GetTimestamp() - began < timeBudget)
                {
                    if (_current == null)
                    {
                        if (_open.Count == 0 || Result.Expanded >= MaximumExpanded) { Finish(null); return; }
                        if (expanded >= MovingExpandedPerStep) return;
                        var current = _open.Dequeue();
                        if (!_closed.Add(current.Key)) continue;
                        _current = current; _neighbor = 0; _goalChecked = false;
                        expanded++; Result.Expanded++;
                    }
                    var position = Point(_current.Key);
                    if (!_goalChecked)
                    {
                        if ((position - Goal).sqrMagnitude <= 0.65f * 0.65f)
                        {
                            if (!TryClear(position, Goal, out bool finalClear)) return;
                            if (finalClear)
                            {
                                var path = new List<Vector2>();
                                for (var node = _current; node != null; node = node.Parent) path.Add(Point(node.Key));
                                path.Reverse();
                                if ((path[path.Count - 1] - Goal).sqrMagnitude > 0.000001f) path.Add(Goal);
                                Finish(path); return;
                            }
                        }
                        _goalChecked = true;
                    }
                    if (_neighbor >= 9) { _current = null; continue; }
                    int dx = _neighbor / 3 - 1, dy = _neighbor % 3 - 1;
                    if (dx == 0 && dy == 0) { _neighbor++; continue; }
                    var next = (X: _current.Key.X + dx, Y: _current.Key.Y + dy);
                    if (_closed.Contains(next)) { _neighbor++; continue; }
                    if (!TryOccupied(next, out bool occupied)) return;
                    if (occupied) { _neighbor++; continue; }
                    if (dx != 0 && dy != 0)
                    {
                        var sideX = (X: _current.Key.X + dx, Y: _current.Key.Y);
                        var sideY = (X: _current.Key.X, Y: _current.Key.Y + dy);
                        if (!TryOccupied(sideX, out bool occupiedX) || !TryOccupied(sideY, out bool occupiedY)) return;
                        if (occupiedX || occupiedY) { _neighbor++; continue; }
                        if (!TryEdge(_current.Key, sideX, out bool clearX) || !TryEdge(_current.Key, sideY, out bool clearY)) return;
                        if (!clearX || !clearY) { _neighbor++; continue; }
                    }
                    if (!TryEdge(_current.Key, next, out bool clearNext)) return;
                    if (clearNext)
                    {
                        float cost = _current.Cost + CellSize * (dx != 0 && dy != 0 ? 1.41421356f : 1f);
                        if (!_nodes.TryGetValue(next, out var previous) || cost < previous.Cost)
                        {
                            var candidate = new Node { Key = next, Cost = cost, Parent = _current };
                            _nodes[next] = candidate;
                            _open.Enqueue(candidate, cost + (Goal - Point(next)).magnitude);
                        }
                    }
                    _neighbor++;
                }
            }

            private Vector2 Point((int X, int Y) key) => _from + new Vector2(key.X * CellSize, key.Y * CellSize);
            private bool TryOccupied((int X, int Y) key, out bool occupied)
            {
                occupied = true;
                if (key.X < _minX || key.X > _maxX || key.Y < _minY || key.Y > _maxY) return true;
                if (_occupancy.TryGetValue(key, out occupied)) return true;
                if (_queries >= MovingQueriesPerStep) return false;
                _queries++; occupied = !_walkable(Point(key)); _occupancy[key] = occupied;
                return true;
            }
            private bool TryEdge((int X, int Y) a, (int X, int Y) b, out bool clear)
            {
                var key = a.X < b.X || (a.X == b.X && a.Y <= b.Y) ? (a, b) : (b, a);
                if (_edges.TryGetValue(key, out clear)) return true;
                if (!TryClear(Point(a), Point(b), out clear)) return false;
                _edges[key] = clear;
                return true;
            }
            private bool TryClear(Vector2 a, Vector2 b, out bool clear)
            {
                clear = false;
                if (_queries >= MovingQueriesPerStep) return false;
                _queries++; clear = _clear(a, b);
                return true;
            }
            private void Finish(List<Vector2> path)
            {
                Result.Path = path; Result.Discovered = _nodes.Count; Finished = true;
            }
        }

        internal static SearchResult Plan(Vector2 from, Vector2 goal,
            Func<Vector2, bool> walkable, Func<Vector2, Vector2, bool> clear)
        {
            var result = new SearchResult();
            if (!ValidTrip(from, goal) || walkable == null || clear == null || !walkable(from) || !walkable(goal)) return result;
            if (clear(from, goal)) { result.Path = new List<Vector2> { from, goal }; return result; }
            int goalX = (int)Math.Round((goal.x - from.x) / CellSize);
            int goalY = (int)Math.Round((goal.y - from.y) / CellSize);
            const int margin = 30; // Twelve metres beyond the endpoints for room/corridor detours.
            int minX = Math.Min(0, goalX) - margin, maxX = Math.Max(0, goalX) + margin;
            int minY = Math.Min(0, goalY) - margin, maxY = Math.Max(0, goalY) + margin;
            var occupancy = new Dictionary<(int X, int Y), bool>();
            var edges = new Dictionary<((int X, int Y) A, (int X, int Y) B), bool>();
            var nodes = new Dictionary<(int X, int Y), Node>();
            var closed = new HashSet<(int X, int Y)>();
            var open = new PriorityQueue<Node, float>();
            Vector2 Point((int X, int Y) key) => from + new Vector2(key.X * CellSize, key.Y * CellSize);
            bool Occupied((int X, int Y) key)
            {
                if (key.X < minX || key.X > maxX || key.Y < minY || key.Y > maxY) return true;
                if (!occupancy.TryGetValue(key, out bool value)) { value = !walkable(Point(key)); occupancy[key] = value; }
                return value;
            }
            bool Edge((int X, int Y) a, (int X, int Y) b)
            {
                var key = a.X < b.X || (a.X == b.X && a.Y <= b.Y) ? (a, b) : (b, a);
                if (!edges.TryGetValue(key, out bool value)) { value = clear(Point(a), Point(b)); edges[key] = value; }
                return value;
            }
            var first = new Node { Key = (0, 0), Cost = 0f };
            nodes[first.Key] = first;
            occupancy[first.Key] = false;
            open.Enqueue(first, (goal - from).magnitude);
            while (open.Count > 0 && result.Expanded < MaximumExpanded)
            {
                var current = open.Dequeue();
                if (!closed.Add(current.Key)) continue;
                result.Expanded++;
                var position = Point(current.Key);
                if ((position - goal).sqrMagnitude <= 0.65f * 0.65f && clear(position, goal))
                {
                    var path = new List<Vector2>();
                    for (var node = current; node != null; node = node.Parent) path.Add(Point(node.Key));
                    path.Reverse();
                    if ((path[path.Count - 1] - goal).sqrMagnitude > 0.000001f) path.Add(goal);
                    result.Path = path;
                    result.Discovered = nodes.Count;
                    return result;
                }
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        var next = (X: current.Key.X + dx, Y: current.Key.Y + dy);
                        if (closed.Contains(next) || Occupied(next)) continue;
                        if (dx != 0 && dy != 0)
                        {
                            var sideX = (X: current.Key.X + dx, Y: current.Key.Y);
                            var sideY = (X: current.Key.X, Y: current.Key.Y + dy);
                            if (Occupied(sideX) || Occupied(sideY) || !Edge(current.Key, sideX) || !Edge(current.Key, sideY)) continue;
                        }
                        if (!Edge(current.Key, next)) continue;
                        float cost = current.Cost + CellSize * (dx != 0 && dy != 0 ? 1.41421356f : 1f);
                        if (nodes.TryGetValue(next, out var previous) && cost >= previous.Cost) continue;
                        // Queue entries are immutable: an older, more expensive entry
                        // cannot change another entry's priority or parent chain.
                        var candidate = new Node { Key = next, Cost = cost, Parent = current };
                        nodes[next] = candidate;
                        open.Enqueue(candidate, cost + (goal - Point(next)).magnitude);
                    }
            }
            result.Discovered = nodes.Count;
            return result;
        }
    }
}

