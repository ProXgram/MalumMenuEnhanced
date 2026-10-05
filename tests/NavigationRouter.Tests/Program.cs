using MalumMenu;
using UnityEngine;

int total = 0, failures = 0;
Run("direct ground movement reaches a nearby destination", () =>
{
    SetUp(); var position = new Vector2(0.17f, -0.13f); var goal = new Vector2(0.71f, 0.21f);
    int before = Physics2D.Queries;
    for (int tick = 0; tick < 100; tick++)
    {
        var direction = NavigationRouter.Direction(position, goal, out _);
        Require(direction.magnitude <= 1.0001f, "movement exceeds unit magnitude");
        if (direction.sqrMagnitude == 0) break;
        var next = position + direction * 0.035f; Require(Physics2D.Clear(position, next), "movement crosses collision"); position = next;
        Time.realtimeSinceStartupAsDouble += 0.02;
    }
    Require((position - goal).magnitude <= 0.09f, "direct movement never reached destination");
    Require(Physics2D.Queries - before < 400, "direct route unexpectedly ran grid search");
});
Run("wall detour traverses safe ground and reaches the actual goal", () =>
{
    SetUp(); Physics2D.Obstacles.Add(new(1.8f, -1f, 2.2f, 1f));
    var position = new Vector2(0.13f, 0.07f); var goal = new Vector2(4.19f, 0.11f);
    bool detoured = false;
    for (int tick = 0; tick < 500; tick++)
    {
        var direction = NavigationRouter.Direction(position, goal, out _);
        Require(direction.sqrMagnitude > 0 || (position - goal).magnitude <= 0.09f, "router stopped before reaching reachable destination");
        if (direction.sqrMagnitude == 0) break;
        var next = position + direction * 0.04f;
        Require(Physics2D.Clear(position, next) && Physics2D.Stand(next), "route clips a wall");
        position = next; detoured |= Math.Abs(position.y) > 1.15f;
        Time.realtimeSinceStartupAsDouble += 0.02;
    }
    Require(detoured && (position - goal).magnitude <= 0.09f, "wall route failed to detour and reach its exact destination");
});
Run("wall route uses an open doorway and closed door blocks the corridor", () =>
{
    SetUp(); Physics2D.Obstacles.Add(new(1.8f, -20f, 2.2f, 1f)); Physics2D.Obstacles.Add(new(1.8f, 2f, 2.2f, 20f));
    var door = new Rectangle(1.8f, 1f, 2.2f, 2f, 10) { Enabled = false }; Physics2D.Obstacles.Add(door);
    var plan = NavigationRouter.GridPlanner.Plan(new(0, 0), new(4, 0), Physics2D.Stand, Physics2D.Clear);
    Require(plan.Path != null && plan.Path.Any(point => point.y > 1.15f && point.y < 1.85f), "planner did not use open doorway");
    ValidatePath(plan.Path);
    door.Enabled = true;
    var closed = NavigationRouter.GridPlanner.Plan(new(0, 0), new(4, 0), Physics2D.Stand, Physics2D.Clear);
    Require(closed.Path == null && closed.Expanded <= 6000, "planner passed through closed door");
});
Run("planner cannot cut a diagonal past blocked orthogonal cells", () =>
{
    SetUp(); var goal = new Vector2(0.4f, 0.4f);
    bool Walkable(Vector2 point) => Near(point, Vector2.zero) || Near(point, goal);
    bool Clear(Vector2 a, Vector2 b) => !(Near(a, Vector2.zero) && Near(b, goal)) && !(Near(b, Vector2.zero) && Near(a, goal));
    var plan = NavigationRouter.GridPlanner.Plan(Vector2.zero, goal, Walkable, Clear);
    Require(plan.Path == null, "diagonal corner was cut");
});
Run("thin wall crossings are rejected even when endpoints are walkable", () =>
{
    SetUp(); Physics2D.Obstacles.Add(new(0.19f, -20f, 0.21f, 20f));
    Require(NavigationRouter.CanStand(Vector2.zero) && NavigationRouter.CanStand(new(0.4f, 0)), "fixture endpoints are not walkable");
    Require(!NavigationRouter.CanTravel(Vector2.zero, new(0.4f, 0)), "point checks missed a wall between nodes");
    var plan = NavigationRouter.GridPlanner.Plan(Vector2.zero, new(2, 0), Physics2D.Stand, Physics2D.Clear);
    Require(plan.Path == null, "grid edges traversed thin wall");
});
Run("unreachable large search obeys node budget and caches nodes/edges", () =>
{
    SetUp(); var cells = new Dictionary<string, int>(); var edges = new Dictionary<string, int>();
    string Key(Vector2 p) => p.x.ToString("R") + "," + p.y.ToString("R");
    bool Walkable(Vector2 p) { string key = Key(p); cells[key] = cells.GetValueOrDefault(key) + 1; return true; }
    bool Clear(Vector2 a, Vector2 b)
    {
        string x = Key(a), y = Key(b); string key = StringComparer.Ordinal.Compare(x, y) < 0 ? x + ";" + y : y + ";" + x;
        edges[key] = edges.GetValueOrDefault(key) + 1;
        return (a - b).magnitude < 0.6f && !(Math.Min(a.x, b.x) < 30f && Math.Max(a.x, b.x) >= 30f);
    }
    var result = NavigationRouter.GridPlanner.Plan(Vector2.zero, new(100, 0), Walkable, Clear);
    Require(result.Path == null && result.Expanded == 6000, "unreachable search did not stop at budget");
    Require(result.Discovered <= 48001, "discovery grew beyond bounded neighbours");
    Require(cells.Values.Max() == 1 && edges.Values.Max() == 1, "search repeated occupancy or undirected edge predicate work");
});
Run("map mask ignores player colliders but includes object doors", () =>
{
    SetUp(); Physics2D.Obstacles.Add(new(0.5f, -0.5f, 1.5f, 0.5f, 8));
    Require(NavigationRouter.CanTravel(Vector2.zero, new(2, 0)), "player collider blocks map navigation");
    Physics2D.Obstacles.Add(new(0.9f, -0.5f, 1.1f, 0.5f, 10));
    Require(!NavigationRouter.CanTravel(Vector2.zero, new(2, 0)), "object door was omitted");
    Require(Physics2D.Masks.SetEquals([Constants.ShipAndObjectsMask]), "query used a non-map collision mask");
});
Run("cached route avoids full search for a nearby moving goal", () =>
{
    SetUp(); Physics2D.Obstacles.Add(new(1.8f, -1f, 2.2f, 1f));
    var first = NavigationRouter.Direction(Vector2.zero, new(4, 0), out _); Require(first.sqrMagnitude > 0, "fixture route failed");
    int initial = Physics2D.Queries; Time.realtimeSinceStartupAsDouble = 0.1;
    var next = NavigationRouter.Direction(Vector2.zero, new(4, 0.2f), out _);
    Require(next.sqrMagnitude > 0 && Physics2D.Queries - initial < initial / 2, "nearby goal discarded cached route");
});
Run("new obstacle invalidates cached movement and retry is throttled", () =>
{
    SetUp(); Physics2D.Obstacles.Add(new(1.8f, -1f, 2.2f, 1f));
    Require(NavigationRouter.Direction(Vector2.zero, new(4, 0), out _).sqrMagnitude > 0, "fixture route failed");
    Physics2D.Obstacles.Add(new(0.25f, -20f, 0.45f, 20f)); Time.realtimeSinceStartupAsDouble = 0.1;
    int before = Physics2D.Queries;
    Require(NavigationRouter.Direction(Vector2.zero, new(4, 0), out _).sqrMagnitude == 0, "closed route still moved");
    Require(Physics2D.Queries - before < 50, "blocked route immediately ran another large search");
    before = Physics2D.Queries; Time.realtimeSinceStartupAsDouble = 0.8;
    Require(NavigationRouter.Direction(Vector2.zero, new(4, 0), out _).sqrMagnitude == 0, "retry walked through wall");
    Require(Physics2D.Queries - before > 50, "blocked route did not retry after interval");
});
Run("large goal changes pause stale route until safe replan", () =>
{
    SetUp(); Physics2D.Obstacles.Add(new(1.8f, -1f, 2.2f, 1f));
    NavigationRouter.Direction(Vector2.zero, new(4, 0), out _); Time.realtimeSinceStartupAsDouble = 0.1;
    Require(NavigationRouter.Direction(Vector2.zero, new(4, 0.8f), out _).sqrMagnitude == 0, "continued toward an obsolete goal");
    Time.realtimeSinceStartupAsDouble = 0.8;
    Require(NavigationRouter.Direction(Vector2.zero, new(4, 0.8f), out _).sqrMagnitude > 0, "new reachable goal never replanned");
});
Run("ship replacement invalidates route and permits immediate fresh plan", () =>
{
    SetUp(); Physics2D.Obstacles.Add(new(1.8f, -1f, 2.2f, 1f));
    NavigationRouter.Direction(Vector2.zero, new(4, 0), out _); int before = Physics2D.Queries;
    ShipStatus.Instance = new(); Time.realtimeSinceStartupAsDouble = 0.1;
    Require(NavigationRouter.Direction(Vector2.zero, new(4, 0), out _).sqrMagnitude > 0 && Physics2D.Queries - before > 50, "new map inherited stale cache/throttle");
});
foreach (var point in new[] { new Vector2(float.NaN, 0), new Vector2(0, float.PositiveInfinity), new Vector2(1e30f, 0), new Vector2(200, 0) })
    Run("invalid or excessive destination is rejected without physics/allocation: " + point, () =>
    {
        SetUp(); Require(NavigationRouter.Direction(Vector2.zero, point, out _).sqrMagnitude == 0, "invalid coordinates produced movement");
        Require(Physics2D.Queries == 0, "invalid coordinate reached physics");
    });
