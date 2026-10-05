using System;
using System.Collections.Generic;
using AmongUs.GameOptions;
using UnityEngine;

namespace MalumMenu;

// A detached notebook: never assign this behaviour as the player's role, and
// never put its death records into the native DetectiveLocationsController.
public static class MultiRoleDetectiveHandler
{
    private const float PendingDeathLifetime = 15f;
    private static readonly Dictionary<byte, PendingDeath> PendingDeaths = new();
    private static readonly Dictionary<byte, PendingDeath> Cases = new();
    private static readonly List<PendingDeath> OrderedCases = new();
    private static DetectiveRole _detective;
    private static DetectiveNotesMinigame _notes;
    private static MapBehaviour _caseMap;
    private static Vector3 _caseMapLocalPosition;
    private static bool _caseMapDepthOwned;
    private static SpriteRenderer _victimMarker;
    private static Material _victimMarkerMaterial;
    private static IntPtr _playerPointer;
    private static IntPtr _shipPointer;
    private static int _session;
    private static float _nextInterrogation;
    private static string _lastError;
    private static int _notesClosingDepth;
    private static bool _openingCaseMap;

    public sealed class PendingDeath
    {
        internal NetworkedPlayerInfo Victim;
        internal byte VictimId;
        internal IntPtr VictimPointer;
        internal int Session;
        internal float CapturedAt;
        internal Vector2 VictimPosition;
        internal bool HasVictimPosition;
        internal readonly Dictionary<byte, string> Locations = new();
        internal readonly List<NetworkedPlayerInfo> Suspects = new();
    }

    public static bool Available => MultiRoleHandler.CanOpenTool &&
        !(MapBehaviour.Instance && MapBehaviour.Instance.IsOpen) && GetPrefab();
    public static bool NotesOpen => _notes;
    public static bool HasCases => MultiRoleHandler.Active && OrderedCases.Count > 0;
    public static string StatusText => _lastError ?? (!MultiRoleHandler.Active
        ? "Detective tools are ready when Multi Role is active."
        : OrderedCases.Count == 0
            ? "Notes are ready. Death evidence is recorded while Multi Role is on."
            : $"{OrderedCases.Count} case(s). Choose a nearby player to interrogate for the selected case.");

    public static void Tick()
    {
        try
        {
            if (!EnsureSession()) return;
            // The detached notebook and its map have no assigned role's input
            // button. Close the owned map first, then Notes on a later Escape.
            if (_notes && Minigame.Instance && Minigame.Instance.Pointer == _notes.Pointer &&
                !MenuUI.isGUIActive && HudManager.InstanceExists &&
                !(HudManager.Instance.Chat && HudManager.Instance.Chat.IsOpenOrOpening) &&
                Input.GetKeyDown(KeyCode.Escape))
            {
                if (MapBehaviour.Instance && MapBehaviour.Instance.IsOpen)
                {
                    if (_caseMap && MapBehaviour.Instance.Pointer == _caseMap.Pointer) CloseCaseMap();
                }
                else CloseNotes();
            }
            if (_caseMap && (!_caseMap.IsOpen || !MapBehaviour.Instance ||
                MapBehaviour.Instance.Pointer != _caseMap.Pointer))
            {
                ReleaseCaseMap();
            }
            if (_notes && (Utils.isMeeting || Utils.isExiling)) CloseNotes();
            if (Utils.isMeeting || Utils.isExiling)
            {
                PendingDeaths.Clear();
                return;
            }
            if (PendingDeaths.Count == 0) return;

            var finished = new List<byte>();
            foreach (var entry in PendingDeaths)
            {
                var pending = entry.Value;
                var victim = pending.Victim;
                var player = victim?.Object;
                if (pending.Session != _session || !victim || victim.Disconnected ||
                    !player || player.Pointer != pending.VictimPointer ||
                    Time.unscaledTime - pending.CapturedAt > PendingDeathLifetime)
                {
                    finished.Add(entry.Key);
                    continue;
                }
                if (!victim.IsDead) continue;
                CommitDeath(pending);
                finished.Add(entry.Key);
            }
            foreach (var id in finished) PendingDeaths.Remove(id);
        }
        catch (Exception error)
        {
            Reset();
            ReportError("Detective tools stopped: " + error.Message);
        }
    }

