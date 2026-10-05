using System;
using System.Collections.Generic;
using System.Text;
using AmongUs.GameOptions;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using InnerNet;

namespace JudgeTrace;

[BepInPlugin(PluginId, "Judge Lifecycle Trace", "0.1.0")]
[BepInProcess("Among Us.exe")]
public sealed class JudgeTracePlugin : BasePlugin
{
    public const string PluginId = "local.diagnostics.judge-trace";
    public override void Load()
    {
        try
        {
            Trace.Initialize(Log, Config.Bind("Diagnostics", "Enabled", true,
                "Observe local Judge lifecycle without changing gameplay. Disable to stop all trace logging."));
            new Harmony(PluginId).PatchAll(typeof(JudgeTracePlugin).Assembly);
            Trace.Note("loaded", "version=0.1.0 mode=passive typed_commands_are_not_acceptance=true");
        }
        catch (Exception error)
        {
            Trace.Error("patch_install", error);
        }
    }
}

internal static class Trace
{
    private static ManualLogSource _log;
    private static ConfigEntry<bool> _enabled;
    private static long _sequence;
    private static int _round;
    private static int _meetingSequence;
    private static MeetingHud _meeting;
    private static IntPtr _assignedPlayer;
    private static IntPtr _assignedRole;
    private static RoleTypes? _assignedRoleType;
    private static readonly Dictionary<(uint Meeting, ushort Nonce), int> Commands = new();

    public static void Initialize(ManualLogSource log, ConfigEntry<bool> enabled)
    {
        _log = log;
        _enabled = enabled;
    }

    private static bool Enabled => _enabled != null && _enabled.Value;

    public static void Safe(string operation, Action action)
    {
        try { if (Enabled) action(); }
        catch (Exception error) { Error(operation, error); }
    }

    public static void Error(string operation, Exception error)
    {
        // Error messages/stacks may contain user strings, so keep only type.
        try { if (Enabled) _log.LogWarning("JudgeTrace event=diagnostic_error operation=" + operation + " exception_type=" + error.GetType().Name); }
        catch { }
    }

    public static void Note(string operation, string fields)
    {
        Safe(operation, () => _log.LogInfo("JudgeTrace seq=" + (++_sequence) + " event=" + operation + " " + fields));
    }

    public static void Joined()
    {
        Safe("joined", () =>
        {
            _meeting = null;
            _assignedPlayer = IntPtr.Zero;
            _assignedRole = IntPtr.Zero;
            _assignedRoleType = null;
            Commands.Clear();
            Note("joined", "lobby_identity_omitted=true");
        });
    }

    public static void RoundStart()
    {
        Safe("round_start", () =>
        {
            _round++;
            _meetingSequence = 0;
            _meeting = null;
            Commands.Clear();
            _assignedPlayer = IntPtr.Zero;
            _assignedRole = IntPtr.Zero;
            _assignedRoleType = null;
            var local = PlayerControl.LocalPlayer;
            if (local && local.AmOwner && local.Data != null && local.Data.Role)
            {
                _assignedPlayer = local.Pointer;
                _assignedRole = local.Data.Role.Pointer;
                _assignedRoleType = local.Data.Role.Role;
            }
            Snapshot("round_start", "before_native_intro", null, null, "role_evidence=observed_at_intro");
        });
    }

    public static void RoundEnd(string phase)
    {
        Snapshot("round_end", phase, null, _meeting, "");
        if (phase == "after") Joined();
    }

    public static void MeetingStart(MeetingHud meeting)
    {
        Safe("meeting_start", () =>
        {
            _meeting = meeting;
            _meetingSequence++;
            Commands.Clear();
            Snapshot("meeting_start", "after_awake", null, meeting, "");
        });
    }

    public static void MeetingClose(MeetingHud meeting, string phase)
    {
        Snapshot("meeting_close", phase, null, meeting, "");
        if (phase == "after") Safe("meeting_close", () => { if (_meeting && meeting && _meeting.Pointer == meeting.Pointer) _meeting = null; });
    }