Run("blocked goal does not silently substitute a remote destination", () =>
{
    SetUp(); Physics2D.Obstacles.Add(new(1.8f, -1f, 2.2f, 1f));
    Require(NavigationRouter.Direction(Vector2.zero, new(2, 0), out _).sqrMagnitude == 0, "blocked goal was displaced implicitly");
});
Run("physics failure stops movement safely", () =>
{
    SetUp(); Physics2D.Throw = true;
    Require(NavigationRouter.Direction(Vector2.zero, new(2, 0), out _).sqrMagnitude == 0, "failed physics yielded movement");
    Require(!NavigationRouter.CanStand(Vector2.zero) && !NavigationRouter.CanTravel(Vector2.zero, new(1, 0)), "helper propagated physics failure as clear");
});
Run("Reset discards failed-route retry state", () =>
{
    SetUp(); Physics2D.Obstacles.Add(new(1.8f, -20f, 2.2f, 20f));
    NavigationRouter.Direction(Vector2.zero, new(4, 0), out _); int before = Physics2D.Queries;
    Time.realtimeSinceStartupAsDouble = 0.1; NavigationRouter.Reset();
    NavigationRouter.Direction(Vector2.zero, new(4, 0), out _);
    Require(Physics2D.Queries - before > 50, "Reset retained retry throttle");
});
Run("native body radius blocks a gap that fixed clearance would accept", () =>
{
    SetUp(); Actor(0.23f);
    Physics2D.Obstacles.Add(new(-20, 0.21f, 20, 1)); Physics2D.Obstacles.Add(new(-20, -1, 20, -0.21f));
    Require(!NavigationRouter.CanStand(Vector2.zero), "full native body was squeezed into a narrow gap");
    Require(NavigationRouter.Direction(Vector2.zero, new(2, 0), out _).sqrMagnitude == 0, "opposing contacts produced unsafe escape");
    Require(Physics2D.Radii.All(radius => radius >= 0.254f), "query did not include native radius plus margin");
});
Run("real collider center offset is applied to candidate ground points", () =>
{
    SetUp(); Actor(0.1f, new(0, 0.4f)); Physics2D.Obstacles.Add(new(-1, 0.55f, 1, 0.7f));
    Require(!NavigationRouter.CanStand(Vector2.zero), "candidate query ignored world collider-center offset");
    Require(Physics2D.Centers.Any(point => Near(point, new(0, 0.4f))), "query used logical point instead of collider center");
});
Run("non-solid trigger objects do not trap navigation", () =>
{
    SetUp(); Actor(0.2f); var trigger = new Rectangle(-1, -1, 3, 1, 10); trigger.Collider.isTrigger = true; Physics2D.Obstacles.Add(trigger);
    Require(NavigationRouter.CanStand(Vector2.zero) && NavigationRouter.CanTravel(Vector2.zero, new(2, 0)), "trigger was treated as solid floor obstruction");
    Require(NavigationRouter.Direction(Vector2.zero, new(2, 0), out _).x > 0.9f, "trigger blocked direct movement");
});
Run("ShortObjects are included only when native player collision mask requires them", () =>
{
    SetUp(); Actor(0.2f); Physics2D.CollisionMask = Constants.ShipAndAllObjectsMask;
    Physics2D.Obstacles.Add(new(0.8f, -1, 1.2f, 1, 11));
    Require(!NavigationRouter.CanTravel(Vector2.zero, new(2, 0)), "colliding short item was omitted");
    Physics2D.CollisionMask = Constants.ShipAndObjectsMask;
    Require(NavigationRouter.CanTravel(Vector2.zero, new(2, 0)), "noncolliding short decoration was included");
});
Run("radius change immediately invalidates cached clearance", () =>
{
    SetUp(); var actor = Actor(0.1f); Physics2D.Obstacles.Add(new(-20, 0.21f, 20, 1)); Physics2D.Obstacles.Add(new(-20, -1, 20, -0.21f));
    Require(NavigationRouter.Direction(Vector2.zero, new(2, 0), out _).x > 0.9f, "small body fixture did not fit");
    actor.Collider.WorldRadius = 0.23f; Time.realtimeSinceStartupAsDouble = 0.1;
    Require(NavigationRouter.Direction(Vector2.zero, new(2, 0), out _).sqrMagnitude == 0, "larger body inherited unsafe route");
});
Run("blocked contact start automatically walks outward instead of deadlocking", () =>
{
    SetUp(); var actor = Actor(0.23f); Physics2D.Obstacles.Add(new(0.23f, -10, 1, 10));
    var direction = NavigationRouter.Direction(Vector2.zero, new(-2, 0), out var status);
    Require(direction.x < -0.9f && status.Contains("away", StringComparison.Ordinal), "start contact did not recover outward");
    for (int step = 0; step < 5 && !NavigationRouter.CanStand(actor.TruePosition); step++)
    {
        direction = NavigationRouter.RecoverDirection(actor.TruePosition, out _);
        Require(direction.x < 0 && direction.magnitude <= 1.0001f, "recovery pointed farther into item");
        actor.TruePosition += direction * 0.04f;
    }
    Require(NavigationRouter.CanStand(actor.TruePosition), "short ground recovery never exited clearance contact");
});
Run("recovery refuses to cross a new obstacle behind the contact", () =>
{
    SetUp(); Actor(0.23f); Physics2D.Obstacles.Add(new(0.23f, -20, 1, 20)); Physics2D.Obstacles.Add(new(-0.3f, -20, -0.28f, 20));
    Require(NavigationRouter.RecoverDirection(Vector2.zero, out _).sqrMagnitude == 0, "escape crossed new obstacle");
});
Run("recovery has no arbitrary direction when embedded deeply in an item", () =>
{
    SetUp(); Actor(0.2f); Physics2D.Obstacles.Add(new(-1, -1, 1, 1));
    Require(NavigationRouter.RecoverDirection(Vector2.zero, out _).sqrMagnitude == 0, "deep embedding invented unsafe outward normal");
});
Run("recovery is harmless on already clear ground", () =>
{
    SetUp(); Actor(0.2f);
    Require(NavigationRouter.RecoverDirection(Vector2.zero, out _).sqrMagnitude == 0, "recovery moved without a contact");
});
Run("unavailable native collider geometry blocks without physics queries", () =>
{
    SetUp(); Actor(float.NaN);
    Require(!NavigationRouter.CanStand(Vector2.zero) && NavigationRouter.Direction(Vector2.zero, new(1, 0), out _).sqrMagnitude == 0, "invalid body radius was ignored");
    Require(Physics2D.Queries == 0, "invalid geometry reached native physics queries");
});
Run("collision matrix uses the actual collider's layer", () =>
{
    SetUp(); var actor = Actor(0.2f); actor.gameObject.layer = 8; actor.Collider.gameObject.layer = 12;
    NavigationRouter.CanStand(Vector2.zero);
    Require(Physics2D.LastCollisionLayer == 12, "player root layer replaced collider layer");
});
Run("noncircular collider geometry conservatively covers bounding-box corners", () =>
{
    SetUp(); var actor = Actor(0.23f); actor.Collider = new Collider2D { Owner = actor, WorldRadius = 0.23f };
    NavigationRouter.CanStand(Vector2.zero);
    Require(Physics2D.Radii.Single() > 0.35f, "unknown body shape was underbounded by maximum half-width");
});
Run("scaled circle geometry uses full world radius", () =>
{
    SetUp(); var actor = Actor(0.15f); actor.Collider.transform.lossyScale = new(2, 1, 1);
    NavigationRouter.CanStand(Vector2.zero);
    Require(Physics2D.Radii.Single() >= 0.324f, "query ignored circle's world scale");
});
Run("unavailable world bounds do not invent a collider center at origin", () =>
{
    SetUp(); var actor = Actor(0.2f); actor.TruePosition = new(10, 10); actor.Collider.BoundsUnavailable = true;
    Require(!NavigationRouter.CanStand(actor.TruePosition) && Physics2D.Queries == 0, "zero bounds redirected query to origin");
});
Run("incremental search completes a detour with bounded nodes and predicate work per call", () =>
{
    SetUp(); Physics2D.Obstacles.Add(new(9.8f, -4, 10.2f, 4));
    int predicates = 0;
    bool Walkable(Vector2 point) { predicates++; return Physics2D.Stand(point); }
    bool Clear(Vector2 from, Vector2 to) { predicates++; return Physics2D.Clear(from, to); }
    var search = new NavigationRouter.GridPlanner.MovingSearch(Vector2.zero, new(20, 0), Walkable, Clear);
    int steps = 0;
    while (!search.Finished && steps++ < 1000)
    {
        int expanded = search.Result.Expanded, before = predicates;
        search.Step();
        Require(search.Result.Expanded - expanded <= 32, "one call expanded a whole search");
        Require(predicates - before <= 128, "one call exceeded its physics predicate budget");
    }
    Require(steps > 1 && search.Finished && search.Result.Path != null, "resumed search never found reachable detour");
    ValidatePath(search.Result.Path);
});
Run("moving-target runtime bounds native-style query counts and walks its completed route", () =>
{
    SetUp(); Physics2D.Obstacles.Add(new(1.8f, -1, 2.2f, 1));
    var position = Vector2.zero; var goal = new Vector2(4, 0); bool waited = false, detoured = false;
    for (int tick = 0; tick < 1000 && (position - goal).magnitude > 0.09f; tick++)
    {
        int before = Physics2D.Queries;
        var direction = NavigationRouter.DirectionToMovingTarget(position, goal, out _);
        Require(Physics2D.Queries - before <= 155, "moving-target call performed unbounded physics work");
        waited |= direction.sqrMagnitude == 0f;
        var next = position + direction * 0.04f;
        Require(Physics2D.Stand(next) && Physics2D.Clear(position, next), "moving route crossed collision");
        position = next; detoured |= Math.Abs(position.y) > 1.15f;
        Time.realtimeSinceStartupAsDouble += 0.02;
    }
    Require(waited && detoured && (position - goal).magnitude <= 0.09f, "bounded moving search did not finish and reach goal");
});
Run("cached moving route keeps walking during cooldown and replacement search", () =>
{
    SetUp(); Physics2D.Obstacles.Add(new(1.8f, -1, 2.2f, 1));
    PrimeMovingRoute(Vector2.zero, new(4, 0));
    Time.realtimeSinceStartupAsDouble += 0.02;
    int before = Physics2D.Queries;
    Require(NavigationRouter.DirectionToMovingTarget(Vector2.zero, new(14, 0), out _).sqrMagnitude > 0f,
        "a changed goal stopped a still-safe route during cooldown");
    Require(Physics2D.Queries - before <= 15, "cooldown unexpectedly began another search");
    Time.realtimeSinceStartupAsDouble += 0.8;
    before = Physics2D.Queries;
    Require(NavigationRouter.DirectionToMovingTarget(Vector2.zero, new(14, 0), out var status).sqrMagnitude > 0f,
        "pending replacement stopped the cached route");
    Require(status.Contains("planning", StringComparison.Ordinal) && Physics2D.Queries - before <= 155,
        "replacement search did not stay incremental");
});
Run("moving search goal updates do not restart pending work on every network sample", () =>
{
    SetUp(); Physics2D.Obstacles.Add(new(9.8f, -4, 10.2f, 4));
    bool found = false;
    for (int tick = 0; tick < 1000; tick++)
    {
        var goal = new Vector2(20, (tick & 1) == 0 ? -0.5f : 0.5f);
        int before = Physics2D.Queries;
        var direction = NavigationRouter.DirectionToMovingTarget(Vector2.zero, goal, out _);
        Require(Physics2D.Queries - before <= 155, "jitter caused an unbounded synchronous replacement");
        if (direction.sqrMagnitude > 0f) { found = true; break; }
        Time.realtimeSinceStartupAsDouble += 0.02;
    }
    Require(found, "pending search was starved by shifting targets");
});
Run("failed moving search retries from completion time instead of immediately rebuilding", () =>
{
    SetUp(); Physics2D.Obstacles.Add(new(1.8f, -20, 2.2f, 20));
    string status = "";
    for (int tick = 0; tick < 1000; tick++)
    {
        int before = Physics2D.Queries;
        Require(NavigationRouter.DirectionToMovingTarget(Vector2.zero, new(4, 0), out status).sqrMagnitude == 0f,
            "unreachable moving goal produced movement");
        Require(Physics2D.Queries - before <= 155, "unreachable moving goal exceeded query budget");
        Time.realtimeSinceStartupAsDouble += 0.02;
        if (status == "Waiting for a clear ground route") break;
    }
    Require(status == "Waiting for a clear ground route", "failed search never reached a bounded terminal result");
    int finalQueries = Physics2D.Queries;
    NavigationRouter.DirectionToMovingTarget(Vector2.zero, new(4, 0), out _);
    Require(Physics2D.Queries - finalQueries == 3, "finished failure immediately allocated and ran another search");
});
Run("moving route rejects a newly blocked target and newly blocked live segments", () =>
{
    SetUp(); Physics2D.Obstacles.Add(new(1.8f, -1, 2.2f, 1));
    PrimeMovingRoute(Vector2.zero, new(4, 0));
    Physics2D.Obstacles.Add(new(3.8f, -0.2f, 4.2f, 0.2f));
    Require(NavigationRouter.DirectionToMovingTarget(Vector2.zero, new(4, 0), out var status).sqrMagnitude == 0f &&
        status == "Destination is blocked", "cached route ignored current target occupancy");
    SetUp(); Physics2D.Obstacles.Add(new(1.8f, -1, 2.2f, 1));
    PrimeMovingRoute(Vector2.zero, new(4, 0));
    Physics2D.Obstacles.Add(new(0.25f, -20, 0.45f, 20));
    Physics2D.Obstacles.Add(new(-0.45f, -20, -0.25f, 20));
    Physics2D.Obstacles.Add(new(-20, 0.25f, 20, 0.45f));
    Physics2D.Obstacles.Add(new(-20, -0.45f, 20, -0.25f));
    for (int tick = 0; tick < 50; tick++)
    {
        Require(NavigationRouter.DirectionToMovingTarget(Vector2.zero, new(4, 0), out _).sqrMagnitude == 0f,
            "cached or incremental route crossed a newly closed passage");
        Time.realtimeSinceStartupAsDouble += 0.02;
    }
});
Run("moving context geometry and nonfinite time invalidate pending work safely", () =>
{
    SetUp(); var actor = Actor(0.1f); Physics2D.Obstacles.Add(new(1.8f, -1, 2.2f, 1));
    NavigationRouter.DirectionToMovingTarget(Vector2.zero, new(4, 0), out _);
    actor.Collider.WorldRadius = float.NaN;
    int before = Physics2D.Queries;
    Require(NavigationRouter.DirectionToMovingTarget(Vector2.zero, new(4, 0), out _).sqrMagnitude == 0f &&
        Physics2D.Queries == before, "invalid changed body used pending movement");
    actor.Collider.WorldRadius = 0.1f;
    Time.realtimeSinceStartupAsDouble = double.NaN;
    Require(NavigationRouter.DirectionToMovingTarget(Vector2.zero, new(4, 0), out _).sqrMagnitude == 0f &&
        Physics2D.Queries == before, "nonfinite time used a pending route");
});
Run("pending moving route remains observable past five seconds and clears on bounded failure or reset", () =>
{
    SetUp(); Physics2D.Obstacles.Add(new(29.8f, -20, 30.2f, 20));
    NavigationRouter.DirectionToMovingTarget(Vector2.zero, new(100, 0), out _);
    Require(NavigationRouter.IsMovingRoutePending, "large bounded search was not exposed as pending");
    Time.realtimeSinceStartupAsDouble = 6;
    NavigationRouter.DirectionToMovingTarget(Vector2.zero, new(100, 0), out _);
    Require(NavigationRouter.IsMovingRoutePending, "elapsed caller time discarded a still-pending search");
    for (int tick = 0; tick < 1000 && NavigationRouter.IsMovingRoutePending; tick++)
    {
        int before = Physics2D.Queries;
        NavigationRouter.DirectionToMovingTarget(Vector2.zero, new(100, 0), out _);
        Require(Physics2D.Queries - before <= 155, "long pending search exceeded per-call work budget");
        Time.realtimeSinceStartupAsDouble += 0.02;
    }
    Require(!NavigationRouter.IsMovingRoutePending, "unreachable search exceeded its total node bound");
    Time.realtimeSinceStartupAsDouble += 0.8;
    NavigationRouter.DirectionToMovingTarget(Vector2.zero, new(100, 0), out _);
    Require(NavigationRouter.IsMovingRoutePending, "fixture did not begin its throttled retry");
    NavigationRouter.Reset();
    Require(!NavigationRouter.IsMovingRoutePending, "reset retained unfinished planning");
});
Run("native frame center drift does not starve a far incremental search", () =>
{
    SetUp(); var actor = Actor(0.1564f); Physics2D.Obstacles.Add(new(9.8f, -4, 10.2f, 4));
    var offsets = new[] { new Vector2(0, 0.1091f), new Vector2(-0.0217f, 0.1078f), new Vector2(-0.0041f, 0.131f) };
    bool found = false;
    for (int tick = 0; tick < 1000; tick++)
    {
        actor.Collider.CenterOffset = offsets[tick % offsets.Length];
        int before = Physics2D.Queries;
        var direction = NavigationRouter.DirectionToMovingTarget(Vector2.zero, new(20, 0), out _);
        Require(Physics2D.Queries - before <= 155, "drift triggered an unbounded search");
        if (direction.sqrMagnitude > 0f) { found = true; break; }
        Time.realtimeSinceStartupAsDouble += 0.02;
    }
    Require(found, "small native interpolation drift restarted pending work indefinitely");
});
Run("walking frame drift retains the far route and checks the current body center", () =>
{
    SetUp(); var actor = Actor(0.1564f, new(0, 0.1091f)); Physics2D.Obstacles.Add(new(9.8f, -4, 10.2f, 4));
    PrimeMovingRoute(Vector2.zero, new(20, 0));
    var offsets = new[] { new Vector2(0, 0.1091f), new Vector2(-0.0217f, 0.1078f), new Vector2(-0.0041f, 0.131f) };
    for (int tick = 0; tick < 60; tick++)
    {
        actor.Collider.CenterOffset = offsets[tick % offsets.Length];
        int before = Physics2D.Queries, centers = Physics2D.Centers.Count;
        var position = actor.TruePosition;
        var direction = NavigationRouter.DirectionToMovingTarget(position, new(20, 0), out _);
        Require(direction.sqrMagnitude > 0f && !NavigationRouter.IsMovingRoutePending,
            "walking center drift discarded a usable cached route");
        Require(Physics2D.Queries - before <= 15, "walking center drift rebuilt the completed route");
        Require(Near(Physics2D.Centers[centers], position + actor.Collider.CenterOffset),
            "live movement reused the old collider-center offset");
        var next = position + direction * 0.06f;
        Require(NavigationRouter.CanTravel(position, next), "drifting-body step crosses native-style collision");
        actor.TruePosition = next; Time.realtimeSinceStartupAsDouble += 0.02;
    }
});
Run("static AI route also retains safe cache across small frame center drift", () =>
{
    SetUp(); var actor = Actor(0.1564f, new(0, 0.1091f)); Physics2D.Obstacles.Add(new(1.8f, -1, 2.2f, 1));
    Require(NavigationRouter.Direction(Vector2.zero, new(4, 0), out _).sqrMagnitude > 0f, "static route fixture failed");
    actor.Collider.CenterOffset = new(-0.0217f, 0.1078f);
    int before = Physics2D.Queries, centers = Physics2D.Centers.Count;
    Require(NavigationRouter.Direction(Vector2.zero, new(4, 0), out _).sqrMagnitude > 0f && Physics2D.Queries - before <= 15,
        "small translation restarted the static AI planner");
    Require(Near(Physics2D.Centers[centers], actor.Collider.CenterOffset), "static route used stale center geometry");
});
Run("substantial body center jump still discards old moving clearance", () =>
{
    SetUp(); var actor = Actor(0.1564f, new(0, 0.1091f)); Physics2D.Obstacles.Add(new(9.8f, -4, 10.2f, 4));
    PrimeMovingRoute(Vector2.zero, new(20, 0));
    actor.Collider.CenterOffset = new(0, 0.45f);
    var direction = NavigationRouter.DirectionToMovingTarget(Vector2.zero, new(20, 0), out _);
    Require(direction.sqrMagnitude == 0f && NavigationRouter.IsMovingRoutePending,
        "substantial center change inherited the old route");
});
foreach (float maximumStep in new[] { 0.06f, 0.24f })
Run("far continuously moving target is reached through two doorways with frame drift at step " + maximumStep, () =>
{
    SetUp(); var actor = Actor(0.1564f, new(0, 0.1091f));
    Physics2D.Obstacles.Add(new(-2, -3.5f, 45, -3)); Physics2D.Obstacles.Add(new(-2, 3, 45, 3.5f));
    Physics2D.Obstacles.Add(new(5.8f, -3, 6.2f, -0.6f)); Physics2D.Obstacles.Add(new(5.8f, 0.6f, 6.2f, 3));
    Physics2D.Obstacles.Add(new(13.8f, -3, 14.2f, 0.8f)); Physics2D.Obstacles.Add(new(13.8f, 2, 14.2f, 3));
    var offsets = new[] { new Vector2(0, 0.1091f), new Vector2(-0.0217f, 0.1078f), new Vector2(-0.0041f, 0.131f) };
    bool crossedSecondDoor = false, caught = false;
    for (int tick = 0; tick < 1500; tick++)
    {
        actor.Collider.CenterOffset = offsets[tick % offsets.Length];
        float travel = (tick * 0.02f * 2f) % 20f;
        var goal = new Vector2(30f + (travel <= 10f ? travel : 20f - travel), 1.4f);
        var position = actor.TruePosition;
        int before = Physics2D.Queries;
        var direction = NavigationRouter.DirectionToMovingTarget(position, goal, out _);
        Require(Physics2D.Queries - before <= 155, "far moving route exceeded its per-call query budget");
        var next = position + direction * Math.Min(maximumStep, NavigationRouter.SteeringDistance);
        Require(NavigationRouter.CanTravel(position, next), "far pursuit crosses a corridor wall or doorway corner");
        actor.TruePosition = next; crossedSecondDoor |= next.x > 14.4f;
        if ((next - goal).magnitude < 2f) { caught = true; break; }
        Time.realtimeSinceStartupAsDouble += 0.02;
    }
    Require(crossedSecondDoor && caught, "far moving target was only reachable after starting nearby");
});
Run("Storage crate route joins an earlier visible point instead of the nearest point behind the crate", () =>
{
    SetUp(); Physics2D.Obstacles.Add(new(0.8f, 0, 1.1f, 2));
    var path = new List<Vector2>
    {
        new(-0.4f, -1.2f), new(0, -1.2f), new(0.4f, -1.2f), new(0.8f, -1.2f), new(1.2f, -1.2f),
        new(1.4f, -0.8f), new(1.4f, -0.4f), new(1.4f, 0), new(1.4f, 0.4f), new(1.4f, 0.8f),
        new(1.4f, 1.2f), new(1.4f, 1.6f), new(1.4f, 2.4f), new(2, 2.8f),
    };
    ValidatePath(path);
    var position = new Vector2(0.4f, 1);
    Require(!Physics2D.Clear(position, path[9]), "fixture nearest waypoint is not behind the crate");
    int predicates = 0;
    bool Stand(Vector2 point) { predicates++; return Physics2D.Stand(point); }
    bool Clear(Vector2 from, Vector2 to) { predicates++; return Physics2D.Clear(from, to); }
    int join = NavigationRouter.ReachableJoin(path, position, Stand, Clear);
    Require(join >= 1 && join < 9 && Physics2D.Clear(position, path[join]), "valid earlier join was discarded");
    Require(predicates <= 12, "reachable join scanned the whole path with physics");
});
Run("moving steering distance describes the returned segment and clears on stop or blocked goal", () =>
{
    SetUp();
    var direction = NavigationRouter.DirectionToMovingTarget(Vector2.zero, new(2.3f, 0), out _);
    Require(direction.x > 0.99f && Math.Abs(NavigationRouter.SteeringDistance - 2.3f) < 0.0001f &&
        !NavigationRouter.IsRecovering, "direct route reported the wrong movement segment");
    Physics2D.Obstacles.Add(new(2.1f, -0.3f, 2.5f, 0.3f));
    Require(NavigationRouter.DirectionToMovingTarget(Vector2.zero, new(2.3f, 0), out _).sqrMagnitude == 0f &&
        NavigationRouter.SteeringDistance == 0f && !NavigationRouter.IsRecovering, "blocked route retained an old segment cap");
    NavigationRouter.Reset();
    Require(NavigationRouter.SteeringDistance == 0f && !NavigationRouter.IsRecovering, "reset retained movement metadata");
});
Run("moving recovery reports only its validated outward endpoint distance", () =>
{
    SetUp(); Actor(0.23f); Physics2D.Obstacles.Add(new(0.23f, -10, 1, 10));
    var direction = NavigationRouter.DirectionToMovingTarget(Vector2.zero, new(-2, 0), out _);
    Require(direction.x < -0.9f && NavigationRouter.IsRecovering && NavigationRouter.SteeringDistance > 0f &&
        NavigationRouter.SteeringDistance <= 0.6f, "recovery did not describe a bounded outward segment");
    Require(NavigationRouter.CanStand(direction * NavigationRouter.SteeringDistance), "reported recovery endpoint is blocked");
});
Run("fast proposed steps capped by live steering distance traverse mixed-vector waypoints", () =>
{
    SetUp(); var actor = Actor(0.1564f, new(0, 0.1091f)); Physics2D.Obstacles.Add(new(1.8f, -1, 2.2f, 1));
    var goal = new Vector2(4.19f, 0.11f); bool detoured = false;
    for (int tick = 0; tick < 500 && (actor.TruePosition - goal).magnitude > 0.09f; tick++)
    {
        var position = actor.TruePosition;
        var direction = NavigationRouter.DirectionToMovingTarget(position, goal, out _);
        if (direction.sqrMagnitude > 0f)
        {
            float distance = Math.Min(0.24f, NavigationRouter.SteeringDistance);
            Require(distance > 0f, "returned route direction had no segment cap");
            var next = position + direction * distance;
            Require(NavigationRouter.CanTravel(position, next), "fast capped step clipped the wall");
            actor.TruePosition = next; detoured |= Math.Abs(next.y) > 1.1f;
        }
        Time.realtimeSinceStartupAsDouble += 0.02;
    }
    Require(detoured && (actor.TruePosition - goal).magnitude <= 0.09f,
        "fast steps oscillated around an intermediate waypoint instead of reaching the goal");
});
Run("failed replacement search retains safe older movement without an endpoint failure signal", () =>
{
    SetUp(); Physics2D.Obstacles.Add(new(1.8f, -1, 2.2f, 1));
    PrimeMovingRoute(Vector2.zero, new(4, 0));
    Physics2D.Obstacles.Add(new(7.8f, -20, 8.2f, 20));
    Time.realtimeSinceStartupAsDouble += 0.8;
    bool wasPending = false, finished = false;
    for (int tick = 0; tick < 1000; tick++)
    {
        var direction = NavigationRouter.DirectionToMovingTarget(Vector2.zero, new(12, 0), out _);
        Require(direction.sqrMagnitude > 0f && !NavigationRouter.MovingRouteFailed,
            "failed replacement endpoint discarded safe retained movement or signalled a stop");
        var next = direction * Math.Min(0.06f, NavigationRouter.SteeringDistance);
        Require(NavigationRouter.CanTravel(Vector2.zero, next), "retained older movement was not live-checked");
        if (wasPending && !NavigationRouter.IsMovingRoutePending) { finished = true; break; }
        wasPending |= NavigationRouter.IsMovingRoutePending;
        Time.realtimeSinceStartupAsDouble += 0.02;
    }
    Require(finished, "unreachable replacement search did not finish while the old segment remained usable");
});
Run("completed unreachable moving search signals failure once and records its actual endpoint", () =>
{
    SetUp(); Physics2D.Obstacles.Add(new(1.8f, -20, 2.2f, 20));
    var goal = new Vector2(4, 0); var traces = new List<string>(); NavigationRouter.FailureTrace = traces.Add;
    bool failed = false;
    for (int tick = 0; tick < 1000; tick++)
    {
        var direction = NavigationRouter.DirectionToMovingTarget(Vector2.zero, goal, out _);
        if (NavigationRouter.MovingRouteFailed)
        {
            Require(direction.sqrMagnitude == 0f && !NavigationRouter.IsMovingRoutePending,
                "completed failure returned movement or retained pending work");
            Require(NavigationRouter.LastFailure.Contains("no path"), "completed search lost its failure reason");
            failed = true; break;
        }
        Require(NavigationRouter.IsMovingRoutePending, "failure was not exposed at completion");
        Time.realtimeSinceStartupAsDouble += 0.02;
    }
    Require(failed, "unreachable endpoint never produced the completion signal");
    Require(traces.Count == 1 && traces[0].Contains("no path") && traces[0].Contains("searchGoal=(4,0)") &&
        traces[0].Contains("expanded=") && traces[0].Contains("discovered=") &&
        traces[0].Contains("geometryResetsDuringSearch=") && traces[0].Contains("retainedMovement=False"),
        "failed-search trace omitted its outcome, endpoint or search diagnostics");
    NavigationRouter.DirectionToMovingTarget(Vector2.zero, goal, out _);
    Require(!NavigationRouter.MovingRouteFailed && !NavigationRouter.IsMovingRoutePending && traces.Count == 1,
        "cooldown repeated a stale completion signal or failure trace");
    Require(NavigationRouter.DirectionToMovingTarget(Vector2.zero, new(-2, 0), out _).x < -0.99f &&
        !NavigationRouter.MovingRouteFailed, "a reachable alternate floor goal inherited the previous failure");
    NavigationRouter.Reset(); Require(!NavigationRouter.MovingRouteFailed, "reset retained an endpoint failure");
});
Run("completed stale route without a live reachable join signals endpoint failure", () =>
{
    SetUp(); Actor(0.1564f); Physics2D.Obstacles.Add(new(9.8f, -4, 10.2f, 4));
    var goal = new Vector2(20, 0);
    for (int tick = 0; tick < 2; tick++)
    {
        NavigationRouter.DirectionToMovingTarget(Vector2.zero, goal, out _);
        Require(NavigationRouter.IsMovingRoutePending && !NavigationRouter.MovingRouteFailed,
            "fixture completed before its doorway changed");
        Time.realtimeSinceStartupAsDouble += 0.02;
    }
    // Keep the actor clear while closing its tiny room after the pending
    // planner cached the old exit. The rest of the historical detour remains open.
    Physics2D.Obstacles.Add(new(0.3f, -0.5f, 0.5f, 0.5f));
    Physics2D.Obstacles.Add(new(-0.5f, -0.5f, -0.3f, 0.5f));
    Physics2D.Obstacles.Add(new(-0.5f, 0.3f, 0.5f, 0.5f));
    Physics2D.Obstacles.Add(new(-0.5f, -0.5f, 0.5f, -0.3f));
    Require(NavigationRouter.CanStand(Vector2.zero), "fixture embeds the actor instead of closing its exit");
    bool failed = false;
    for (int tick = 0; tick < 1000; tick++)
    {
        var direction = NavigationRouter.DirectionToMovingTarget(Vector2.zero, goal, out _);
        Require(direction.sqrMagnitude == 0f, "pending historical route crossed the newly closed exit");
        if (NavigationRouter.MovingRouteFailed)
        {
            Require(!NavigationRouter.IsMovingRoutePending && NavigationRouter.LastFailure.Contains("no reachable join"),
                "live join rejection was not distinguished from no path");
            failed = true; break;
        }
        Time.realtimeSinceStartupAsDouble += 0.02;
    }
    Require(failed, "historical route without a live join did not expose a completed failure");
});
Console.WriteLine($"{total - failures}/{total} tests passed; synthetic geometry only, no game/network calls.");
return failures == 0 ? 0 : 1;