    public static void Reset()
    {
        CloseNotes();
        var owned = _detective;
        _detective = null;
        if (owned) UnityEngine.Object.Destroy(owned.gameObject);
        PendingDeaths.Clear();
        Cases.Clear();
        OrderedCases.Clear();
        _playerPointer = IntPtr.Zero;
        _shipPointer = IntPtr.Zero;
        _nextInterrogation = 0f;
        _lastError = null;
        _session++;
    }

    public static void OpenNotes()
    {
        if (!Available || _notes) return;
        try
        {
            if (!EnsureSession() || !EnsureDetective() || !Camera.main) return;
            OpenOwnedNotes();
            _lastError = null;
        }
        catch (Exception error)
        {
            CloseNotes();
            ReportError("Could not open Detective Notes: " + error.Message);
        }
    }

    public static bool CanInterrogate(PlayerControl target)
    {
        if (!Available || !HasCases || Time.unscaledTime < _nextInterrogation ||
            !target || !target.Data || target.Data.IsDead || target.Data.Disconnected ||
            target.Pointer == PlayerControl.LocalPlayer.Pointer || !target.Collider || !target.Collider.enabled)
            return false;
        var prefab = GetPrefab();
        // The native getter is a constant distance, without player/host state.
        return prefab && Vector2.Distance(PlayerControl.LocalPlayer.GetTruePosition(),
            target.GetTruePosition()) <= prefab.GetAbilityDistance();
    }

    public static void Interrogate(PlayerControl target)
    {
        if (!CanInterrogate(target)) return;
        try
        {
            Trace("Interrogate ensure clone begin");
            if (!EnsureSession() || !EnsureDetective()) return;
            Trace("Interrogate ensure clone complete");
            var index = Mathf.Clamp(_detective.currentNotesIndex, 0, _detective.notesPageInfos.Count - 1);
            var page = _detective.notesPageInfos[index];
            if (!page.victimPlayer || !Cases.TryGetValue(page.victimPlayer.PlayerId, out var record)) return;
            var info = target.Data;
            Trace("Interrogate selected case=" + index + " target=" + info.PlayerId);
            foreach (var existing in record.Suspects)
                if (existing && existing.PlayerId == info.PlayerId)
                {
                    ReportError("This player is already in the selected case.");
                    return;
                }
            // Add only notebook data. Native Interrogate also disables the real
            // SecondaryAbilityButton, so it must not be called for a detached role.
            if (record.Suspects.Count >= 3)
            {
                ReportError("This case already has three suspects. Select another case in Notes.");
                return;
            }
            // Keep the actual data reference in our case model. Native
            // DetectiveSuspect struct/list marshalling is unnecessary for this
            // local notebook, and its native renderer casts the assigned role.
            record.Suspects.Add(info);
            Trace("Interrogate add complete count=" + record.Suspects.Count);
            _nextInterrogation = Time.unscaledTime + 1f;
            OpenOwnedNotes();
            Trace("Interrogate open Notes complete");
            _lastError = null;
        }
        catch (Exception error)
        {
            CloseNotes();
            ReportError("Could not interrogate this player: " + error.Message);
        }
    }

