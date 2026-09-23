using System.Linq;
using DRF.Saves;
using UnityEngine;

namespace DRF.Gui;

internal static class SaveListView
{
    private const float ListHeight = 190f;

    private static Vector2 _scroll;
    private static string _search = "";

    internal static string? Draw(string? selectedKey)
    {
        GUILayout.BeginVertical(GUI.skin.box);

        GUILayout.BeginHorizontal();
        GUILayout.Label("Search:", GUILayout.Width(50));
        var search = GUILayout.TextField(_search, GUILayout.ExpandWidth(true));
        if (search != _search)
        {
            _search = search;
            _scroll = Vector2.zero; // a freshly filtered list scrolled halfway down reads as empty
        }
        if (GUILayout.Button("Refresh", GUILayout.Width(90)))
        {
            SaveCatalog.Invalidate();
            SettingsGUI.ForgetCachedSurveys();
        }
        GUILayout.EndHorizontal();

        var saves = SaveCatalog.All();
        if (saves.Count == 0)
        {
            GUILayout.Label("No saves found for this session.");
            GUILayout.EndVertical();
            return selectedKey;
        }

        var result = selectedKey;
        _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(ListHeight));
        foreach (var save in saves.Where(Matches))
        {
            var chosen = save.Key == selectedKey;
            var label = (chosen ? "▸ " : "   ") + save.Describe();
            if (GUILayout.Button(label, chosen ? Selected : GUI.skin.button, GUILayout.ExpandWidth(true)))
                result = chosen ? null : save.Key;
        }
        GUILayout.EndScrollView();

        GUILayout.EndVertical();
        return result;
    }

    private static bool Matches(SaveSource save)
    {
        if (_search.Length == 0) return true;
        var needle = _search.ToLowerInvariant();
        return save.Name.ToLowerInvariant().Contains(needle)
            || save.Timestamp.ToString("yyyy-MM-dd HH:mm").Contains(needle);
    }

    private static GUIStyle? _selected;

    private static GUIStyle Selected
    {
        get
        {
            if (_selected != null) return _selected;
            _selected = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
            return _selected;
        }
    }
}