void SetUp() { NavigationRouter.Reset(); NavigationRouter.FailureTrace = null; PlayerControl.LocalPlayer = null; ShipStatus.Instance = new(); Time.realtimeSinceStartupAsDouble = 0; Physics2D.Obstacles.Clear(); Physics2D.Masks.Clear(); Physics2D.Overlaps = 0; Physics2D.Sweeps = 0; Physics2D.Throw = false; Physics2D.CollisionMask = Constants.ShipAndObjectsMask; Physics2D.Radii.Clear(); Physics2D.Centers.Clear(); }
PlayerControl Actor(float radius, Vector2 offset = default) { var actor = new PlayerControl(); actor.Collider = new CircleCollider2D { Owner = actor, WorldRadius = radius, CenterOffset = offset }; PlayerControl.LocalPlayer = actor; return actor; }
void Run(string name, Action test) { total++; try { test(); Console.WriteLine("PASS " + name); } catch (Exception error) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + error.Message); } }
void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
bool Near(Vector2 a, Vector2 b) => (a - b).sqrMagnitude < 0.000001f;
void ValidatePath(List<Vector2> path) { Require(path.Count >= 2, "route has no movement"); for (int i = 1; i < path.Count; i++) Require(Physics2D.Stand(path[i]) && Physics2D.Clear(path[i - 1], path[i]), "path crosses blocked geometry"); }
void PrimeMovingRoute(Vector2 from, Vector2 goal)
{
    for (int tick = 0; tick < 1000; tick++)
    {
        if (NavigationRouter.DirectionToMovingTarget(from, goal, out _).sqrMagnitude > 0f) return;
        Time.realtimeSinceStartupAsDouble += 0.02;
    }
    throw new InvalidOperationException("moving route fixture did not finish planning");
}
