using UnityModManagerNet;

namespace DRF;

public static class Main
{
    internal static Settings Settings { get; private set; } = null!;

    internal static UnityModManager.ModEntry.ModLogger Logger { get; private set; } = null!;

    private static UnityModManager.ModEntry _entry = null!;

    internal static void SaveSettings() => Settings.Save(_entry);

    private static bool Load(UnityModManager.ModEntry modEntry)
    {
        _entry = modEntry;
        Logger = modEntry.Logger;
        Settings = UnityModManager.ModSettings.Load<Settings>(modEntry);

        modEntry.OnGUI = SettingsGUI.OnGUI;
        modEntry.OnSaveGUI = entry =>
        {
            Settings.Save(entry);
        };
        return true;
    }
}
