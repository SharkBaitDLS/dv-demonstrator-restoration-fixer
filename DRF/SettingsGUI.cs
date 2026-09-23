using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DRF.Gui;
using DRF.Model;
using DRF.Restore;
using DRF.Saves;
using DV.UserManagement;
using DV.Utils;
using UnityEngine;
using UnityModManagerNet;

namespace DRF;

internal static class SettingsGUI
{
    private const string IntroText =
        """
        Something happened that caused your demonstrators to reset or be lost?

        Pick a save from before that happened and DRF will bring them back to the present exactly as they were (or on a nearby track if the exact spot can't be used).

        Optionally, if you are using Custom Demonstrators and wish to bring a custom demonstrator forward without overridding a different one that has been restored in its slot, DRF can assign the recovered demonstrator to a new slot in the museum if one is available.
        """;

    private const float ButtonWidth = 420f;
    private const float FieldLabelWidth = 220f;

    private static string? _selectedKey;
    private static string? _surveyedKey;
    private static RestorationSurvey? _sourceSurvey;
    private static string? _sourceError;

    private static RestorationSurvey? _currentSurvey;
    private static string _currentLabel = "";
    private static string? _currentError;

    private static readonly HashSet<string> Chosen = [];

    // The subset of Chosen that is going into a slot of its own rather than the one it came from.
    private static readonly HashSet<string> AsNewSlots = [];
    private static string? _chosenFor;

    private static string _searchRangeText = "";

    private static RestoreOutcome? _lastOutcome;
    private static string _lastOutcomeLabel = "";

    internal static void ForgetCachedSurveys()
    {
        _surveyedKey = null;
        _sourceSurvey = null;
        _sourceError = null;
        _currentSurvey = null;
        _currentError = null;
        _chosenFor = null;
    }

    internal static void OnGUI(UnityModManager.ModEntry entry)
    {
        var users = SingletonBehaviour<UserManager>.Instance;
        if (users == null || users.CurrentUser == null)
        {
            GUILayout.Label("Waiting for a profile to load...");
            return;
        }

        var current = CurrentSurvey();

        GUILayout.Label(IntroText, GUILayout.ExpandWidth(true));
        GUILayout.Space(6);

        DrawLastOutcome();
        DrawOptions();

        GUILayout.Space(6);
        GUILayout.Label("Restoring into", Bold);
        if (current == null)
        {
            GUILayout.Label(_currentError ?? "No save is active, nowhere to restore to.");
            return;
        }
        GUILayout.Label($"{_currentLabel} — {ComparisonView.Summarize(current)}");
        GUILayout.Label($"Railway: {ShortHash(current.TracksHash)}    Saved by: {current.GameVersion ?? "unknown"}");
        DrawWorldNotice();

        GUILayout.Space(8);
        GUILayout.Label("Restore from", Bold);
        var picked = SaveListView.Draw(_selectedKey);
        if (picked != _selectedKey)
        {
            _selectedKey = picked;
            _surveyedKey = null;
            _chosenFor = null;
        }

        var source = SourceSurvey();
        if (source == null)
        {
            if (_sourceError != null) GUILayout.Label(_sourceError);
            else if (_selectedKey == null) GUILayout.Label("Pick a save above to compare it with this one.");
            return;
        }

        GUILayout.Space(6);
        GUILayout.Label($"Railway: {ShortHash(source.TracksHash)}    Saved by: {source.GameVersion ?? "unknown"}"
            + $"    Custom Demonstrators: {(source.HasCustomDemonstratorData ? "recorded" : "not recorded")}");
        GUILayout.Space(6);

        var plan = RestorePlan.Build(current, source);
        SyncChosen(plan);
        ComparisonView.Draw(plan, Chosen, AsNewSlots);

        GUILayout.Space(8);
        DrawRestoreButton(source);
    }

    private static void DrawWorldNotice()
    {
        if (!WorldState.IsInGame) return;

        var world = WorldState.Read();
        var lost = world.Values.Count(d => !d.HasLoco);
        var reset = world.Count(d => d.Value.State == 0 && WasFurtherAlong(d.Key));
        if (lost == 0 && reset == 0) return;

        GUILayout.BeginVertical(GUI.skin.box);
        if (lost > 0)
            GUILayout.Label($"{lost} demonstrator(s) in were not recovered successfully.");
        if (reset > 0)
            GUILayout.Label($"{reset} demonstrator(s) have been reset to a fresh wreck.");
        GUILayout.EndVertical();
        GUILayout.Space(6);
    }

    private static bool WasFurtherAlong(string saveId) =>
        _currentSurvey?.Demonstrators.TryGetValue(saveId, out var record) == true && record.State > 0;

