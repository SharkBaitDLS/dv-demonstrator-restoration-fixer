using System.Collections.Generic;
using System.Linq;
using DRF.Saves;
using DV.CabControls;
using DV.Customization.Gadgets;
using DV.Utils;
using Newtonsoft.Json.Linq;

namespace DRF.Restore;

internal static class ItemReattach
{
    internal static void Apply(SaveGameData source, IReadOnlyDictionary<string, TrainCar> cars,
        RestoreOutcome outcome)
    {
        if (cars.Count == 0) return;

        var available = LostAndFound();
        if (available.Count == 0)
        {
            CountUnclaimed(source, cars, outcome);
            return;
        }

        var installed = new List<ItemSaveData>();
        foreach (var saved in StorageLists.Read(source, SaveKeys.StorageInstalledGadgets))
        {
            var placedOn = saved.state?.GetValue(SaveKeys.GadgetPlacedOn)?.ToString();
            if (placedOn == null || !cars.ContainsKey(placedOn)) continue;
            if (!TryClaim(available, saved.itemPrefabName, out var item)) { outcome.ItemsUnaccounted++; continue; }

            if (Install(item!, saved.state) is { } save) installed.Add(save);
            else outcome.ItemsUnaccounted++;
        }

        // Only triggered after every gadget is in place because a mount looks up the gadget sitting
        // on it, and finds nothing if that gadget hasn't been installed yet.
        foreach (var save in installed) save.PostLoadItemData();
        outcome.ItemsReattached += installed.Count;

        foreach (var saved in StorageLists.Read(source, SaveKeys.StorageWorld))
        {
            if (saved.carGuid == null || !cars.TryGetValue(saved.carGuid, out var car)) continue;
            if (!TryClaim(available, saved.itemPrefabName, out var item)) { outcome.ItemsUnaccounted++; continue; }

            if (PutInCab(item!, car, saved)) outcome.ItemsReattached++;
            else outcome.ItemsUnaccounted++;
        }
    }

    private static ItemSaveData? Install(ItemBase item, JObject? state)
    {
        if (state == null) return null;

        if (!item.TryGetComponent<ItemSaveData>(out var save)) return null;

        SingletonBehaviour<StorageController>.Instance.RemoveItemFromLostAndFound(item);
        save.LoadItemData(state);

        if (IsInstalled(item)) return save;

        // Back to the shed you go if you can't find your home
        SingletonBehaviour<StorageController>.Instance.AddItemToLostAndFound(item);
        return null;
    }

    // Loose cab items are saved relative to the car's interior transform
    private static bool PutInCab(ItemBase item, TrainCar car, StorageItemData saved)
    {
        if (car.interior == null) return false;

        var position = car.interior.TransformPoint(saved.ItemPosition);
        var rotation = car.interior.rotation * saved.ItemRotation;

        StorageController.RemoveItemFromCurrentStorageAndAddToWorld(item, position, rotation);
        if (item.TryGetComponent<ItemReparentingBase>(out var reparenting))
            reparenting.ParentItemExternal(car.interior, car.rb);
        return true;
    }

    private static bool IsInstalled(ItemBase item) =>
        item.TryGetComponent<GadgetItem>(out var gadget) && gadget.Gadget != null && gadget.Gadget.Custom != null;

    private static bool TryClaim(Dictionary<string, List<ItemBase>> available, string prefabName,
        out ItemBase? item)
    {
        item = null;
        if (!available.TryGetValue(prefabName, out var candidates) || candidates.Count == 0) return false;

        item = candidates[candidates.Count - 1];
        candidates.RemoveAt(candidates.Count - 1);
        return true;
    }

    // Items in lost and found that aren't attached to anything, grouped by what they are. A gadget that has
    // been reinstalled on something else is deliberately not a candidate.
    private static Dictionary<string, List<ItemBase>> LostAndFound()
    {
        var result = new Dictionary<string, List<ItemBase>>();
        var controller = SingletonBehaviour<StorageController>.Instance;
        var storage = controller != null ? controller.StorageLostAndFound : null;
        if (storage == null) return result;

        foreach (var item in storage.GetStorageItemList().Where(i => i != null))
        {
            if (IsInstalled(item)) continue;

            var name = item.InventorySpecs != null ? item.InventorySpecs.ItemPrefabName : null;
            if (string.IsNullOrEmpty(name)) continue;


            if (!result.TryGetValue(name!, out var list)) result[name!] = list = [];
            list.Add(item);
        }
        return result;
    }

    private static void CountUnclaimed(SaveGameData source, IReadOnlyDictionary<string, TrainCar> cars,
        RestoreOutcome outcome)
    {
        outcome.ItemsUnaccounted += StorageLists.Read(source, SaveKeys.StorageInstalledGadgets)
            .Count(i => i.state?.GetValue(SaveKeys.GadgetPlacedOn)?.ToString() is { } on && cars.ContainsKey(on));
        outcome.ItemsUnaccounted += StorageLists.Read(source, SaveKeys.StorageWorld)
            .Count(i => i.carGuid != null && cars.ContainsKey(i.carGuid));
    }
}
