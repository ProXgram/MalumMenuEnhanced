using MalumMenu;
using Context = MalumMenu.GuestRoundTestPolicy.Context;

var valid = new Context(
    Online: true,
    Started: true,
    NonHost: true,
    Normal: true,
    OwnPlayer: true,
    Alive: true,
    Connected: true,
    CanMove: true,
    IntroClosed: true,
    NoMeeting: true,
    NoExile: true,
    BoundRoundMatches: true);

var tests = new (string Name, Action Run)[]
{
    ("Valid active guest is accepted without a visibility prerequisite", () => ExpectAllowed(valid)),
    ("Default or unavailable context is rejected", () => ExpectRejected(default)),
    ("Offline context is rejected", () => ExpectRejected(valid with { Online = false })),
    ("Inactive lobby context is rejected", () => ExpectRejected(valid with { Started = false })),
    ("Host context is rejected", () => ExpectRejected(valid with { NonHost = false })),
    ("Hide-and-seek context is rejected", () => ExpectRejected(valid with { Normal = false })),
    ("Foreign or missing player ownership is rejected", () => ExpectRejected(valid with { OwnPlayer = false })),
    ("Dead player is rejected", () => ExpectRejected(valid with { Alive = false })),
    ("Disconnected player is rejected", () => ExpectRejected(valid with { Connected = false })),
    ("Player unable to move is rejected", () => ExpectRejected(valid with { CanMove = false })),
    ("Open intro context is rejected", () => ExpectRejected(valid with { IntroClosed = false })),
    ("Active meeting is rejected", () => ExpectRejected(valid with { NoMeeting = false })),
    ("Active exile is rejected", () => ExpectRejected(valid with { NoExile = false })),
    ("Changed round identity is rejected", () => ExpectRejected(valid with { BoundRoundMatches = false })),
    ("Unbound round is rejected", () => ExpectRejected(valid with { BoundRoundMatches = false })),
    ("Changed lobby visibility invalidates a prior binding", () => ExpectRejected(valid with { BoundRoundMatches = false })),
    ("Multiple incomplete conditions cannot bypass the policy", () => ExpectRejected(valid with { NonHost = false, Alive = false, NoMeeting = false })),
    ("Policy does not change its input snapshot", () => SnapshotImmutability(valid)),
    ("All 4096 context combinations follow the required gates", AllContextCombinations),
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

static void ExpectAllowed(Context context)
{
    Require(GuestRoundTestPolicy.CanAct(context), "Expected the active guest context to be accepted.");
}

static void ExpectRejected(Context context)
{
    Require(!GuestRoundTestPolicy.CanAct(context), "An incomplete context was accepted.");
}

static void SnapshotImmutability(Context context)
{
    var before = context;
    ExpectAllowed(context);
    Require(context == before, "Policy changed its input context.");
    ExpectRejected(context with { BoundRoundMatches = false });
    Require(context == before, "A rejected snapshot changed the original context.");
}

static void AllContextCombinations()
{
    const int flagCount = 12;
    const int allRequiredFlags = (1 << flagCount) - 1;
    var acceptedCount = 0;
    for (var flags = 0; flags <= allRequiredFlags; flags++)
    {
        var context = new Context(
            Online: (flags & (1 << 0)) != 0,
            Started: (flags & (1 << 1)) != 0,
            NonHost: (flags & (1 << 2)) != 0,
            Normal: (flags & (1 << 3)) != 0,
            OwnPlayer: (flags & (1 << 4)) != 0,
            Alive: (flags & (1 << 5)) != 0,
            Connected: (flags & (1 << 6)) != 0,
            CanMove: (flags & (1 << 7)) != 0,
            IntroClosed: (flags & (1 << 8)) != 0,
            NoMeeting: (flags & (1 << 9)) != 0,
            NoExile: (flags & (1 << 10)) != 0,
            BoundRoundMatches: (flags & (1 << 11)) != 0);
        var accepted = GuestRoundTestPolicy.CanAct(context);
        Require(accepted == (flags == allRequiredFlags), $"Incorrect decision for context flags {flags}.");
        if (accepted)
            acceptedCount++;
    }
    Require(acceptedCount == 1, "The policy must accept exactly one complete flag combination.");
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
