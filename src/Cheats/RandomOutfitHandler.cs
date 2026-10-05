using System;
using System.Collections.Generic;
using AmongUs.Data;
using AmongUs.Data.Player;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using InnerNet;
using UnityEngine;

namespace MalumMenu;

public static class RandomOutfitHandler
{
    private const double CooldownSeconds = 2;
    private static readonly System.Random Random = new();
    private static Outfit _previousSaved;
    private static Outfit _previousVisible;
    private static IntPtr _previousProfile;
    private static double _nextActionAt;
    private static double _lastClock;

    public static string StatusText { get; private set; } = "Pick an outfit before the round or in practice.";
    public static bool CanRestore
    {
        get
        {
            try
            {
                var profile = DataManager.Player;
                return _previousSaved != null && profile != null && profile.Pointer == _previousProfile;
            }
            catch { return false; }
        }
    }

    public static void Apply() => Change(false);
    public static void Restore() => Change(true);

    private static void Change(bool restore)
    {
        try
        {
            if (!TryContext(out var profile, out var local)) return;
            var now = Time.realtimeSinceStartupAsDouble;
            if (!double.IsFinite(now))
            {
                StatusText = "Wait until the game clock is ready.";
                return;
            }
            if (now < _lastClock) _nextActionAt = now + CooldownSeconds;
            _lastClock = now;
            if (now < _nextActionAt)
            {
                StatusText = "Wait two seconds between outfit changes.";
                return;
            }
            if (restore && !CanRestore)
            {
                StatusText = "No previous outfit is saved for this player profile.";
                return;
            }
            if (!DestroyableSingleton<HatManager>.InstanceExists)
            {
                StatusText = "Wait until the outfit catalog is ready.";
                return;
            }

            var manager = DestroyableSingleton<HatManager>.Instance;
            var unlockedHats = manager.GetUnlockedHats();
            var unlockedSkins = manager.GetUnlockedSkins();
            var unlockedVisors = manager.GetUnlockedVisors();
            var unlockedPets = manager.GetUnlockedPets();
            if (unlockedHats == null || unlockedSkins == null || unlockedVisors == null || unlockedPets == null)
            {
                StatusText = "The game has not loaded a usable outfit catalog yet.";
                return;
            }
            var hats = Catalog(unlockedHats, manager);
            var skins = Catalog(unlockedSkins, manager);
            var visors = Catalog(unlockedVisors, manager);
            var pets = Catalog(unlockedPets, manager);

            var saved = ReadSaved(profile);
            var visible = local ? ReadVisible(local) : saved;
            Outfit selection;
            Outfit savedSelection;
            if (restore)
            {
                selection = local ? _previousVisible : _previousSaved;
                savedSelection = _previousSaved;
                if (!IsAvailable(selection, hats, skins, visors, pets) ||
                    !IsAvailable(savedSelection, hats, skins, visors, pets))
                {
                    StatusText = "The previous outfit is unavailable in the current catalog or game mode.";
                    return;
                }
            }
            else
            {
                selection = new Outfit(Pick(hats, visible.Hat), Pick(skins, visible.Skin),
                    Pick(visors, visible.Visor), Pick(pets, visible.Pet));
                savedSelection = selection;
            }

            // An explicit click performs one normal selection batch, never a timer or a retry burst.
            _nextActionAt = now + CooldownSeconds;
            try
            {
                WriteSaved(profile, savedSelection);
                profile.Save();
            }
            catch
            {
                WriteSaved(profile, saved);
                profile.Save();
                throw;
            }
            if (!restore && !CanRestore)
            {
                _previousSaved = saved;
                _previousVisible = visible;
                _previousProfile = profile.Pointer;
            }

            if (local)
            {
                try
                {
                    // Each native method owns its ordinary cosmetic sequence and network message.
                    local.RpcSetHat(selection.Hat);
                    local.RpcSetSkin(selection.Skin);
                    local.RpcSetVisor(selection.Visor);
                    local.RpcSetPet(selection.Pet);
                }
                catch
                {
                    StatusText = "Outfit saved; some appearance requests could not be sent. Try Restore later.";
                    return;
                }
            }
            StatusText = restore
                ? (local ? "Previous outfit saved and requested." : "Previous outfit restored in your profile.")
                : (local ? "Random outfit saved and requested." : "Random outfit saved. Join a lobby to wear it.");
        }
        catch (Exception)
        {
            StatusText = "The outfit could not be changed. Wait until the catalog is ready.";
        }
    }