    private static void DrawLastOutcome()
    {
        if (_lastOutcome == null) return;

        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label($"Last restore, from {_lastOutcomeLabel}:", Bold);
        GUILayout.Label(_lastOutcome.Summary());
        foreach (var note in _lastOutcome.Notes) GUILayout.Label("• " + note, GUILayout.ExpandWidth(true));
        GUILayout.Label("If this isn't what you wanted, quit to the menu without saving or reload an earlier save.");
        if (GUILayout.Button("Dismiss", GUILayout.Width(120))) _lastOutcome = null;
        GUILayout.EndVertical();
        GUILayout.Space(6);
    }

    private static void DrawOptions()
    {
        var settings = Main.Settings;

        GUILayout.BeginHorizontal();
        GUILayout.Label("Relocation search range (m):", GUILayout.Width(FieldLabelWidth));
        if (_searchRangeText.Length == 0)
            _searchRangeText = settings.SearchRangeMeters.ToString(CultureInfo.InvariantCulture);
        _searchRangeText = GUILayout.TextField(_searchRangeText, GUILayout.Width(90));
        if (float.TryParse(_searchRangeText, NumberStyles.Float, CultureInfo.InvariantCulture, out var range)
            && range > 0f)
        {
            settings.SearchRangeMeters = range;
        }
        GUILayout.Label("If the exact location of the locomotive can't be used, how far should the mod search "
            + "for a track to place it on?", GUILayout.ExpandWidth(true));
        GUILayout.EndHorizontal();
    }

    private static void DrawRestoreButton(RestorationSurvey source)
    {
        if (!WorldState.IsInGame)
        {
            GUILayout.Label("Load a save to begin restoration.");
            return;
        }

        var enabled = GUI.enabled;
        GUI.enabled = Chosen.Count > 0;
        if (GUILayout.Button($"Restore {Chosen.Count} demonstrator(s) now", GUILayout.Width(ButtonWidth)))
            Restore(source);
        GUI.enabled = enabled;

        GUILayout.Label("Existing demonstrators in their slots will be destroyed and the rescued ones will be "
        + "placed at the same restoration state and customization as they were in their source savegame.");
    }

    private static void Restore(RestorationSurvey source)
    {
        _lastOutcomeLabel = SaveCatalog.Find(_selectedKey)?.Describe() ?? "an earlier save";
        Main.Logger.Log($"Restoring {string.Join(", ", Chosen)} from {_lastOutcomeLabel}.");

        _lastOutcome = LiveRestore.Apply(source, [.. Chosen], [.. AsNewSlots]);
        Main.SaveSettings();

        // The world has moved on from what the comparison above was built from.
        _currentSurvey = null;
        _chosenFor = null;
    }

    private static SaveGameData? _surveyedSave;

    private static RestorationSurvey? CurrentSurvey()
    {
        var live = LiveSave.Data();
        if (live == null)
        {
            _surveyedSave = null;
            _currentSurvey = null;
            return null;
        }

        if (!ReferenceEquals(live, _surveyedSave))
        {
            _surveyedSave = live;
            SaveCatalog.Invalidate();
            ForgetCachedSurveys();
            _lastOutcome = null;
            _selectedKey = null;
        }

        if (_currentSurvey != null) return _currentSurvey;

        _currentError = null;
        _currentLabel = "The save you are playing";
        return _currentSurvey = RestorationSurvey.Read(live);
    }

    private static RestorationSurvey? SourceSurvey()
    {
        if (_selectedKey == null) return null;
        if (_surveyedKey == _selectedKey) return _sourceSurvey;

        _surveyedKey = _selectedKey;
        _sourceSurvey = null;
        _sourceError = null;

        var source = SaveCatalog.Find(_selectedKey);
        if (source == null)
        {
            _sourceError = "That save is no longer there.";
            return null;
        }

        var data = source.Read(out var reason);
        if (data == null)
        {
            _sourceError = $"Couldn't read {source.Describe()}: {reason ?? "unknown error"}.";
            return null;
        }
        return _sourceSurvey = RestorationSurvey.Read(data);
    }

    private static void SyncChosen(RestorePlan plan)
    {
        if (_chosenFor == _selectedKey) return;
        _chosenFor = _selectedKey;
        Chosen.Clear();
        AsNewSlots.Clear();
        foreach (var id in plan.RecommendedIds) Chosen.Add(id);
    }

    private static string ShortHash(string? hash) =>
        string.IsNullOrEmpty(hash) ? "unknown" : hash!.Length <= 12 ? hash! : hash!.Substring(0, 12);

    private static GUIStyle? _bold;

    private static GUIStyle Bold
    {
        get
        {
            if (_bold != null) return _bold;
            _bold = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            return _bold;
        }
    }
}
