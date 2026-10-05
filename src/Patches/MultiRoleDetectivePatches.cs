using System;
using HarmonyLib;

namespace MalumMenu;

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.MurderPlayer))]
public static class MultiRoleDetectiveDeathCapture
{
    [HarmonyPriority(Priority.First)]
    public static void Prefix(PlayerControl target, MurderResultFlags resultFlags,
        out MultiRoleDetectiveHandler.PendingDeath __state)
    {
        __state = null;
        try { __state = MultiRoleDetectiveHandler.CapturePendingDeath(target, resultFlags); }
        catch (Exception error) { MalumMenu.Log?.LogWarning("Detective snapshot failed: " + error.Message); }
    }

    public static void Postfix(PlayerControl target, MultiRoleDetectiveHandler.PendingDeath __state)
    {
        try { MultiRoleDetectiveHandler.ConfirmDeath(__state, target); }
        catch (Exception error) { MalumMenu.Log?.LogWarning("Detective death confirmation failed: " + error.Message); }
    }
}

[HarmonyPatch(typeof(DetectiveRole), nameof(DetectiveRole.SetAbilityInfo))]
public static class MultiRoleDetectiveKeepNativeButtons
{
    public static bool Prefix(DetectiveRole __instance) => !MultiRoleDetectiveHandler.Owns(__instance);
}

[HarmonyPatch(typeof(DetectiveRole), nameof(DetectiveRole.GetPlayerLocation))]
public static class MultiRoleDetectiveOwnLocations
{
    public static bool Prefix(DetectiveRole __instance, byte playerID, byte victimID,
        ref string __result)
    {
        if (!MultiRoleDetectiveHandler.Owns(__instance)) return true;
        __result = MultiRoleDetectiveHandler.GetCapturedLocation(victimID, playerID);
        return false;
    }
}

[HarmonyPatch(typeof(DetectiveNotesMinigame), nameof(DetectiveNotesMinigame.SetUpCurrentPage))]
public static class MultiRoleDetectiveSafePage
{
    public static void Postfix(DetectiveNotesMinigame __instance)
    {
        try
        {
            MultiRoleDetectiveHandler.RenderPage(__instance);
        }
        catch (Exception error) { MalumMenu.Log?.LogWarning("Detective page display failed: " + error.Message); }
    }

}

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CanMove), MethodType.Getter)]
public static class MultiRoleDetectiveMapRenderGuard
{
    public static bool Prefix(PlayerControl __instance, ref bool __result)
    {
        if (!MultiRoleDetectiveHandler.AllowOwnedMapRender(__instance)) return true;
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(DetectiveNotesMinigame), nameof(DetectiveNotesMinigame.OpenVictimLocationMap))]
public static class MultiRoleDetectiveSafeMap
{
    public static bool Prefix(DetectiveNotesMinigame __instance)
    {
        if (!MultiRoleDetectiveHandler.Owns(__instance)) return true;
        try { MultiRoleDetectiveHandler.OpenCaseMap(__instance); }
        catch (Exception error) { MalumMenu.Log?.LogWarning("Detective map failed: " + error.Message); }
        return false;
    }
}

[HarmonyPatch(typeof(DetectiveNotesMinigame), nameof(DetectiveNotesMinigame.Close), new Type[0])]
public static class MultiRoleDetectiveOwnedClose
{
    public static void Prefix(DetectiveNotesMinigame __instance, out bool __state) =>
        __state = MultiRoleDetectiveHandler.BeginNotesClose(__instance);

    public static Exception Finalizer(Exception __exception, bool __state)
    {
        if (__state) MultiRoleDetectiveHandler.EndNotesClose();
        return __exception;
    }
}

[HarmonyPatch(typeof(MapBehaviour), nameof(MapBehaviour.Close))]
public static class MultiRoleDetectiveKeepOtherMaps
{
    public static bool Prefix(MapBehaviour __instance) => MultiRoleDetectiveHandler.MayCloseMap(__instance);

    public static void Postfix(MapBehaviour __instance, bool __runOriginal)
    {
        if (!__runOriginal) return;
        MultiRoleDetectiveHandler.OnCaseMapClosed(__instance);
    }
}

[HarmonyPatch(typeof(MapBehaviour), nameof(MapBehaviour.FixedUpdate))]
public static class MultiRoleDetectiveVictimMarker
{
    public static void Postfix(MapBehaviour __instance)
    {
        try { MultiRoleDetectiveHandler.UpdateCaseMap(__instance); }
        catch (Exception error) { MalumMenu.Log?.LogWarning("Detective victim marker failed: " + error.Message); }
    }
}