    // Native MurderPlayer accepts Succeeded or DecisionByHost. Both remain
    // candidates until the victim is actually dead; protected decisions expire.
    // Capture before native kill animation moves the killer and victim.
    internal static PendingDeath CapturePendingDeath(PlayerControl target, MurderResultFlags flags)
    {
        if ((flags & (MurderResultFlags.Succeeded | MurderResultFlags.DecisionByHost)) == 0 ||
            (flags & (MurderResultFlags.FailedError | MurderResultFlags.FailedProtected)) != 0 ||
            !EnsureSession() || Utils.isMeeting || Utils.isExiling ||
            !target || !target.Data || target.Data.IsDead || target.Data.Disconnected ||
            Cases.ContainsKey(target.PlayerId)) return null;

        // A host-decision request can be rejected by protection. A later kill
        // attempt must take a fresh snapshot instead of reusing that candidate.
        PendingDeaths.Remove(target.PlayerId);

        var pending = new PendingDeath
        {
            Victim = target.Data, VictimId = target.PlayerId, VictimPointer = target.Pointer,
            Session = _session, CapturedAt = Time.unscaledTime,
            VictimPosition = target.GetTruePosition()
        };
        pending.HasVictimPosition = float.IsFinite(pending.VictimPosition.x) &&
            float.IsFinite(pending.VictimPosition.y);
        foreach (var player in PlayerControl.AllPlayerControls)
        {
            if (!player || !player.Data || player.Data.Disconnected || player.Data.IsDead) continue;
            var room = Utils.GetRoomFromPosition(player.GetTruePosition());
            pending.Locations[player.PlayerId] = room ? room.RoomId.ToString() : "Between rooms";
        }
        return pending;
    }

    internal static void ConfirmDeath(PendingDeath pending, PlayerControl target)
    {
        if (pending == null || !MultiRoleHandler.Active || pending.Session != _session ||
            !target || target.Pointer != pending.VictimPointer || !target.Data ||
            target.Data.Disconnected || Cases.ContainsKey(pending.VictimId)) return;
        if (target.Data.IsDead) CommitDeath(pending);
        else PendingDeaths[pending.VictimId] = pending;
    }

    internal static bool Owns(DetectiveRole role) => role && _detective && role.Pointer == _detective.Pointer;
    internal static bool Owns(DetectiveNotesMinigame notes) => notes && _notes && notes.Pointer == _notes.Pointer;

    internal static bool Owns(DetectiveNotesSuspectInterface suspect) => suspect && _notes &&
        suspect.transform.IsChildOf(_notes.transform);

    internal static string GetCapturedLocation(byte victimId, byte playerId) =>
        Cases.TryGetValue(victimId, out var record) && record.Locations.TryGetValue(playerId, out var location)
            ? location : "Unknown (not recorded)";

    internal static void RenderSuspect(DetectiveNotesSuspectInterface view, NetworkedPlayerInfo info,
        NetworkedPlayerInfo victim, int index, int maskLayer)
    {
        Trace("RenderSuspect slot=" + index + " begin");
        if (!info || !victim)
        {
            view.Clear();
            return;
        }
        var outfit = GetPortraitOutfit(info);
        Trace("RenderSuspect slot=" + index + " outfit available=" + (outfit != null));
        if (view.container) view.container.SetActive(true);
        Trace("RenderSuspect slot=" + index + " container complete; portrait begin");
        if (view.player)
        {
            view.player.gameObject.SetActive(outfit != null);
            view.player.SetMaskLayer(maskLayer);
            Trace("RenderSuspect slot=" + index + " mask complete; outfit begin");
            // Match native DetectiveNotesSuspectInterface.SetPlayerInfo:
            // notebook portraits have no pet display and use a living avatar.
            // Resolve the outfit with a guarded lookup outside the native
            // UpdateFromPlayerData indexer path reached in the crash dump.
            if (outfit != null) view.player.UpdateFromPlayerOutfit(outfit,
                (PlayerMaterial.MaskType)2, false, false, null, true);
            Trace("RenderSuspect slot=" + index + " outfit complete");
        }
        Trace("RenderSuspect slot=" + index + " name begin");
        if (view.playerName) view.playerName.text = outfit != null ? outfit.PlayerName : "Player " + info.PlayerId;
        Trace("RenderSuspect slot=" + index + " color begin");
        if (view.playerColor) view.playerColor.text = outfit != null && outfit.ColorId >= 0 &&
            outfit.ColorId < Palette.PlayerColors.Length ? Palette.GetColorName(outfit.ColorId) : "Unknown color";
        Trace("RenderSuspect slot=" + index + " location begin");
        if (view.locationName) view.locationName.text = GetCapturedLocation(victim.PlayerId, info.PlayerId);
        Trace("RenderSuspect slot=" + index + " number begin");
        if (view.numberedText) view.numberedText.text = (index + 1).ToString();
        Trace("RenderSuspect slot=" + index + " dead-image begin");
        if (view.deadImage) view.deadImage.SetActive(info.IsDead);
        Trace("RenderSuspect slot=" + index + " complete");
    }

