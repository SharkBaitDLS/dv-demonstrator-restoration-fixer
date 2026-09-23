using System;
using System.Collections.Generic;
using System.Linq;
using DV.UserManagement;
using DV.UserManagement.Data;
using DV.Utils;

namespace DRF.Saves;

internal static class SaveCatalog
{
    private const string SavesFolder = "Saves";
    private const string BackupPattern = "*.bak";

    private static List<SaveSource>? _cached;

    internal static void Invalidate() => _cached = null;

    internal static IReadOnlyList<SaveSource> All()
    {
        if (_cached != null) return _cached;
        _cached = Enumerate().OrderByDescending(s => s.Timestamp).ToList();
        return _cached;
    }

    internal static SaveSource? Find(string? key) =>
        string.IsNullOrEmpty(key) ? null : All().FirstOrDefault(s => s.Key == key);

    // Only the loaded session's own saves, we're not in the business of parallel universes, only time travel
    private static IEnumerable<SaveSource> Enumerate()
    {
        var manager = SingletonBehaviour<UserManager>.Instance;
        var session = manager != null ? manager.CurrentUser?.CurrentSession : null;
        if (manager == null || session == null) yield break;

        foreach (var save in session.Saves)
        {
            SaveSource? source = null;
            try
            {
                source = SaveSource.ForSnapshot(save);
            }
            catch (Exception ex)
            {
                Main.Logger.Warning($"Skipping an unreadable save in '{session.Name}': {ex.Message}");
            }
            if (source != null) yield return source;
        }

        if (session is not GameSession concrete) yield break;
        foreach (var backup in Backups(manager, concrete)) yield return backup;
    }

    private static IEnumerable<SaveSource> Backups(UserManager manager, GameSession session)
    {
        var directory = session.BasePath + "/" + SavesFolder;
        List<string> files;
        try
        {
            files = manager.Storage.ListFiles(directory, BackupPattern);
        }
        catch (Exception ex)
        {
            Main.Logger.Warning($"Couldn't list backups in '{directory}': {ex.Message}");
            yield break;
        }

        foreach (var file in files)
        {
            var path = directory + "/" + file;
            DateTime written;
            try
            {
                written = manager.Storage.GetLastWriteTime(path);
            }
            catch (Exception ex)
            {
                Main.Logger.Warning($"Couldn't read the timestamp of '{path}': {ex.Message}");
                continue;
            }
            yield return SaveSource.ForBackup(session, path, file, written);
        }
    }
}
