using System.Collections.Generic;

namespace MalumMenu;
public static class OverloadHandler
{
    public static float cooldown;
    public static int strength;
    private static HashSet<int> _customTargets = new();

    // Compatibility entry point: the overload runner is permanently disabled.
    public static void Run()
    {
    }

    public static void AddCustomTarget(NetworkedPlayerInfo playerData)
    {
        int clientId = playerData.ClientId;
        _customTargets.Add(clientId);
    }

    public static void RemoveCustomTarget(NetworkedPlayerInfo playerData)
    {
        int clientId = playerData.ClientId;
        _customTargets.Remove(clientId);
    }

    public static bool IsCustomTarget(NetworkedPlayerInfo playerData)
    {
        return _customTargets.Contains(playerData.ClientId);
    }

    public static (HashSet<TargetType> targetTypes, bool isTarget) GetTarget(NetworkedPlayerInfo playerData)
    {
        bool isTarget = false;
        var targetTypes = new HashSet<TargetType>();

        if (CheatToggles.overloadAll)
        {
            targetTypes.Add(TargetType.All);
            isTarget = true;
        }

        bool hostTarget = CheatToggles.overloadHost && AmongUsClient.Instance.HostId == playerData.ClientId;
        if (hostTarget)
        {
            targetTypes.Add(TargetType.Host);
            isTarget = true;
        }

        if (playerData.Role != null)
        {
            RoleTeamTypes roleTeamType = playerData.Role.TeamType;

            bool crewTarget = CheatToggles.overloadCrew && roleTeamType.Equals(RoleTeamTypes.Crewmate);
            if (crewTarget)
            {
                targetTypes.Add(TargetType.Crewmate);
                isTarget = true;
            }

            bool impTarget = CheatToggles.overloadImps && roleTeamType.Equals(RoleTeamTypes.Impostor);
            if (impTarget)
            {
                targetTypes.Add(TargetType.Impostor);
                isTarget = true;
            }
        }

        bool customTarget = IsCustomTarget(playerData);
        if (customTarget)
        {
            targetTypes.Add(TargetType.Custom);
            isTarget = true;
        }

        if (!isTarget)
        {
            targetTypes.Add(TargetType.None);
        }

        return (targetTypes, isTarget);
    }

    public static void ClearCustomTargets()
    {
        _customTargets.Clear();
    }

    // Iterates through all given players and
    // adds all that are marked as targets and match given targetType to _customTargets
    public static void PopulateCustomTargets(PlayerControl[] players, TargetType targetType)
    {
        int playerCount = players.Length;

        for (int i = 0; i < playerCount; i++)
        {
            NetworkedPlayerInfo playerData = players[i].Data;
            var playerTarget = GetTarget(playerData);
            bool isTarget = playerTarget.isTarget;

            if (isTarget && !IsCustomTarget(playerData))
            {
                HashSet<TargetType> currentTargetTypes = playerTarget.targetTypes;
                if (currentTargetTypes.Contains(targetType))
                {
                    AddCustomTarget(playerData);
                }
            }
        }
    }

    // No active overload settings are available in this compatibility fork.
    public static (int strength, float cooldown) CalculateAdaptedValues()
    {
        return (0, 0f);
    }

    public enum TargetType
    {
        None,
        All,
        Custom,
        Host,
        Impostor,
        Crewmate
    }
}
