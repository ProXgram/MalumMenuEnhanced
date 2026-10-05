using MalumMenu;
using RoleSlot = MalumMenu.AlwaysImpostorPlanner.RoleSlot;
using Swap = MalumMenu.AlwaysImpostorPlanner.Swap;

var tests = new (string Name, Action Run)[]
{
    ("Disabled setting does nothing", () => ExpectNoPlan(DefaultPlayers(), enabled: false)),
    ("Non-host cannot change roles", () => ExpectNoPlan(DefaultPlayers(), isHost: false)),
    ("Hide-and-seek is excluded", () => ExpectNoPlan(DefaultPlayers(), normalMode: false)),
    ("Null snapshot is rejected", () => ExpectNoPlan(null)),
    ("Empty snapshot is rejected", () => ExpectNoPlan([])),
    ("Missing local player is rejected", () => ExpectNoPlan([Impostor(2)])),
    ("Dead local player is rejected", () => ExpectNoPlan([Crew(1) with { Alive = false }, Impostor(2)])),
    ("Disconnected local player is rejected", () => ExpectNoPlan([Crew(1) with { Connected = false }, Impostor(2)])),
    ("Existing basic impostor keeps its role", () => ExpectNoPlan([Impostor(1), Impostor(2)])),
    ("Existing special impostor keeps its role", () => ExpectNoPlan([Impostor(1, 18), Impostor(2, 5)])),
    ("No impostor donor is rejected", () => ExpectNoPlan([Crew(1), Crew(2, 12)])),
    ("Dead impostor donor is rejected", () => ExpectNoPlan([Crew(1), Impostor(2) with { Alive = false }])),
    ("Disconnected impostor donor is rejected", () => ExpectNoPlan([Crew(1), Impostor(2) with { Connected = false }])),
    ("Single impostor swaps with local crew", SingleImpostorSwap),
    ("Multiple impostors use lowest eligible ID", MultipleImpostorSwap),
    ("Dead and disconnected donors are skipped", SkipUnavailableDonors),
    ("Special roles and team counts are conserved", SpecialRoleConservation),
    ("Repeated application makes no further swap", RepeatedApplication),
    ("Input snapshot remains unchanged", DoesNotMutateSnapshot),
    ("Duplicate local ID is rejected", () => ExpectNoPlan([Crew(1), Crew(1, 12), Impostor(2)])),
    ("Duplicate donor ID is rejected", () => ExpectNoPlan([Crew(1), Impostor(2), Impostor(2, 18)])),
    ("Player ID zero is valid", PlayerIdZero),
    ("Every role-pair preserves exact IDs", AllRolePairConservation),
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
    }
}

Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
return failures == 0 ? 0 : 1;

static RoleSlot Crew(byte id, ushort role = 0) => new(id, role, false, true, true);
static RoleSlot Impostor(byte id, ushort role = 1) => new(id, role, true, true, true);
static RoleSlot[] DefaultPlayers() => [Crew(1), Impostor(2), Crew(3)];

static void ExpectNoPlan(
    IReadOnlyList<RoleSlot>? players,
    bool enabled = true,
    bool isHost = true,
    bool normalMode = true)
{
    var result = AlwaysImpostorPlanner.TryPlan(enabled, isHost, normalMode, 1, players!, out var swap);
    Require(!result, "Unexpected swap plan.");
    Require(swap == default, "A rejected plan must clear its output.");
}

static Swap Plan(IReadOnlyList<RoleSlot> players, byte localId = 1)
{
    Require(AlwaysImpostorPlanner.TryPlan(true, true, true, localId, players, out var swap), "Expected a swap plan.");
    Require(swap.LocalPlayerId != swap.DonorPlayerId, "Cannot swap a player with itself.");
    return swap;
}

static RoleSlot[] Apply(IReadOnlyList<RoleSlot> players, Swap swap)
{
    return players.Select(player =>
        player.PlayerId == swap.LocalPlayerId
            ? player with { RoleId = swap.DonorOriginalRole, IsImpostor = true }
            : player.PlayerId == swap.DonorPlayerId
                ? player with { RoleId = swap.LocalOriginalRole, IsImpostor = false }
                : player).ToArray();
}

