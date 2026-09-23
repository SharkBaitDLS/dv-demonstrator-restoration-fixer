using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DV.Customization.Gadgets;
using DV.JObjectExtstensions;
using DV.Logic.Job;
using DV.ServicePenalty;
using DV.ThingTypes;
using DV.Utils;
using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace DRF.World;

internal static class CarLifecycle
{
    private static readonly FieldInfo? BlockerTrainField =
        AccessTools.Field(typeof(LocoZoneBlocker), "train");

    internal static IEnumerable<LocoZoneBlocker> BlockersFor(TrainCar? car)
    {
        if (car == null) yield break;

        foreach (var blocker in UnityEngine.Object.FindObjectsOfType<LocoZoneBlocker>())
            if (BlockerTrainField?.GetValue(blocker) as TrainCar == car) yield return blocker;
    }

    internal static void DestroyStaleBlockers(TrainCar? car)
    {
        if (car == null) return;

        foreach (var blocker in BlockersFor(car).ToList())
        {
            Main.Logger.Log($"Destroying the zone blocker for {car.name} [{car.ID}] along with the car.");
            if (blocker.blockerObjectsParent != null)
                UnityEngine.Object.Destroy(blocker.blockerObjectsParent);
            UnityEngine.Object.Destroy(blocker.gameObject);
        }
    }

    internal static void Delete(TrainCar car)
    {
        var owned = SingletonBehaviour<OwnedCarsStateController>.Instance;
        var before = owned != null
            ? new HashSet<StagedOwnedCarDebt>(owned.currentlyDestroyedOwnedCarStates)
            : null;

        SingletonBehaviour<CarSpawner>.Instance.DeleteCar(car);

        if (owned == null || before == null) return;

        var staged = owned.currentlyDestroyedOwnedCarStates.Where(d => !before.Contains(d)).ToList();
        if (staged.Count == 0) return;

        owned.currentlyDestroyedOwnedCarStates.RemoveAll(d => !before.Contains(d));
        owned.UpdateSortedList();

        Main.Logger.Log($"Dropped {staged.Count} owned-car fee(s) staged by removing this car, which nothing "
            + "would ever clear once the restored locomotive takes its own ID back: "
            + string.Join(", ", staged.Select(d => d.ID)));
    }

    internal static int SweepGadgetsToLostAndFound(TrainCar car)
    {
        var storage = SingletonBehaviour<StorageController>.Instance;
        var custom = car.Customization;
        if (storage == null || custom == null) return 0;

        var gadgets = custom.Customizers.OfType<GadgetBase>().Where(g => g != null).ToList();
        var items = gadgets.Select(g => g.GadgetItem != null ? g.GadgetItem.Item : null).Where(i => i != null).ToList();
        if (items.Count == 0) return 0;

        foreach (var gadget in gadgets)
        {
            if (gadget.IsLinked) gadget.ForceRemove(reparentToTrainCar: false);
        }

        var swept = 0;
        foreach (var item in items)
        {
            if (item == null || storage.IsInStorageLostAndFound(item)) continue;

            storage.AddItemToLostAndFound(item);
            // Items waiting in lost and found are kept inactive until the player walks up to it.
            if (!storage.StorageLostAndFound.itemsActiveOrActivating) item.gameObject.SetActive(false);
            swept++;
        }
        return swept;
    }

    // The game stashes a deleted unique car's state under its livery and reserves its number, ready
    // for the next car spawned of that livery to reclaim both, but only a car spawned through the game's own
    // BaseSpawn ever reclaims it. Putting a car back directly by its number bypasses that, so the stash has
    // to be dropped by hand on both sides of a restore.
    internal static string? ForgetDeletedUnique(TrainCarLivery? livery)
    {
        var spawner = SingletonBehaviour<CarSpawner>.Instance;
        if (livery == null
            || AccessTools.Field(typeof(CarSpawner), "deletedUniqueCarLiveryToLastCarState")
                ?.GetValue(spawner) is not IDictionary deleted
            || !deleted.Contains(livery))
        {
            return null;
        }

        var stashedId = (deleted[livery] as JObject)?.GetString("id");
        deleted.Remove(livery);
        if (stashedId != null) SingletonBehaviour<IdGenerator>.Instance.UnReserveCarId(stashedId);
        return stashedId;
    }

    // Rebuilds a delegate equal to one the game subscribed itself, so it can be unsubscribed by value.
    internal static T DelegateFor<T>(object target, string method) where T : Delegate =>
        (T)Delegate.CreateDelegate(typeof(T), target, AccessTools.Method(target.GetType(), method));
}
