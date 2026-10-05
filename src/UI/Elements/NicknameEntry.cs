using System;
using System.Text;
using UnityEngine;

namespace MalumMenu;

/// <summary>Draft-only nickname input using the game's supported keyboard input path.</summary>
public static class NicknameEntry
{
    private const int MaximumLength = 128;
    private const int PreviewLength = 28;
    private static bool _selectAll;
    private static int _lastInputFrame = -1;
    private static GUIStyle _entryStyle;

    public static bool IsEditing { get; private set; }

    public static void Draw()
    {
        if (_entryStyle == null)
            _entryStyle = new GUIStyle(GUIStylePreset.NormalButton)
            {
                alignment = TextAnchor.MiddleLeft,
                richText = false,
                clipping = TextClipping.Clip,
                wordWrap = false,
            };

        var text = Filter(NicknamePreset.NameInput ?? "");
        var preview = Preview(text);
        var label = (IsEditing ? "Typing: " : "Nickname: ") +
            (preview.Length == 0 ? "click to type" : preview);
        if (GUILayout.Button(label, _entryStyle, GUILayout.Width(270f)))
        {
            NicknamePreset.NameInput = text;
            IsEditing = true;
            _selectAll = false;
            _lastInputFrame = -1;
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Clear", GUILayout.Width(85f)))
        {
            NicknamePreset.NameInput = "";
            _selectAll = false;
            IsEditing = true;
        }
        if (GUILayout.Button("Paste", GUILayout.Width(85f))) PasteClipboard();
        GUILayout.EndHorizontal();
        GUILayout.Label(IsEditing
            ? _selectAll ? "All selected. Type to replace; Enter finishes."
                : "Typing at end. Ctrl+A selects all; Enter finishes."
            : "Click nickname to type; use Set my nickname to apply.", GUILayout.Width(270f));
    }

    public static void Tick()
    {
        if (!IsEditing) return;
        if (!MenuUI.isGUIActive || MenuUI.ActiveTabName != "Fun" ||
            !Application.isFocused || MalumMenu.isPanicked)
        {
            StopEditing();
            return;
        }
        if (_lastInputFrame == Time.frameCount) return;
        _lastInputFrame = Time.frameCount;

        var control = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        if (control)
        {
            if (Input.GetKeyDown(KeyCode.A)) _selectAll = true;
            else if (Input.GetKeyDown(KeyCode.V)) PasteClipboard();
            // Shortcut letters must not become ordinary nickname text.
            return;
        }

        var input = Input.inputString;
        if (string.IsNullOrEmpty(input)) return;
        var draft = new StringBuilder(Filter(NicknamePreset.NameInput ?? ""));
        for (var index = 0; index < input.Length; index++)
        {
            var character = input[index];
            if (character is '\r' or '\n')
            {
                StopEditing(); // Enter ends editing; applying remains explicit.
                break;
            }
            if (character == '\b')
            {
                if (_selectAll) { draft.Clear(); _selectAll = false; }
                else RemoveLast(draft);
                continue;
            }
            if (!Allowed(character) || char.IsLowSurrogate(character)) continue;
            var length = 1;
            if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= input.Length || !char.IsLowSurrogate(input[index + 1])) continue;
                length = 2;
            }
            if (_selectAll) { draft.Clear(); _selectAll = false; }
            if (draft.Length + length <= MaximumLength)
            {
                draft.Append(character);
                if (length == 2) draft.Append(input[index + 1]);
            }
            if (length == 2) index++;
        }
        NicknamePreset.NameInput = draft.ToString();
    }

    public static void StopEditing()
    {
        IsEditing = false;
        _selectAll = false;
        _lastInputFrame = -1;
    }

    public static void SuppressMovement(PlayerPhysics physics)
    {
        if (!IsEditing || !MenuUI.isGUIActive || MenuUI.ActiveTabName != "Fun" ||
            !Application.isFocused || MalumMenu.isPanicked) return;
        try
        {
            var local = PlayerControl.LocalPlayer;
            if (local && local.AmOwner && local.MyPhysics && physics && physics.AmOwner &&
                physics.myPlayer && physics.myPlayer.AmOwner &&
                physics.Pointer == local.MyPhysics.Pointer && physics.myPlayer.Pointer == local.Pointer)
                physics.SetNormalizedVelocity(Vector2.zero);
        }
        catch (Exception) { StopEditing(); }
    }

    private static void PasteClipboard()
    {
        // Called only by the explicit Paste button or Ctrl+V while editing.
        var clipboard = GUIUtility.systemCopyBuffer;
        var previous = _selectAll ? "" : NicknamePreset.NameInput ?? "";
        NicknamePreset.NameInput = Filter(previous + (clipboard ?? ""));
        _selectAll = false;
    }

    private static string Filter(string text)
    {
        var filtered = new StringBuilder(Math.Min(text.Length, MaximumLength));
        for (var index = 0; index < text.Length && filtered.Length < MaximumLength; index++)
        {
            var character = text[index];
            if (!Allowed(character) || char.IsLowSurrogate(character)) continue;
            if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= text.Length || !char.IsLowSurrogate(text[index + 1])) continue;
                if (filtered.Length + 2 > MaximumLength) break;
                filtered.Append(character).Append(text[++index]);
            }
            else filtered.Append(character);
        }
        return filtered.ToString();
    }

    private static bool Allowed(char character) =>
        !char.IsControl(character) && character is not '\u2028' and not '\u2029';

    private static void RemoveLast(StringBuilder text)
    {
        if (text.Length == 0) return;
        var count = char.IsLowSurrogate(text[text.Length - 1]) && text.Length >= 2 &&
            char.IsHighSurrogate(text[text.Length - 2]) ? 2 : 1;
        text.Length -= count;
    }

    private static string Preview(string text)
    {
        if (text.Length <= PreviewLength) return text;
        var end = PreviewLength;
        if (char.IsHighSurrogate(text[end - 1])) end--;
        return text.Substring(0, end) + "...";
    }
}