static void AssertConserved(IReadOnlyList<RoleSlot> before, IReadOnlyList<RoleSlot> after)
{
    Require(before.Select(player => player.RoleId).Order().SequenceEqual(after.Select(player => player.RoleId).Order()),
        "The complete role multiset changed.");
    Require(before.Count(player => player.IsImpostor) == after.Count(player => player.IsImpostor),
        "The impostor count changed.");
    Require(before.Count(player => !player.IsImpostor) == after.Count(player => !player.IsImpostor),
        "The crew count changed.");
    Require(before.Select(player => (player.PlayerId, player.Alive, player.Connected))
        .SequenceEqual(after.Select(player => (player.PlayerId, player.Alive, player.Connected))),
        "Player identity or availability changed.");
}

static void SingleImpostorSwap()
{
    var players = DefaultPlayers();
    var swap = Plan(players);
    Require(swap == new Swap(1, 0, 2, 1), "Wrong single-impostor swap.");
    var after = Apply(players, swap);
    Require(after.Single(player => player.PlayerId == 1).IsImpostor, "Local player did not become an impostor.");
    Require(!after.Single(player => player.PlayerId == 2).IsImpostor, "Donor did not become crew.");
    AssertConserved(players, after);
}

static void MultipleImpostorSwap()
{
    RoleSlot[] players = [Crew(1, 12), Impostor(9, 18), Impostor(3, 5), Crew(7), Impostor(6, 16)];
    var swap = Plan(players);
    Require(swap == new Swap(1, 12, 3, 5), "The lowest eligible donor ID was not selected.");
    var after = Apply(players, swap);
    Require(after.Single(player => player.PlayerId == 9) == players[1], "Unselected impostor changed.");
    Require(after.Single(player => player.PlayerId == 6) == players[4], "Unselected special impostor changed.");
    AssertConserved(players, after);
    Require(Plan(players.Reverse().ToArray()) == swap, "Donor choice depends on list order.");
}

static void SkipUnavailableDonors()
{
    RoleSlot[] players = [Crew(1), Impostor(2) with { Alive = false }, Impostor(3) with { Connected = false }, Impostor(4, 18)];
    var swap = Plan(players);
    Require(swap.DonorPlayerId == 4 && swap.DonorOriginalRole == 18, "Unavailable donor was selected.");
    AssertConserved(players, Apply(players, swap));
}

static void SpecialRoleConservation()
{
    RoleSlot[] players = [Crew(1, 12), Impostor(2, 18), Crew(3, 3), Crew(4, 7), Impostor(5, 16)];
    var swap = Plan(players);
    var after = Apply(players, swap);
    Require(after[0].RoleId == 18 && after[1].RoleId == 12, "Special roles were reduced to basic roles.");
    Require(after.Skip(2).SequenceEqual(players.Skip(2)), "Unrelated role assignments changed.");
    AssertConserved(players, after);
}

static void RepeatedApplication()
{
    var players = DefaultPlayers();
    var after = Apply(players, Plan(players));
    ExpectNoPlan(after);
    AssertConserved(players, after);
}

static void DoesNotMutateSnapshot()
{
    var players = DefaultPlayers();
    var original = players.ToArray();
    _ = Plan(players);
    Require(players.SequenceEqual(original), "Planner mutated the supplied role snapshot.");
}

static void PlayerIdZero()
{
    RoleSlot[] players = [Crew(0, 12), Impostor(1, 18)];
    Require(Plan(players, 0) == new Swap(0, 12, 1, 18), "Player ID zero was treated as missing.");
}

static void AllRolePairConservation()
{
    ushort[] crewRoles = [0, 2, 3, 7, 12, 17, ushort.MaxValue];
    ushort[] impostorRoles = [1, 5, 6, 16, 18, ushort.MaxValue];
    foreach (var crewRole in crewRoles)
    foreach (var impostorRole in impostorRoles)
    {
        RoleSlot[] players = [Crew(1, crewRole), Impostor(2, impostorRole), Crew(3, 4), Impostor(4, 19)];
        var after = Apply(players, Plan(players));
        Require(after[0].RoleId == impostorRole && after[1].RoleId == crewRole, "An exact role ID was lost.");
        AssertConserved(players, after);
        ExpectNoPlan(after);
    }
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
