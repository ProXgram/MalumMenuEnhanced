using System;
using System.Collections.Generic;
using UnityEngine;

namespace MalumMenu;

public enum ColorCycleMode { Off, LocalRainbow, LobbyCycle }

public static class ColorCycleHandler
{
    private static PlayerControl _player;
    private static CosmeticsLayer _cosmetics;
    private static IntPtr _clientPointer;
    private static IntPtr _shipPointer;
    private static bool _previewApplied;
    private static int _nextColor;
    private static double _nextChangeAt;

    public static ColorCycleMode Mode { get; private set; }
    public static string StatusText { get; private set; } = "Colors: stopped";

    public static void Start(ColorCycleMode mode)
    {
        Stop(); // A switch must restore a previous visual preview first.
        if (mode == ColorCycleMode.Off) return;
        var now = Time.realtimeSinceStartupAsDouble;
        if (!double.IsFinite(now))
        {
            StatusText = "Colors: waiting for a valid game clock";
            return;
        }
        var local = PlayerControl.LocalPlayer;
        if (!HasContext(local) || local.Data.IsDead || !Application.isFocused || MalumMenu.isPanicked)
        {
            StatusText = "Colors: join as a living player first";
            return;
        }
        if (local.CurrentOutfitType != PlayerOutfitType.Default)
        {
            StatusText = "Colors: wait for your normal outfit";
            return;
        }
        if (mode == ColorCycleMode.LobbyCycle && !IsJoinedLobby())
        {
            StatusText = "Lobby colors: join a lobby first";
            return;
        }

        _player = local;
        _cosmetics = local.cosmetics;
        _clientPointer = AmongUsClient.Instance.Pointer;
        _shipPointer = ShipStatus.Instance ? ShipStatus.Instance.Pointer : IntPtr.Zero;
        _nextColor = local.CurrentOutfit.ColorId + 1;
        Mode = mode;
        if (!ScheduleNext(now)) return;
        StatusText = mode == ColorCycleMode.LocalRainbow
            ? "Rainbow: local visual preview active"
            : "Lobby colors: waiting to request an available color";
    }

    public static void Stop()
    {
        RestorePreview();
        Mode = ColorCycleMode.Off;
        _player = null;
        _cosmetics = null;
        _clientPointer = IntPtr.Zero;
        _shipPointer = IntPtr.Zero;
        _nextColor = 0;
        _nextChangeAt = 0;
        StatusText = "Colors: stopped";
    }

    public static void Tick()
    {
        if (Mode == ColorCycleMode.Off) return;
        try
        {
            var now = Time.realtimeSinceStartupAsDouble;
            if (!double.IsFinite(now))
            {
                StopWithStatus("Colors: stopped after an unavailable game clock");
                return;
            }
            var local = PlayerControl.LocalPlayer;
            if (!HasContext(local) || !_player || !_cosmetics ||
                local.Pointer != _player.Pointer || local.cosmetics.Pointer != _cosmetics.Pointer ||
                AmongUsClient.Instance.Pointer != _clientPointer ||
                (ShipStatus.Instance ? ShipStatus.Instance.Pointer : IntPtr.Zero) != _shipPointer)
            {
                StopWithStatus("Colors: stopped after the player or game changed");
                return;
            }
            if (MalumMenu.isPanicked)
            {
                StopWithStatus("Colors: stopped");
                return;
            }

            if (Mode == ColorCycleMode.LobbyCycle)
            {
                // CmdCheckColor is the ordinary host-mediated lobby request.
                // Never request a mid-round color or send a restore request.
                if (!IsJoinedLobby() || local.Data.IsDead || !Application.isFocused ||
                    local.CurrentOutfitType != PlayerOutfitType.Default)
                {
                    StopWithStatus("Lobby colors: stopped when the lobby context changed");
                    return;
                }
                TickLobby(local);
                return;
            }

            if (local.Data.IsDead || !Application.isFocused ||
                local.CurrentOutfitType != PlayerOutfitType.Default)
            {
                RestorePreview();
                if (!ScheduleNext(now)) return;
                StatusText = local.Data.IsDead ? "Rainbow: paused for a dead player"
                    : !Application.isFocused ? "Rainbow: paused while the game is unfocused"
                    : "Rainbow: paused during a disguise";
                return;
            }

            if (now < _nextChangeAt) return;
            if (!ScheduleNext(now)) return; // One step; never replay missed ticks.
            var count = PaletteCount();
            if (count == 0)
            {
                RestorePreview();
                StatusText = "Rainbow: waiting for the color palette";
                return;
            }
            var color = ((_nextColor % count) + count) % count;
            _nextColor = (color + 1) % count;
            // Visuals only: no outfit/data/saved customization writes and no RPC.
            _cosmetics.SetColor(color);
            _previewApplied = true;
            StatusText = "Rainbow: local visual preview active";
        }
        catch (Exception error)
        {
            StopWithStatus("Colors: stopped after an unavailable color update");
            MalumMenu.Log?.LogWarning("Color cycle stopped: " + error.Message);
        }
    }

