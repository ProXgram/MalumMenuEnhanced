using HarmonyLib;

namespace MalumMenu;

[HarmonyPatch(typeof(JudgeRole), nameof(JudgeRole.OnMeetingStart))]
public static class JudgeRole_OnMeetingStart_InfiniteJudge
{
    public static void Prefix(JudgeRole __instance) =>
        InfiniteJudgeHandler.RefillForMeeting(__instance);
}

[HarmonyPatch(typeof(JudgeRole), nameof(JudgeRole.ConsumeOverruleVotesUsage))]
public static class JudgeRole_ConsumeOverruleVotesUsage_InfiniteJudge
{
    public static void Postfix(JudgeRole __instance) =>
        InfiniteJudgeHandler.OnUseConsumed(__instance);
}
