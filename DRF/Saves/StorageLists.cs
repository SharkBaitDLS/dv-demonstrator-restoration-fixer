using System;
using System.Collections.Generic;

namespace DRF.Saves;

internal static class StorageLists
{
    internal static List<StorageItemData> Read(SaveGameData save, string key)
    {
        try
        {
            return save.GetObject<List<StorageItemData>>(key) ?? [];
        }
        catch (Exception ex)
        {
            Main.Logger.Warning($"Couldn't read '{key}' from a save: {ex.Message}");
            return [];
        }
    }

    internal static void Write(SaveGameData save, string key, List<StorageItemData> items) =>
        save.SetObject(key, items);
}