    private static void TickLobby(PlayerControl local)
    {
        var now = Time.realtimeSinceStartupAsDouble;
        if (now < _nextChangeAt) return;
        if (!ScheduleNext(now)) return;
        var count = Mathf.Min(PaletteCount(), byte.MaxValue + 1);
        var players = PlayerControl.AllPlayerControls;
        if (count == 0 || players == null)
        {
            StatusText = "Lobby colors: waiting for player colors";
            return;
        }

        var occupied = new HashSet<int>();
        foreach (var player in players)
        {
            if (!player || player.Pointer == local.Pointer || player.Data == null || player.Data.Disconnected)
                continue;
            var outfit = player.Data.DefaultOutfit;
            if (outfit != null && outfit.ColorId >= 0 && outfit.ColorId < count)
                occupied.Add(outfit.ColorId);
        }
        var actualColor = local.Data.DefaultOutfit?.ColorId ?? local.CurrentOutfit.ColorId;
        for (var offset = 0; offset < count; offset++)
        {
            var color = (((_nextColor + offset) % count) + count) % count;
            if (color == actualColor || occupied.Contains(color)) continue;
            _nextColor = (color + 1) % count;
            // Native host arbitration remains responsible for accepting the
            // request and handling a simultaneous request from another player.
            local.CmdCheckColor((byte)color);
            StatusText = "Lobby colors: requested an available color";
            return;
        }
        StatusText = "Lobby colors: waiting for an unused color";
    }

    private static bool HasContext(PlayerControl local) =>
        Utils.isClient && AmongUsClient.Instance && (Utils.isLobby || Utils.isInGame || Utils.isFreePlay) &&
        local && local.AmOwner && local.Data != null && !local.Data.Disconnected && local.cosmetics &&
        local.CurrentOutfit != null;

    private static bool IsJoinedLobby() =>
        Utils.isLobby && !Utils.isInGame && !Utils.isFreePlay;

    private static int PaletteCount()
    {
        var colors = Palette.PlayerColors;
        var shadows = Palette.ShadowColors;
        return colors == null || shadows == null ? 0 : Mathf.Min(colors.Length, shadows.Length);
    }

    private static float Interval()
    {
        var lobby = Mode == ColorCycleMode.LobbyCycle;
        var configured = lobby ? MalumMenu.lobbyColorInterval?.Value ?? 3f
            : MalumMenu.rainbowColorInterval?.Value ?? 0.6f;
        if (!float.IsFinite(configured)) configured = lobby ? 3f : 0.6f;
        return Mathf.Clamp(configured, lobby ? 2f : 0.15f, lobby ? 10f : 3f);
    }

    private static bool ScheduleNext(double now)
    {
        var next = now + Interval();
        if (!double.IsFinite(next) || next <= now)
        {
            StopWithStatus("Colors: stopped after an unavailable game clock");
            return false;
        }
        _nextChangeAt = next;
        return true;
    }

    private static void RestorePreview()
    {
        if (!_previewApplied) return;
        _previewApplied = false;
        try
        {
            if (!_player || !_player.AmOwner || !_cosmetics || !_player.cosmetics ||
                _player.cosmetics.Pointer != _cosmetics.Pointer || _player.Data == null) return;
            var outfit = _player.CurrentOutfit;
            var count = PaletteCount();
            if (outfit != null && outfit.ColorId >= 0 && outfit.ColorId < count)
                _cosmetics.SetColor(outfit.ColorId);
        }
        catch (Exception error)
        {
            MalumMenu.Log?.LogWarning("Rainbow preview could not be restored: " + error.Message);
        }
    }

    private static void StopWithStatus(string status)
    {
        Stop();
        StatusText = status;
    }
}
