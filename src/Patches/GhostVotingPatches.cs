using System;
using HarmonyLib;
using InnerNet;

namespace MalumMenu;

// The host must enable the same mode to count ghost votes. No death state is
// serialized or changed outside a single synchronous vote-selection call.
internal static class GhostVoting
{
    [ThreadStatic] private static IntPtr _scopedLocalGhost;
    private static PlayerVoteArea _visibleSkipButton;
    private static bool _skipButtonWasActive;

    private static bool IsEnabledMeeting(MeetingHud meeting) =>
        CheatToggles.ghostVoting && (Utils.isInGame || (Utils.isFreePlay && ShipStatus.Instance)) && meeting &&
        MeetingHud.Instance && meeting.Pointer == MeetingHud.Instance.Pointer &&
        GameOptionsManager.Instance != null &&
        GameOptionsManager.Instance.CurrentGameOptions != null && Utils.isNormalGame;

    private static bool IsVoting(MeetingHud meeting) =>
        meeting.state is MeetingHud.MeetingStates.NotVoted or MeetingHud.MeetingStates.Voted;

    internal static bool IsGhost(NetworkedPlayerInfo data) =>
        data != null && (data.IsDead ||
        (_scopedLocalGhost != IntPtr.Zero && data.Pointer == _scopedLocalGhost));

    private static PlayerVoteArea FindArea(MeetingHud meeting, byte playerId)
    {
        if (meeting.playerStates == null) return null;
        foreach (var area in meeting.playerStates)
            if (area && area.PlayerId.Value == playerId) return area;
        return null;
    }

    internal static void BeginLocalSelection(MeetingHud meeting, out LocalDeadScope scope)
    {
        scope = null;
        if (!IsEnabledMeeting(meeting) || meeting.state != MeetingHud.MeetingStates.NotVoted)
            return;

        var local = PlayerControl.LocalPlayer;
        if (!local || local.Data == null || local.Data.Disconnected || !local.Data.IsDead)
            return;
        var area = FindArea(meeting, local.PlayerId);
        if (!area || area.DidVote) return;

        // Assign the Harmony state before changing the flag, so its finalizer
        // can restore it even if a later native call throws. Nested Select calls
        // inherit this scope and leave the original target's AmDead untouched.
        scope = new LocalDeadScope(local.Data, _scopedLocalGhost);
        _scopedLocalGhost = local.Data.Pointer;
        local.Data.IsDead = false;
    }

    internal static void BeginHostVote(MeetingHud meeting, PlayerId sourceId,
        PlayerId targetId, out VoteAreaDeadScope scope)
    {
        scope = null;
        if (!IsEnabledMeeting(meeting) || !Utils.isHost || !IsVoting(meeting) ||
            GameData.Instance == null)
            return;

        var source = GameData.Instance.GetPlayerById(sourceId.Value);
        var area = FindArea(meeting, sourceId.Value);
        if (source == null || source.PlayerId != sourceId.Value || source.Disconnected ||
            !IsGhost(source) || !area || !area.AmDead || area.DidVote)
            return;

        if (targetId.Value != PlayerVoteArea.SkippedVote)
        {
            var target = GameData.Instance.GetPlayerById(targetId.Value);
            var targetArea = FindArea(meeting, targetId.Value);
            if (target == null || target.Disconnected || IsGhost(target) ||
                !targetArea || targetArea.AmDead)
                return;
        }

        // Run the original CastVote with its duplicate-vote, dirty-bit, tally,
        // meeting-completion and Judge behavior. Only its source-death gate is
        // opened temporarily; no RPC, other player's vote or role is fabricated.
        scope = new VoteAreaDeadScope(area);
        area.AmDead = false;
    }

    internal static void UpdateSkipButton(MeetingHud meeting)
    {
        var local = PlayerControl.LocalPlayer;
        var shouldShow = IsEnabledMeeting(meeting) &&
            meeting.state == MeetingHud.MeetingStates.NotVoted && local &&
            local.Data != null && !local.Data.Disconnected && IsGhost(local.Data);
        var skip = meeting ? meeting.SkipVoteButton : null;

        if (_visibleSkipButton && (!shouldShow || !skip ||
            _visibleSkipButton.Pointer != skip.Pointer))
            RestoreSkipButton();

        if (!shouldShow || !skip) return;
        if (!_visibleSkipButton)
        {
            _visibleSkipButton = skip;
            _skipButtonWasActive = skip.gameObject.activeSelf;
        }
        skip.gameObject.SetActive(true);
    }

    internal static void RestoreSkipButton()
    {
        if (_visibleSkipButton)
            _visibleSkipButton.gameObject.SetActive(_skipButtonWasActive);
        _visibleSkipButton = null;
    }

    internal sealed class LocalDeadScope
    {
        private readonly NetworkedPlayerInfo _data;
        private readonly IntPtr _previousLocalGhost;

        internal LocalDeadScope(NetworkedPlayerInfo data, IntPtr previousLocalGhost)
        {
            _data = data;
            _previousLocalGhost = previousLocalGhost;
        }

        internal void Restore()
        {
            try { if (_data != null) _data.IsDead = true; }
            finally { _scopedLocalGhost = _previousLocalGhost; }
        }
    }

    internal sealed class VoteAreaDeadScope
    {
        private readonly PlayerVoteArea _area;
        internal VoteAreaDeadScope(PlayerVoteArea area) => _area = area;
        internal void Restore() { if (_area) _area.AmDead = true; }
    }
}

[HarmonyPatch(typeof(PlayerVoteArea), nameof(PlayerVoteArea.Select))]
internal static class GhostVoting_PlayerSelect
{
    public static void Prefix(PlayerVoteArea __instance, out GhostVoting.LocalDeadScope __state) =>
        GhostVoting.BeginLocalSelection(__instance.Parent, out __state);
    public static void Finalizer(GhostVoting.LocalDeadScope __state) => __state?.Restore();
}

[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Select))]
internal static class GhostVoting_MeetingSelect
{
    public static void Prefix(MeetingHud __instance, out GhostVoting.LocalDeadScope __state) =>
        GhostVoting.BeginLocalSelection(__instance, out __state);
    public static void Finalizer(GhostVoting.LocalDeadScope __state) => __state?.Restore();
}

[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Confirm))]
internal static class GhostVoting_Confirm
{
    public static void Prefix(MeetingHud __instance, out GhostVoting.LocalDeadScope __state) =>
        GhostVoting.BeginLocalSelection(__instance, out __state);
    public static void Finalizer(GhostVoting.LocalDeadScope __state) => __state?.Restore();
}

[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.CastVote))]
internal static class GhostVoting_HostCastVote
{
    public static void Prefix(MeetingHud __instance,
        [HarmonyArgument(0)] PlayerId sourceId, [HarmonyArgument(1)] PlayerId targetId,
        out GhostVoting.VoteAreaDeadScope __state) =>
        GhostVoting.BeginHostVote(__instance, sourceId, targetId, out __state);
    public static void Finalizer(GhostVoting.VoteAreaDeadScope __state) => __state?.Restore();
}

[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Update))]
internal static class GhostVoting_MeetingUpdate
{
    public static void Postfix(MeetingHud __instance) => GhostVoting.UpdateSkipButton(__instance);
}

[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.OnDestroy))]
internal static class GhostVoting_MeetingDestroy
{
    public static void Prefix() => GhostVoting.RestoreSkipButton();
}
