using HarmonyLib;

namespace MalumMenu;

[HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.FixedUpdate))]
public static class PlayerPhysics_FixedUpdate_MovementAutomation
{
    public static void Postfix(PlayerPhysics __instance)
    {
        MovementAutomation.FixedTick(__instance);
        NicknameEntry.SuppressMovement(__instance);
    }
}
