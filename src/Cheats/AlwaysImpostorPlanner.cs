using System.Collections.Generic;

namespace MalumMenu;

internal static class AlwaysImpostorPlanner
{
    public readonly record struct RoleSlot(
        byte PlayerId,
        ushort RoleId,
        bool IsImpostor,
        bool Alive,
        bool Connected);

    public readonly record struct Swap(
        byte LocalPlayerId,
        ushort LocalOriginalRole,
        byte DonorPlayerId,
        ushort DonorOriginalRole);

    public static bool TryPlan(
        bool enabled,
        bool isHost,
        bool normalMode,
        byte localPlayerId,
        IReadOnlyList<RoleSlot> players,
        out Swap swap)
    {
        swap = default;
        if (!enabled || !isHost || !normalMode || players == null)
            return false;

        var playerIds = new HashSet<byte>();
        RoleSlot local = default;
        RoleSlot donor = default;
        var foundLocal = false;
        var foundDonor = false;

        for (var i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (!playerIds.Add(player.PlayerId))
                return false;

            if (player.PlayerId == localPlayerId)
            {
                local = player;
                foundLocal = true;
            }
            else if (player.IsImpostor && player.Alive && player.Connected &&
                     (!foundDonor || player.PlayerId < donor.PlayerId))
            {
                donor = player;
                foundDonor = true;
            }
        }

        if (!foundLocal || !local.Alive || !local.Connected || local.IsImpostor || !foundDonor)
            return false;

        swap = new Swap(local.PlayerId, local.RoleId, donor.PlayerId, donor.RoleId);
        return true;
    }
}
