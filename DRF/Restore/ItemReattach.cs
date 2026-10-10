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

        var gadgets = MissingGadgets(source, cars, outcome);
        var looseItems = MissingLooseItems(source, cars, outcome);

        var available = LostAndFound();

        var installed = new List<ItemSaveData>();
        foreach (var saved in gadgets)
        {
            if (!TryClaim(available, saved.itemPrefabName, out var item)) { outcome.ItemsUnaccounted++; continue; }

            if (Install(item!, saved.state) is { } save) installed.Add(save);
            else outcome.ItemsUnaccounted++;
        }

        // Only triggered after every gadget is in place because a mount looks up the gadget sitting
        // on it, and finds nothing if that gadget hasn't been installed yet.
        foreach (var save in installed) save.PostLoadItemData();
        outcome.ItemsReattached += installed.Count;

        foreach (var (saved, car) in looseItems)
        {
            if (!TryClaim(available, saved.itemPrefabName, out var item)) { outcome.ItemsUnaccounted++; continue; }

            if (PutInCab(item!, car, saved)) outcome.ItemsReattached++;
            else outcome.ItemsUnaccounted++;
        }
    }

    private static List<StorageItemData> MissingGadgets(SaveGameData source,
        IReadOnlyDictionary<string, TrainCar> cars, RestoreOutcome outcome)
    {
        var missing = new List<StorageItemData>();
        foreach (var saved in StorageLists.Read(source, SaveKeys.StorageInstalledGadgets))
        {
            var placedOn = saved.state?.GetValue(SaveKeys.GadgetPlacedOn)?.ToString();
            if (placedOn == null || !cars.TryGetValue(placedOn, out var car)) continue;

            var fitted = FittedWithUid(car, SavedUid(saved.state));
            if (fitted == null)
            {
                missing.Add(saved);
                continue;
            }

            var fittedName = PrefabName(fitted);
            if (fittedName == saved.itemPrefabName)
            {
                outcome.ItemsAlreadyThere++;
                continue;
            }

            outcome.Warn($"Left a {saved.itemPrefabName} for {car.ID} in lost and found, because the "
                + $"{fittedName ?? "gadget"} fitted there since has taken over its wiring.");
        }
        return missing;
    }

    private static List<(StorageItemData Saved, TrainCar Car)> MissingLooseItems(SaveGameData source,
        IReadOnlyDictionary<string, TrainCar> cars, RestoreOutcome outcome)
    {
        var aboard = LooseItemsAboard(cars);
        var missing = new List<(StorageItemData, TrainCar)>();
        foreach (var saved in StorageLists.Read(source, SaveKeys.StorageWorld))
        {
            if (saved.carGuid == null || !cars.TryGetValue(saved.carGuid, out var car)) continue;

            var key = (saved.carGuid, saved.itemPrefabName);
            if (aboard.TryGetValue(key, out var count) && count > 0)
            {
                aboard[key] = count - 1;
                outcome.ItemsAlreadyThere++;
                continue;
            }
            missing.Add((saved, car));
        }
        return missing;
    }

    private static int? SavedUid(JObject? state) =>
        (state?[SaveKeys.GadgetData] as JObject)?[SaveKeys.GadgetUid]?.Value<int?>();

    private static GadgetBase? FittedWithUid(TrainCar car, int? uid)
    {
        // A uid of 0 is one the game never assigned
        if (uid is not { } id || id == 0 || car.Customization == null) return null;
        return car.Customization.Customizers.OfType<GadgetBase>().FirstOrDefault(g => g != null && g.UID == id);
    }

    private static Dictionary<(string Guid, string Prefab), int> LooseItemsAboard(
        IReadOnlyDictionary<string, TrainCar> cars)
    {
        var result = new Dictionary<(string, string), int>();
        var controller = SingletonBehaviour<StorageController>.Instance;
        var storage = controller != null ? controller.StorageWorld : null;
        if (storage == null) return result;

        foreach (var item in storage.GetStorageItemList().Where(i => i != null))
        {
            var car = TrainCar.Resolve(item.gameObject);
            if (car == null || !cars.ContainsKey(car.CarGUID)) continue;
            if (PrefabName(item) is not { } name) continue;

            var key = (car.CarGUID, name);
            result[key] = result.TryGetValue(key, out var count) ? count + 1 : 1;
        }
        return result;
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
            if (PrefabName(item) is not { } name) continue;

            if (!result.TryGetValue(name, out var list)) result[name] = list = [];
            list.Add(item);
        }
        return result;
    }

    private static string? PrefabName(ItemBase item) =>
        item.InventorySpecs != null && !string.IsNullOrEmpty(item.InventorySpecs.ItemPrefabName)
            ? item.InventorySpecs.ItemPrefabName
            : null;

    private static string? PrefabName(GadgetBase gadget) =>
        gadget.GadgetItem != null && gadget.GadgetItem.TryGetComponent<InventoryItemSpec>(out var spec)
            && !string.IsNullOrEmpty(spec.ItemPrefabName)
            ? spec.ItemPrefabName
            : null;
}