    private static NetworkedPlayerInfo.PlayerOutfit GetPortraitOutfit(NetworkedPlayerInfo info)
    {
        var outfits = info.Outfits;
        if (outfits == null) return null;
        if (outfits.TryGetValue(PlayerOutfitType.Default, out var outfit) && outfit != null) return outfit;
        var player = info.Object;
        return player && outfits.TryGetValue(player.CurrentOutfitType, out outfit) ? outfit : null;
    }

    internal static void RenderPage(DetectiveNotesMinigame notes)
    {
        Trace("RenderPage begin owned=" + Owns(notes));
        if (!Owns(notes) || !_detective || notes.currentPageIndex < 0 ||
            notes.currentPageIndex >= _detective.notesPageInfos.Count) return;
        var page = _detective.notesPageInfos[notes.currentPageIndex];
        if (page == null || !page.victimPlayer || notes.suspectContainers == null ||
            !Cases.TryGetValue(page.victimPlayer.PlayerId, out var record)) return;
        Trace("RenderPage suspect count=" + record.Suspects.Count + " slots=" + notes.suspectContainers.Length);
        if (notes.noSuspectsPostIt) notes.noSuspectsPostIt.SetActive(record.Suspects.Count == 0);
        for (var index = 0; index < notes.suspectContainers.Length; index++)
        {
            var slot = notes.suspectContainers[index];
            Trace("RenderPage slot=" + index + " begin");
            if (!slot) continue;
            // This notebook admits three suspects per case. The current native
            // prefab also contains a fourth slot which is not usable here.
            slot.gameObject.SetActive(index < 3);
            if (index >= 3) continue;
            bool occupied = index < record.Suspects.Count;
            if (slot.BGSprite) slot.BGSprite.sprite = occupied ? slot.activeBGSprite : slot.inactiveBGSprite;
            if (slot.SuspectPlaceholderText) slot.SuspectPlaceholderText.gameObject.SetActive(!occupied);
            if (!occupied)
            {
                if (slot.suspectInterface) slot.suspectInterface.Clear();
                continue;
            }
            if (!slot.suspectInterface)
            {
                if (!slot.SuspectPrefab) continue;
                var instance = UnityEngine.Object.Instantiate(slot.SuspectPrefab, slot.transform, false);
                slot.suspectInterface = instance.GetComponent<DetectiveNotesSuspectInterface>();
                Trace("RenderPage slot=" + index + " instantiate complete");
                if (!slot.suspectInterface)
                {
                    UnityEngine.Object.Destroy(instance);
                    continue;
                }
            }
            RenderSuspect(slot.suspectInterface, record.Suspects[index], page.victimPlayer, index, slot.MaskLayer);
        }
        Trace("RenderPage complete");
    }

