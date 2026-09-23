using System;
using System.Collections.Generic;
using System.Linq;
using DRF.World;
using DV.Logic.Job;
using DV.LocoRestoration;
using DV.PitStops;
using DV.Shops;
using DV.ThingTypes;
using HarmonyLib;

namespace DRF.Restore;

// Puts a LocoRestorationController back to the state it is in just before the game hands it a save.
internal static class ControllerTeardown
{
    private static readonly Type Controller = typeof(LocoRestorationController);

    internal static void Detach(LocoRestorationController controller, RestoreOutcome outcome)
    {
        var view = Traverse.Create(controller);
        var loco = view.Field("loco").GetValue<TrainCar>();
        var secondCar = view.Field("secondCar").GetValue<TrainCar>();

        // Unsubscribe from the cars before they are deleted, because deleting a demonstrator's locomotive
        // is exactly what OnUnexpectedDestroy watches to spawn a new wreck.
        controller.StopAllCoroutines();
        StopWatchingPaint(controller, loco);
        StopWatchingLicenses(controller, loco, secondCar);
        StopWatchingPurchases(controller);
        CancelPartDelivery(controller, view);
        ReleaseTransportingCars(controller, view, outcome);

        foreach (var car in new[] { loco, secondCar })
        {
            if (car == null) continue;
            StopWatchingCar(controller, car);
        }

        DeleteCars(controller, outcome, secondCar, loco);

        view.Field("loco").SetValue(null);
        view.Field("secondCar").SetValue(null);
        view.Field("transportingCars").SetValue(null);
        view.Field("locoPartDelivery").SetValue(null);
        view.Field("unexpectedDestroyHandlingCoro").SetValue(null);

        GarageState.ClearCars(controller.garageSpawner);
        GarageState.StopSpawning(controller.garageSpawner);

        FreeSpawnAnchors(controller);
    }

    private static void StopWatchingPaint(LocoRestorationController controller, TrainCar? loco)
    {
        if (loco == null || controller.State < LocoRestorationController.RestorationState.S9_LocoServiced) return;
        AccessTools.Method(Controller, "SetupListenersForPaintJob", [typeof(bool)])
            ?.Invoke(controller, [false]);
    }

    private static void StopWatchingLicenses(LocoRestorationController controller, TrainCar? loco,
        TrainCar? secondCar)
    {
        var manager = GarageState.Manager();
        if (manager != null)
        {
            manager.LicenseAcquired -= CarLifecycle.DelegateFor<Action<GeneralLicenseType_v2>>(
                controller, "OnRestorationLicenseAcquired");
        }

        var unblocked = CarLifecycle.DelegateFor<Action>(controller, "OnBlockersRemovedWrapper");
        foreach (var car in new[] { loco, secondCar })
            foreach (var blocker in CarLifecycle.BlockersFor(car))
                blocker.Unblocked -= unblocked;
    }

    private static void StopWatchingPurchases(LocoRestorationController controller)
    {
        Unsubscribe(controller.orderPartsModule, controller, "OnPartsOrdered");
        Unsubscribe(controller.installPartsModule, controller, "OnInstallPartsPaid");
        ClearCart(controller.orderPartsModule);
        ClearCart(controller.installPartsModule);
    }

    private static void Unsubscribe(GenericThingCashRegisterModule? module,
        LocoRestorationController controller, string method)
    {
        if (module == null) return;
        module.ThingBought -= CarLifecycle.DelegateFor<Action>(controller, method);
    }

    private static void ClearCart(GenericThingCashRegisterModule? module)
    {
        if (module == null) return;
        AccessTools.Method(module.GetType(), "SetUnitsToBuy", [typeof(float)])?.Invoke(module, [0f]);
    }

    // A part that was already ordered has a standing delivery against the warehouse. It is re-created from
    // the restored state if that state still wants one.
    private static void CancelPartDelivery(LocoRestorationController controller, Traverse view)
    {
        var delivery = view.Field("locoPartDelivery").GetValue<WarehouseSpecialDelivery>();
        if (delivery == null) return;

        PartsWarehouse(controller)?.RemoveSpecialDelivery(delivery);
        delivery.Processed -= CarLifecycle.DelegateFor<Action<List<Car>>>(controller, "OnOrderedPartLoadedCargo");
    }

