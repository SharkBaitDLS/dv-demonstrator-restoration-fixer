using System;
using System.Collections.Generic;
using System.Linq;
using DRF.Model;
using DRF.Saves;
using DRF.World;
using DV.JObjectExtstensions;
using DV.Logic.Job;
using DV.LocoRestoration;
using DV.ThingTypes;
using DV.Utils;

namespace DRF.Restore;

internal static class PartsOrderReset
{
    private const LocoRestorationController.RestorationState OnOrder =
        LocoRestorationController.RestorationState.S5_PartOrdered;

    private const LocoRestorationController.RestorationState InTransit =
        LocoRestorationController.RestorationState.S6_PartPickedUp;

    internal readonly struct Survey(int orders, int strandedCars)
    {
        internal readonly int Orders = orders;

        internal readonly int StrandedCars = strandedCars;

        internal bool Any => Orders > 0 || StrandedCars > 0;
    }

    internal static Survey Read()
    {
        var orders = ActiveOrders().ToList();
        var tracked = new HashSet<TrainCar>(orders.SelectMany(o => WorldState.TransportingCars(o.Controller)));
        return new Survey(orders.Count, CarsCarryingParts().Count(car => !tracked.Contains(car)));
    }

    internal static RestoreOutcome Apply()
    {
        var outcome = new RestoreOutcome { ByHand = true };

        foreach (var condition in ActiveOrders().ToList()) Reorder(condition, outcome);

        foreach (var car in CarsCarryingParts().ToList())
        {
            CarLifecycle.ReturnCargo(car, WarehouseFor(car.logicCar.CurrentCargoTypeInCar));
            outcome.Note($"{car.ID} was carrying parts without any controller looking for them.");
        }

        if (outcome.Notes.Count == 0) outcome.Note("There were no parts orders or parts cargo to reset.");
        return outcome;
    }

    private static void Reorder(DemonstratorCondition condition, RestoreOutcome outcome)
    {
        var saveId = condition.Controller.SaveID;
        var wasInTransit = condition.Controller.State == InTransit;
        try
        {
            ControllerReload.At(condition, (int)OnOrder, outcome);
        }
        catch (Exception ex)
        {
            Main.Logger.LogException($"Resetting the parts order for '{saveId}' failed:", ex);
            outcome.Warn($"Resetting the parts order for '{saveId}' failed partway through.");
            outcome.Failed = true;
            return;
        }

        outcome.Note(wasInTransit
            ? $"'{saveId}' had its part in transit. It is back on order and ready to be picked up at the "
                + "loading track again."
            : $"'{saveId}' had its parts order placed again at the loading track.");
    }

    // A controller with no cars to reload is partway through respawning them and has dropped its order already.
    private static IEnumerable<DemonstratorCondition> ActiveOrders() =>
        LocoRestorationController.allLocoRestorationControllers
            .Where(c => c != null && c.State is OnOrder or InTransit)
            .Select(DemonstratorCondition.Read)
            .Where(condition => condition != null)!;

    private static IEnumerable<TrainCar> CarsCarryingParts()
    {
        var spawner = SingletonBehaviour<CarSpawner>.Instance;
        if (spawner == null) return [];

        var parts = PartCargoTypes();
        return spawner.AllCars.Where(car =>
            car != null && car.logicCar != null && parts.Contains(car.logicCar.CurrentCargoTypeInCar));
    }

    // Custom Demonstrators keeps a cargo number table for anything that has ever been in the save, so
    // it catches parts no controller in this world wants anymore.
    private static HashSet<CargoType> PartCargoTypes()
    {
        var parts = new HashSet<CargoType>(LocoRestorationController.allLocoRestorationControllers
            .Where(c => c != null && c.locoPartCargo != null)
            .Select(c => c.locoPartCargo.v1));

        foreach (var number in LiveSave.Json()?.GetIntArray(SaveKeys.CustomDemonstratorsCargoValues) ?? [])
            parts.Add((CargoType)number);

        parts.Remove(CargoType.None);
        return parts;
    }

    private static WarehouseMachine? WarehouseFor(CargoType cargo)
    {
        var controllers = LocoRestorationController.allLocoRestorationControllers.Where(c => c != null).ToList();
        var owner = controllers.FirstOrDefault(c => c.locoPartCargo != null && c.locoPartCargo.v1 == cargo);
        return (owner != null ? ControllerTeardown.PartsWarehouse(owner) : null)
            ?? controllers.Select(ControllerTeardown.PartsWarehouse).FirstOrDefault(w => w != null);
    }
}
