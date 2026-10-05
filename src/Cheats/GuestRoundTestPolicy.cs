#if GUEST_KILL_EXPERIMENT
namespace MalumMenu;

internal static class GuestRoundTestPolicy
{
    public readonly record struct Context(
        bool Online,
        bool Started,
        bool NonHost,
        bool Normal,
        bool OwnPlayer,
        bool Alive,
        bool Connected,
        bool CanMove,
        bool IntroClosed,
        bool NoMeeting,
        bool NoExile,
        bool BoundRoundMatches);

    public static bool CanAct(Context c) =>
        c.Online && c.Started && c.NonHost && c.Normal &&
        c.OwnPlayer && c.Alive && c.Connected && c.CanMove && c.IntroClosed &&
        c.NoMeeting && c.NoExile && c.BoundRoundMatches;
}
#endif
