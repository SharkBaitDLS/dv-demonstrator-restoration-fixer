using System;
using System.Collections.Generic;
using System.Linq;
using DRF.Model;
using DRF.Saves;
using DRF.World;
using DV.JObjectExtstensions;
using DV.LocoRestoration;
using DV.ThingTypes;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DRF.Restore;

// Moves a demonstrator whose cars are still in the world on to a later quest state, keeping those cars.
internal static class ManualAdvance
{
    private const int Rerailed = (int)LocoRestorationController.RestorationState.S3_RerailedCars;
    private const int PartInstalled = (int)LocoRestorationController.RestorationState.S8_PartInstalled;
    private const int Serviced = (int)LocoRestorationController.RestorationState.S9_LocoServiced;

    private static readonly System.Reflection.FieldInfo? LoadingDoneField =
        AccessTools.Field(typeof(LocoRestorationController), "loadingDone");

    internal static RestoreOutcome Apply(string saveId, int wanted)
    {
        var outcome = new RestoreOutcome { ByHand = true };

        var controller = LocoRestorationController.allLocoRestorationControllers
            .FirstOrDefault(c => c != null && c.SaveID == saveId);
        if (controller == null)
        {
            outcome.Warn($"'{saveId}' has no restoration in this world, nothing was changed.");
            return outcome;
        }

        var condition = DemonstratorCondition.Read(controller);
        if (condition == null)
        {
            outcome.Warn($"'{saveId}' has no cars in the world to move forward, so restore it from a save "
                + "instead.");
            return outcome;
        }

        if (!condition.Choices.Contains(wanted))
        {
            outcome.Warn($"'{saveId}' can't go from {RestorationStates.Describe(condition.State)} to "
                + $"{RestorationStates.Describe(wanted)} while it is {condition.Describe()}, nothing was changed.");
            return outcome;
        }

        // Quest popups for every step it skips past would only be noise.
        var loadingDone = LoadingDoneField?.GetValue(controller);
        LoadingDoneField?.SetValue(controller, false);
        try
        {
            if (wanted < Rerailed) Unblock(condition, wanted);
            else Reload(condition, wanted, outcome);
        }
        catch (Exception ex)
        {
            Main.Logger.LogException($"Moving '{saveId}' forward failed:", ex);
            outcome.Warn($"Moving '{saveId}' forward failed partway through. See the log; this demonstrator "
                + "may need the save reloaded to settle.");
            outcome.Failed = true;
            return outcome;
        }
        finally
        {
            LoadingDoneField?.SetValue(controller, loadingDone ?? true);
        }

        outcome.Demonstrators++;
        CommsRadio.Refresh();

        var settled = (int)controller.State;
        if (settled == wanted)
        {
            outcome.Note($"Moved '{saveId}' forward to {RestorationStates.Describe(settled)}.");
        }
        else
        {
            outcome.Warn($"'{saveId}' settled at {RestorationStates.Describe(settled)} rather than "
                + $"{RestorationStates.Describe(wanted)}; see the log for what the game made of it.");
        }
        return outcome;
    }

    internal static RestoreOutcome RevokeGarage(string saveId)
    {
        var outcome = new RestoreOutcome { ByHand = true };

        var controller = LocoRestorationController.allLocoRestorationControllers
            .FirstOrDefault(c => c != null && c.SaveID == saveId);
        if (controller == null || !WorldState.GarageUnlockedEarly(controller))
        {
            outcome.Warn($"'{saveId}' has no garage unlocked ahead of its quest, nothing was changed.");
            return outcome;
        }

        GarageState.Revoke(controller.garageSpawner.garageType,
            $"{saveId} is only at {RestorationStates.Describe((int)controller.State)}.");
        GarageState.StopSpawning(controller.garageSpawner);
        outcome.Note($"Revoked the garage for '{saveId}', which it hasn't earned yet at "
            + $"{RestorationStates.Describe((int)controller.State)}. It unlocks again once it is repaired.");
        return outcome;
    }

