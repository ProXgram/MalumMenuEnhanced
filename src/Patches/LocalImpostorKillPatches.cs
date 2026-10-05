using HarmonyLib;

namespace MalumMenu;

[HarmonyPatch(typeof(KillButton), nameof(KillButton.DoClick))]
public static class KillButton_DoClick
{
    public static bool Prefix() =>
        !LocalImpostorHandler.BlockOwnKill(PlayerControl.LocalPlayer);
}

// Guard outgoing decisions only. Incoming MurderPlayer must still apply the
// authority's accepted deaths, and Freeplay keeps its normal local behavior.
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RpcMurderPlayer))]
public static class PlayerControl_RpcMurderPlayer
{
    public static bool Prefix(PlayerControl __instance) =>
        !LocalImpostorHandler.BlockOwnKill(__instance);
}