    // Rather than take the risk of trying to reconcile a world state where the DM1U/flatcar are carrying cargoes,
    // just tear them down and let the quests roll back to S5. It's gonna be pretty damn rare that someone reloads
    // a save mid-transit of a parts order *and* makes a breaking change that would motivate them to use DRF while
    // doing so, and we can inconvenience that edge case of users slightly to avoid the risks associated with trying
    // to rebuild S6 states.
    private static void ReleaseTransportingCars(LocoRestorationController controller, Traverse view,
        RestoreOutcome outcome)
    {
        foreach (var car in view.Field("transportingCars").GetValue<List<TrainCar>>() ?? [])
        {
            if (car == null) continue;

            car.preventFastTravelWithCar = false;
            car.preventDelete = false;

            var logic = car.logicCar;
            if (logic == null || logic.CurrentCargoTypeInCar == CargoType.None) continue;

            logic.UnloadCargo(logic.LoadedCargoAmount, logic.CurrentCargoTypeInCar, PartsWarehouse(controller));
            outcome.Note($"{car.ID} was hauling this demonstrator's part, which no longer has a restoration "
                + "waiting for it, so it has gone back to the warehouse.");
        }
    }

    private static WarehouseMachine? PartsWarehouse(LocoRestorationController controller) =>
        controller.warehouseMachineForPartPickup != null ? controller.warehouseMachineForPartPickup.warehouseMachine : null;

    private static void StopWatchingCar(LocoRestorationController controller, TrainCar car)
    {
        car.OnDestroyCar -= CarLifecycle.DelegateFor<Action<TrainCar>>(controller, "OnUnexpectedDestroy");
        car.OnRerailed -= CarLifecycle.DelegateFor<Action>(controller, "OnRerailed");

        if (car.logicCar != null)
            car.logicCar.CurrentTrackChanged -= CarLifecycle.DelegateFor<Action>(controller, "OnTrackChanged");

        if (car.TryGetComponent<SimulatedCarPitStopParameters>(out var pitStop))
            pitStop.ParametersUpdated -= CarLifecycle.DelegateFor<Action>(controller, "OnServiceDone");

        var garage = controller.garageSpawner;
        if (garage != null)
            car.OnDestroyCar -= CarLifecycle.DelegateFor<Action<TrainCar>>(garage, "OnGarageCarDeleted");
    }

    // The tender goes first because deleting it cascades to the locomotive it is coupled to, but not the other
    // way round, as we learned in CD.
    private static void DeleteCars(LocoRestorationController controller, RestoreOutcome outcome,
        params TrainCar?[] cars)
    {
        foreach (var car in cars)
        {
            if (car == null) continue;

            var livery = car.carLivery;
            var id = car.ID;
            var name = car.name;

            CarLifecycle.DestroyStaleBlockers(car);
            if (CarLifecycle.SweepGadgetsToLostAndFound(car) is var swept and > 0)
                outcome.Note($"{swept} gadget(s) fitted to {id} went to lost and found rather than being left "
                    + "on the ground where it stood.");
            car.preventDelete = false;

            // Deleting a unique car stashes it under its livery and reserves its number, but it overwrites
            // whatever was stashed there before without releasing that one's number. That earlier one is
            // typically the very car being restored that got lost in the world update, so its number
            // would stay reserved while it is back in the world under it.
            if (CarLifecycle.ForgetDeletedUnique(livery) is string earlier)
                Main.Logger.Log($"Dropped the stashed state and number reservation for the earlier {earlier}.");

            CarLifecycle.Delete(car);
            if (CarLifecycle.ForgetDeletedUnique(livery) is string released)
                Main.Logger.Log($"Dropped the stashed state and number reservation for the removed {released}.");

            outcome.CarsRemoved++;
            Main.Logger.Log($"Removed {name} [{id}], the car {controller.SaveID} had been given.");
        }
    }

    private const float AnchorOccupiedRadius = 20f;

    private static void FreeSpawnAnchors(LocoRestorationController ignoring)
    {
        var anchors = new HashSet<LocoRestorationSpawnPoint>();
        var occupied = new HashSet<LocoRestorationSpawnPoint>();

        foreach (var controller in LocoRestorationController.allLocoRestorationControllers)
        {
            if (controller == null || controller.spawnPoints == null) continue;
            foreach (var anchor in controller.spawnPoints.Where(a => a != null)) anchors.Add(anchor);
            if (controller == ignoring) continue;

            var view = Traverse.Create(controller);
            var cars = new[]
            {
                view.Field("loco").GetValue<TrainCar>(),
                view.Field("secondCar").GetValue<TrainCar>(),
            };

            foreach (var car in cars.Where(c => c != null))
            {
                foreach (var anchor in controller.spawnPoints.Where(a => a != null))
                {
                    if ((anchor.transform.position - car.transform.position).sqrMagnitude
                        < AnchorOccupiedRadius * AnchorOccupiedRadius)
                    {
                        occupied.Add(anchor);
                    }
                }
            }
        }

        foreach (var anchor in anchors) anchor.pointUsed = occupied.Contains(anchor);
    }
}