    // A wreck is still waiting on its blockers, so it's walked through the same steps the licenses would
    // have taken it through.
    private static void Unblock(DemonstratorCondition condition, int wanted)
    {
        var controller = condition.Controller;
        if (wanted == (int)LocoRestorationController.RestorationState.S1_UnlockedRestorationLicense)
        {
            AccessTools.Method(typeof(LocoRestorationController), "SetState")
                .Invoke(controller, [LocoRestorationController.RestorationState.S1_UnlockedRestorationLicense]);
            return;
        }

        // The blockers stay up until the licenses are bought, but they must not take the quest back to S2
        // when they eventually come down.
        var unblocked = CarLifecycle.DelegateFor<Action>(controller, "OnBlockersRemovedWrapper");
        foreach (var blocker in condition.Cars.SelectMany(CarLifecycle.BlockersFor))
            blocker.Unblocked -= unblocked;

        AccessTools.Method(typeof(LocoRestorationController), "OnBlockersRemoved", [typeof(bool)])
            .Invoke(controller, [true]);
    }

    private static void Reload(DemonstratorCondition condition, int wanted, RestoreOutcome outcome)
    {
        var controller = condition.Controller;
        var entry = Traverse.Create(controller).Field("saveData").GetValue<JObject>()?.DeepClone() as JObject
            ?? [];
        entry.SetInt(SaveKeys.RestorationState, wanted);
        entry.SetString(SaveKeys.RestorationLoco, condition.Loco.CarGUID);
        if (condition.SecondCar != null) entry.SetString(SaveKeys.RestorationSecondCar, condition.SecondCar.CarGUID);
        entry.Remove(SaveKeys.RestorationTransportingCars);

        ControllerTeardown.Release(controller, outcome);

        // Paying for the part installation is what brings a steam loco's boiler back into working order.
        var sim = condition.Loco.SimController;
        if (condition.State < PartInstalled && wanted >= PartInstalled
            && CarTypes.IsSteamLocomotive(condition.Loco.carLivery) && sim != null && sim.portsOverrider != null)
        {
            sim.portsOverrider.BoilerSpecialRequest(2f);
        }

        var lent = LendBlockers(condition.Cars);
        try
        {
            controller.LoadData(entry);
        }
        finally
        {
            ReturnBlockers(lent);
        }

        // LoadData expects serviced cars to come out of a save already free of the restoration's limits, as
        // they would be once the service check has passed, but these are still held down from earlier states.
        if (wanted >= Serviced)
        {
            foreach (var car in condition.Cars) Free(car);
        }

        LiveRestore.ReconcileGarage(controller, condition.Cars.ToDictionary(c => c.CarGUID, c => c), outcome);
    }

    private static List<(LocoZoneBlocker Blocker, TrainCar Car)> LendBlockers(IEnumerable<TrainCar> cars)
    {
        var lent = cars.SelectMany(car => CarLifecycle.BlockersFor(car).Take(1).Select(b => (b, car))).ToList();
        foreach (var (blocker, car) in lent) blocker.transform.SetParent(car.transform, worldPositionStays: true);
        return lent;
    }

    private static void ReturnBlockers(List<(LocoZoneBlocker Blocker, TrainCar Car)> lent)
    {
        foreach (var (blocker, car) in lent)
        {
            if (blocker == null || car == null) continue;
            blocker.transform.SetParent(car.interior, worldPositionStays: false);
            blocker.transform.localPosition = Vector3.zero;
            blocker.transform.localRotation = Quaternion.identity;
        }
    }

    private static void Free(TrainCar car)
    {
        car.preventDelete = false;
        car.preventDebtDisplay = false;
        car.preventFastTravelWithCar = false;
        car.preventFastTravelDestination = false;
        car.preventRerail = false;
        car.preventCouple = false;
        car.preventService = false;

        if (car.FastTravelDestination == null) return;
        car.FastTravelDestination.showOnMap = true;
        car.FastTravelDestination.RefreshMarkerVisibility();
    }
}