    internal static void OpenCaseMap(DetectiveNotesMinigame notes)
    {
        if (!Owns(notes) || !HudManager.InstanceExists || !CanRenderNotebookMap(PlayerControl.LocalPlayer)) return;
        if (MapBehaviour.Instance && MapBehaviour.Instance.IsOpen) return;
        if (!TryGetSelectedCase(notes, out var record) || !record.HasVictimPosition)
        {
            ReportError("No death position was recorded for this case.");
            return;
        }
        // Native Detective map mode casts the assigned role. Normal map mode
        // lets this detached notebook display the ship without replacing it.
        // Native normal-map rendering rejects the notebook's movement lock.
        // Supply its UI-only guard answer solely during this synchronous call;
        // no movement flag is changed and later gameplay queries stay native.
        _openingCaseMap = true;
        try { HudManager.Instance.ToggleMapVisible(new MapOptions { Mode = MapOptions.Modes.Normal }); }
        finally { _openingCaseMap = false; }
        if (MapBehaviour.Instance && MapBehaviour.Instance.IsOpen)
        {
            _caseMap = MapBehaviour.Instance;
            _caseMapLocalPosition = _caseMap.transform.localPosition;
            _caseMapDepthOwned = true;
            // GenericShow uses the ordinary map's depth, which is behind the
            // notebook. Lift only our map, keeping its native layout and scale.
            // Save local position so later native map use gets its exact depth.
            var mapPosition = _caseMap.transform.position;
            var frontDepth = notes.transform.position.z - 10f;
            if (mapPosition.z > frontDepth) mapPosition.z = frontDepth;
            _caseMap.transform.position = mapPosition;
            UpdateCaseMap(_caseMap);
            if (notes.mapFadeBackground) notes.mapFadeBackground.SetActive(true);
        }
    }

    internal static bool AllowOwnedMapRender(PlayerControl player) => _openingCaseMap && CanRenderNotebookMap(player);

    private static bool CanRenderNotebookMap(PlayerControl player) => MultiRoleHandler.Active &&
        player && PlayerControl.LocalPlayer && player.Pointer == PlayerControl.LocalPlayer.Pointer &&
        _notes && Minigame.Instance && Minigame.Instance.Pointer == _notes.Pointer &&
        !Utils.isMeeting && !Utils.isExiling && !MenuUI.isGUIActive && HudManager.InstanceExists &&
        !HudManager.Instance.IsIntroDisplayed &&
        !(HudManager.Instance.Chat && HudManager.Instance.Chat.IsOpenOrOpening) &&
        !player.inVent && !player.onLadder && !player.inMovingPlat;

    internal static void UpdateCaseMap(MapBehaviour map)
    {
        if (!_caseMap || !map || map.Pointer != _caseMap.Pointer || !map.IsOpen) return;
        if (!MultiRoleHandler.Active || !TryGetSelectedCase(_notes, out var record) ||
            !record.HasVictimPosition || !ShipStatus.Instance || !map.HerePoint)
        {
            DestroyVictimMarker();
            return;
        }

        var ship = ShipStatus.Instance;
        if (!float.IsFinite(ship.MapScale) || Mathf.Abs(ship.MapScale) < 0.0001f)
        {
            DestroyVictimMarker();
            return;
        }
        if (!_victimMarker)
        {
            _victimMarker = UnityEngine.Object.Instantiate(map.HerePoint, map.HerePoint.transform.parent);
            _victimMarker.gameObject.SetActive(true);
            _victimMarkerMaterial = _victimMarker.material;
            var tint = new Color(1f, 0.2f, 0.7f, 1f);
            _victimMarkerMaterial.SetColor(PlayerMaterial.BackColor, tint);
            _victimMarkerMaterial.SetColor(PlayerMaterial.BodyColor, tint);
            _victimMarkerMaterial.SetColor(PlayerMaterial.VisorColor, Palette.VisorColor);
        }
        // This point is the confirmed kill's pre-animation snapshot, never the
        // victim's later ghost/body position. Match the native minimap scaling.
        var point = new Vector3(record.VictimPosition.x, record.VictimPosition.y, 0f);
        point /= ship.MapScale;
        point.x *= Mathf.Sign(ship.transform.localScale.x);
        point.z = -1f;
        _victimMarker.transform.localPosition = point;
    }

