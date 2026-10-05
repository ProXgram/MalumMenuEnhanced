using System;
using AmongUs.Data;
using InnerNet;

namespace MalumMenu;

public static class NicknamePreset
{
    public static string NameInput { get; set; } = "he is hacking";
    public static string StatusText { get; private set; } = "Uses your own normal nickname.";

    public static void Apply()
    {
        var name = NameInput;
        try
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                StatusText = "Enter a nickname first.";
                return;
            }
            if (name.Length > 128)
            {
                StatusText = "Keep the nickname within 128 characters.";
                return;
            }
            var client = AmongUsClient.Instance;
            if (Utils.isInGame || Utils.isFreePlay || Utils.isMeeting || Utils.isExiling ||
                (client && client.GameState == InnerNetClient.GameStates.Started))
            {
                StatusText = "Set your nickname before the round starts.";
                return;
            }
            if (!NameTextBehaviour.IsValidName(name))
            {
                StatusText = "The game rejected this nickname.";
                return;
            }
            var local = PlayerControl.LocalPlayer;
            var inLobby = Utils.isLobby;
            if ((client && client.GameState != InnerNetClient.GameStates.NotJoined && !inLobby) ||
                (inLobby && (!local || !local.AmOwner || local.Data == null || local.Data.Disconnected)) ||
                (!inLobby && local))
            {
                StatusText = "Wait until the lobby or main menu is ready.";
                return;
            }
            var player = DataManager.Player;
            if (player == null || player.Customization == null)
            {
                StatusText = "Wait until your player profile is ready.";
                return;
            }
            var previous = player.Customization.Name;
            try
            {
                player.Customization.Name = name;
                player.Save();
                // This requests only the user's own ordinary player name.
                if (inLobby) local.RpcSetName(name);
            }
            catch
            {
                player.Customization.Name = previous;
                player.Save();
                throw;
            }
            StatusText = inLobby ? "Nickname requested: " + name
                : "Nickname saved: " + name + ". Join a lobby to use it.";
        }
        catch (Exception)
        {
            StatusText = "Nickname could not be changed.";
        }
    }
}