    private static JudgeRole CurrentJudge(JudgeRole observed)
    {
        var local = PlayerControl.LocalPlayer;
        if (!local || !local.AmOwner || local.Data == null || !local.Data.Role ||
            local.Data.Role.Role != RoleTypes.Judge)
            return null; // Never cast a Crewmate/fake Impostor as Judge.
        var role = local.Data.Role.TryCast<JudgeRole>();
        if (!role || !role.Player || role.Player.Pointer != local.Pointer || !role.Player.AmOwner)
            return null;
        if (observed && observed.Pointer != role.Pointer) return null;
        // Network snapshots follow only the role observed before this mod's
        // intro-time role edits. Practice has no normal round-intro assignment.
        var client = AmongUsClient.Instance;
        bool practice = client && client.NetworkMode == NetworkModes.FreePlay;
        if (!practice && (_assignedRoleType != RoleTypes.Judge ||
            _assignedPlayer != local.Pointer || _assignedRole != role.Pointer))
            return null;
        return role;
    }

    public static void Snapshot(string eventName, string phase, JudgeRole observed,
        MeetingHud meeting, string typedFields)
    {
        Safe(eventName, () =>
        {
            var local = PlayerControl.LocalPlayer;
            var client = AmongUsClient.Instance;
            var currentMeeting = meeting ? meeting : _meeting;
            var role = CurrentJudge(observed);
            // Role-specific callbacks for another actor are excluded entirely.
            if (observed && !role) return;
            var line = new StringBuilder("JudgeTrace");
            line.Append(" seq=").Append(++_sequence)
                .Append(" round=").Append(_round)
                .Append(" meeting_sequence=").Append(_meetingSequence)
                .Append(" event=").Append(eventName).Append(" phase=").Append(phase)
                .Append(" is_freeplay=").Append(client && client.NetworkMode == NetworkModes.FreePlay)
                .Append(" is_host=").Append(client && client.AmHost)
                .Append(" local_player_id=").Append(local ? ((byte)local.PlayerId).ToString() : "none")
                .Append(" local_instance=").Append(local ? local.Pointer.ToString("X") : "none")
                .Append(" assigned_role=").Append(_assignedRoleType?.ToString() ?? "unknown")
                .Append(" assigned_role_instance=").Append(_assignedRole.ToString("X"))
                .Append(" meeting_net_id=").Append(currentMeeting ? currentMeeting.NetId.ToString() : "none")
                .Append(" meeting_owner_id=").Append(currentMeeting ? currentMeeting.OwnerId.ToString() : "none")
                .Append(" meeting_instance=").Append(currentMeeting ? currentMeeting.Pointer.ToString("X") : "none")
                .Append(" current_judge_in_scope=").Append((bool)role);
            if (role)
            {
                line.Append(" role_instance=").Append(role.Pointer.ToString("X"))
                    .Append(" has_use=").Append(role.HasAnOverruleUse)
                    .Append(" used_this_meeting=").Append(role.HasAlreadyOverruledThisMeeting)
                    .Append(" target_player_id=").Append((byte)role.OverruledPlayerId)
                    .Append(" current_request_nonce=").Append(role.OverruleNonce);
                var tasks = local.Data.Tasks;
                if (tasks != null)
                {
                    int complete = 0;
                    foreach (var task in tasks) if (task != null && task.Complete) complete++;
                    line.Append(" tasks_done=").Append(complete).Append(" tasks_total=").Append(tasks.Count);
                }
            }
            if (!string.IsNullOrEmpty(typedFields)) line.Append(' ').Append(typedFields);
            _log.LogInfo(line.ToString());
        });
    }

    public static void Command(MeetingHud meeting, PlayerId judge, PlayerId target,
        ushort nonce, string phase)
    {
        Safe("cmd_queue_overrule", () =>
        {
            var role = CurrentJudge(null);
            var local = PlayerControl.LocalPlayer;
            if (!role || !local || (byte)judge != (byte)local.PlayerId) return;
            var key = (meeting.NetId, nonce);
            Commands.TryGetValue(key, out int count);
            if (phase == "before") { count++; Commands[key] = count; }
            Snapshot("cmd_queue_overrule", phase, role, meeting,
                "cmd_judge_id=" + (byte)judge + " cmd_target_id=" + (byte)target +
                " request_nonce=" + nonce + " typed_cmd_entries_for_nonce=" + count +
                " evidence=typed_command_callback_only");
        });
    }

    public static void Voting(MeetingHud meeting, NetworkedPlayerInfo exiled,
        bool tie, bool wasOverruled, ushort nonce, string phase)
    {
        Safe("voting_complete", () => Snapshot("voting_complete", phase, null, meeting,
            "result_nonce=" + nonce + " result_was_overruled=" + wasOverruled +
            " result_tie=" + tie + " exiled_player_id=" + (exiled != null ? ((byte)exiled.PlayerId).ToString() : "none") +
            " evidence=native_callback_only"));
    }
}

[HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.CoBegin))]
internal static class IntroTrace
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix() => Trace.RoundStart();
}

[HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameJoined))]
internal static class JoinTrace { private static void Postfix() => Trace.Joined(); }

[HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameEnd))]
internal static class EndTrace
{
    [HarmonyPriority(Priority.First)] private static void Prefix() => Trace.RoundEnd("before");
    [HarmonyPriority(Priority.Last)] private static void Postfix() => Trace.RoundEnd("after");
}

[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Awake))]
internal static class MeetingStartTrace { private static void Postfix(MeetingHud __instance) => Trace.MeetingStart(__instance); }

[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Close))]
internal static class MeetingCloseTrace
{
    [HarmonyPriority(Priority.First)] private static void Prefix(MeetingHud __instance) => Trace.MeetingClose(__instance, "before");
    [HarmonyPriority(Priority.Last)] private static void Postfix(MeetingHud __instance) => Trace.MeetingClose(__instance, "after");
}

[HarmonyPatch(typeof(JudgeRole), nameof(JudgeRole.OnMeetingStart))]
internal static class JudgeMeetingTrace
{
    [HarmonyPriority(Priority.First)] private static void Prefix(JudgeRole __instance) => Trace.Snapshot("judge_meeting_start", "before", __instance, null, "");
    [HarmonyPriority(Priority.Last)] private static void Postfix(JudgeRole __instance) => Trace.Snapshot("judge_meeting_start", "after", __instance, null, "");
}

[HarmonyPatch(typeof(JudgeRole), nameof(JudgeRole.TryOverrule), new[] { typeof(PlayerId) })]
internal static class TryTrace
{
    [HarmonyPriority(Priority.First)] private static void Prefix(JudgeRole __instance, PlayerId __0) => Trace.Safe("try_overrule_before", () => Trace.Snapshot("try_overrule", "before", __instance, null, "requested_target_id=" + (byte)__0));
    [HarmonyPriority(Priority.Last)] private static void Postfix(JudgeRole __instance, PlayerId __0, bool __result) => Trace.Safe("try_overrule_after", () => Trace.Snapshot("try_overrule", "after", __instance, null, "requested_target_id=" + (byte)__0 + " local_return=" + __result + " evidence=local_return_only"));
}

[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.CmdQueueOverruleVotes), new[] { typeof(PlayerId), typeof(PlayerId), typeof(ushort) })]
internal static class CommandTrace
{
    [HarmonyPriority(Priority.First)] private static void Prefix(MeetingHud __instance, PlayerId __0, PlayerId __1, ushort __2) => Trace.Command(__instance, __0, __1, __2, "before");
    [HarmonyPriority(Priority.Last)] private static void Postfix(MeetingHud __instance, PlayerId __0, PlayerId __1, ushort __2) => Trace.Command(__instance, __0, __1, __2, "after");
}

[HarmonyPatch(typeof(JudgeRole), nameof(JudgeRole.ClearOverrule))]
internal static class ClearTrace
{
    [HarmonyPriority(Priority.First)] private static void Prefix(JudgeRole __instance) => Trace.Snapshot("clear_overrule", "before", __instance, null, "");
    [HarmonyPriority(Priority.Last)] private static void Postfix(JudgeRole __instance) => Trace.Snapshot("clear_overrule", "after", __instance, null, "");
}

[HarmonyPatch(typeof(JudgeRole), nameof(JudgeRole.ConsumeOverruleVotesUsage))]
internal static class ConsumeTrace
{
    [HarmonyPriority(Priority.First)] private static void Prefix(JudgeRole __instance) => Trace.Snapshot("consume_overrule_use", "before", __instance, null, "");
    [HarmonyPriority(Priority.Last)] private static void Postfix(JudgeRole __instance) => Trace.Snapshot("consume_overrule_use", "after", __instance, null, "");
}

[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.VotingComplete))]
internal static class VotingTrace
{
    [HarmonyPriority(Priority.First)] private static void Prefix(MeetingHud __instance, NetworkedPlayerInfo __1, bool __2, bool __3, ushort __4) => Trace.Voting(__instance, __1, __2, __3, __4, "before");
    [HarmonyPriority(Priority.Last)] private static void Postfix(MeetingHud __instance, NetworkedPlayerInfo __1, bool __2, bool __3, ushort __4) => Trace.Voting(__instance, __1, __2, __3, __4, "after");
}