    internal static void OnCaseMapClosed(MapBehaviour map)
    {
        if (!_caseMap || !map || _caseMap.Pointer != map.Pointer) return;
        ReleaseCaseMap();
    }

    internal static void CloseCaseMap()
    {
        var owned = _caseMap;
        if (!owned) { ReleaseCaseMap(); return; }
        try
        {
            // A replaced singleton can belong to another tool. Release our
            // original presentation without closing that other map's overlay.
            if (MapBehaviour.Instance && MapBehaviour.Instance.Pointer == owned.Pointer && owned.IsOpen)
                owned.Close();
        }
        catch (Exception error) { ReportError("Could not close Detective case map: " + error.Message); }
        finally { ReleaseCaseMap(); }
    }

    private static void ReleaseCaseMap()
    {
        if (_caseMap && _caseMapDepthOwned) _caseMap.transform.localPosition = _caseMapLocalPosition;
        _caseMapDepthOwned = false;
        DestroyVictimMarker();
        _caseMap = null;
        if (_notes && _notes.mapFadeBackground) _notes.mapFadeBackground.SetActive(false);
    }

    private static bool TryGetSelectedCase(DetectiveNotesMinigame notes, out PendingDeath record)
    {
        record = null;
        if (!Owns(notes) || !_detective || _detective.notesPageInfos == null ||
            notes.currentPageIndex < 0 || notes.currentPageIndex >= _detective.notesPageInfos.Count) return false;
        var page = _detective.notesPageInfos[notes.currentPageIndex];
        return page != null && page.victimPlayer && Cases.TryGetValue(page.victimPlayer.PlayerId, out record);
    }

    private static void DestroyVictimMarker()
    {
        var marker = _victimMarker;
        var material = _victimMarkerMaterial;
        _victimMarker = null;
        _victimMarkerMaterial = null;
        if (marker) UnityEngine.Object.Destroy(marker.gameObject);
        if (material) UnityEngine.Object.Destroy(material);
    }

    internal static bool BeginNotesClose(DetectiveNotesMinigame notes)
    {
        if (!Owns(notes)) return false;
        _notesClosingDepth++;
        return true;
    }

    internal static void EndNotesClose() => _notesClosingDepth = Math.Max(0, _notesClosingDepth - 1);

    internal static bool MayCloseMap(MapBehaviour map) => _notesClosingDepth == 0 ||
        (_caseMap && map && _caseMap.Pointer == map.Pointer);

    private static bool EnsureSession()
    {
        if (!MultiRoleHandler.Active)
        {
            if (_playerPointer != IntPtr.Zero || _detective || _notes || OrderedCases.Count > 0)
                Reset();
            return false;
        }
        var local = PlayerControl.LocalPlayer;
        var ship = ShipStatus.Instance;
        if (_playerPointer != local.Pointer || _shipPointer != ship.Pointer)
        {
            Reset();
            _playerPointer = local.Pointer;
            _shipPointer = ship.Pointer;
        }
        return true;
    }

    private static DetectiveRole GetPrefab()
    {
        if (!RoleManager.Instance) return null;
        foreach (var role in RoleManager.Instance.AllRoles)
            if (role && role.Role == RoleTypes.Detective)
            {
                var detective = role.TryCast<DetectiveRole>();
                return detective && detective.notesPrefab ? detective : null;
            }
        return null;
    }