    private static bool TryContext(out PlayerData profile, out PlayerControl local)
    {
        profile = null;
        local = null;
        var client = AmongUsClient.Instance;
        var practice = Utils.isFreePlay;
        var lobby = Utils.isLobby;
        if (MalumMenu.isPanicked || !Application.isFocused || Utils.isMeeting || Utils.isExiling ||
            (!practice && (Utils.isInGame || (client && client.GameState == InnerNetClient.GameStates.Started))))
        {
            StatusText = "Change outfits before the round or in practice.";
            return false;
        }
        local = PlayerControl.LocalPlayer;
        if (lobby || practice)
        {
            if (!client || !local || !local.AmOwner || local.Data == null || local.Data.Disconnected ||
                local.Data.IsDead || local.CurrentOutfitType != PlayerOutfitType.Default ||
                (practice && !ShipStatus.Instance) || local.inVent || local.onLadder || local.inMovingPlat)
            {
                StatusText = "Wait until your living player and normal outfit are ready.";
                return false;
            }
        }
        else if ((client && client.GameState != InnerNetClient.GameStates.NotJoined) || local || ShipStatus.Instance)
        {
            StatusText = "Wait until the main menu or lobby is ready.";
            return false;
        }
        profile = DataManager.Player;
        if (profile == null || profile.Customization == null)
        {
            StatusText = "Wait until your player profile is ready.";
            return false;
        }
        return true;
    }

    private static List<string> Catalog<T>(Il2CppReferenceArray<T> items, HatManager manager) where T : CosmeticData
    {
        // Empty is the game's valid default ID for every no-outfit slot.
        // Keep it usable even when that slot has no purchased catalog items.
        var result = new List<string> { string.Empty };
        if (items == null) return result;
        var seen = new HashSet<string>(StringComparer.Ordinal) { string.Empty };
        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (item == null) continue;
            var id = item.ProductId;
            if (string.IsNullOrWhiteSpace(id) ||
                id == NetworkedPlayerInfo.PlayerOutfit.MISSING_COSMETIC_ID || !seen.Add(id)) continue;
            // GetUnlocked uses the game's normal Free/GetPurchase catalog predicate.
            // Free Cosmetics can expand it; this does not purchase or grant an item.
            if (manager.CheckLongModeValidCosmetic(id, false)) result.Add(id);
        }
        return result;
    }

    private static string Pick(List<string> choices, string current)
    {
        var index = choices.IndexOf(current);
        if (choices.Count < 2 || index < 0) return choices[Random.Next(choices.Count)];
        var selected = Random.Next(choices.Count - 1);
        return choices[selected >= index ? selected + 1 : selected];
    }

    private static bool IsAvailable(Outfit outfit, List<string> hats, List<string> skins,
        List<string> visors, List<string> pets) => outfit != null && hats.Contains(outfit.Hat) &&
        skins.Contains(outfit.Skin) && visors.Contains(outfit.Visor) && pets.Contains(outfit.Pet);

    private static Outfit ReadSaved(PlayerData profile) => new(profile.Customization.Hat,
        profile.Customization.Skin, profile.Customization.Visor, profile.Customization.Pet);

    private static Outfit ReadVisible(PlayerControl local) => new(local.CurrentOutfit.HatId,
        local.CurrentOutfit.SkinId, local.CurrentOutfit.VisorId, local.CurrentOutfit.PetId);

    private static void WriteSaved(PlayerData profile, Outfit outfit)
    {
        profile.Customization.Hat = outfit.Hat;
        profile.Customization.Skin = outfit.Skin;
        profile.Customization.Visor = outfit.Visor;
        profile.Customization.Pet = outfit.Pet;
    }

    private sealed class Outfit
    {
        public readonly string Hat, Skin, Visor, Pet;
        public Outfit(string hat, string skin, string visor, string pet)
        {
            Hat = hat; Skin = skin; Visor = visor; Pet = pet;
        }
    }
}