    private static bool EnsureDetective()
    {
        if (_detective) return true;
        var prefab = GetPrefab();
        if (!prefab) { ReportError("Detective Notes are unavailable in this scene."); return false; }
        _detective = UnityEngine.Object.Instantiate(prefab);
        _detective.enabled = false;
        _detective.gameObject.SetActive(false);
        _detective.Player = PlayerControl.LocalPlayer;
        _detective.buttonManager = null;
        _detective.secondaryButtonManager = null;
        _detective.abilityInfo = null;
        _detective.meetingAbilityInfo = null;
        _detective.currentTarget = null;
        _detective.notesMinigame = null;
        _detective.notesPageInfos = new Il2CppSystem.Collections.Generic.List<DetectiveNotesPageInfo>();
        _detective.deadPlayers = new Il2CppSystem.Collections.Generic.List<NetworkedPlayerInfo>();
        _detective.currentNotesIndex = 0;
        foreach (var record in OrderedCases) AddNativePage(record);
        return true;
    }

    private static void CommitDeath(PendingDeath pending)
    {
        if (Cases.ContainsKey(pending.VictimId)) return;
        Cases.Add(pending.VictimId, pending);
        OrderedCases.Add(pending);
        if (_detective) AddNativePage(pending);
        _lastError = null;
    }

    private static void AddNativePage(PendingDeath record)
    {
        var page = new DetectiveNotesPageInfo(record.Victim);
        // Native Notes still owns victim fields, tabs, editable controls and
        // masks. Its suspect list stays empty; our reference-only model renders
        // suspect evidence after native layout without calling SetPlayerInfo.
        page.suspects = new Il2CppSystem.Collections.Generic.List<DetectiveSuspect>();
        if (record.Locations.TryGetValue(record.VictimId, out var room)) page.SetLocation(room);
        _detective.notesPageInfos.Add(page);
        _detective.deadPlayers.Add(record.Victim);
        if (!_notes) _detective.currentNotesIndex = _detective.notesPageInfos.Count - 1;
    }

    private static void OpenOwnedNotes()
    {
        Trace("OpenNotes instantiate begin");
        var instance = UnityEngine.Object.Instantiate(_detective.notesPrefab, Camera.main.transform, false);
        Trace("OpenNotes instantiate complete; component begin");
        _notes = instance.GetComponent<DetectiveNotesMinigame>();
        if (!_notes)
        {
            UnityEngine.Object.Destroy(instance);
            throw new InvalidOperationException("Native Notes prefab has no notebook component.");
        }
        _notes.transform.localPosition = new Vector3(0f, 0f, -50f);
        _detective.notesMinigame = _notes;
        Trace("OpenNotes SetAssociatedDetective begin");
        _notes.SetAssociatedDetective(_detective);
        Trace("OpenNotes SetAssociatedDetective complete");
        if (_detective.notesPageInfos.Count > 0)
        {
            var index = Mathf.Clamp(_detective.currentNotesIndex, 0, _detective.notesPageInfos.Count - 1);
            Trace("OpenNotes OpenExistingPage begin index=" + index);
            _notes.OpenExistingPage(_detective, _detective.notesPageInfos[index]);
            Trace("OpenNotes OpenExistingPage complete");
        }
        Trace("OpenNotes Begin begin");
        _notes.Begin(null);
        Trace("OpenNotes Begin complete");
    }

    public static void CloseNotes()
    {
        var owned = _notes;
        if (!owned) { CloseCaseMap(); _notes = null; return; }
        try
        {
            // Close dismisses the notebook-specific controller overlays and
            // materials. ForceClose then ends its normal closing animation.
            owned.Close();
            owned.ForceClose();
        }
        catch { UnityEngine.Object.Destroy(owned.gameObject); }
        finally
        {
            CloseCaseMap();
            _notes = null;
            if (_detective) _detective.notesMinigame = null;
        }
    }

    private static void ReportError(string message)
    {
        if (_lastError != message) MalumMenu.Log?.LogWarning(message);
        _lastError = message;
    }

    // Diagnostic builds can trace the local notebook's native presentation.
    [System.Diagnostics.Conditional("DEBUG")]
    internal static void Trace(string message) =>
        MalumMenu.Log?.LogInfo("MultiRole Detective diagnostic: " + message);
}
