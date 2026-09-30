using System;
using System.Collections.Generic;
using System.Linq;
using DV.Customization.Paint;
using DV.Damage;
using DRF.World;
using DV.LocoRestoration;
using DV.ThingTypes;
using HarmonyLib;
using LocoSim.Resources;

namespace DRF.Model;

// Where a demonstrator's cars stand in the world right now, which bounds how far along its quest it could be.
internal sealed class DemonstratorCondition
{
    private const LocoRestorationController.RestorationState PartInTransit =
        LocoRestorationController.RestorationState.S6_PartPickedUp;

    private const int Serviced = (int)LocoRestorationController.RestorationState.S9_LocoServiced;
    private const int PaintJobDone = (int)LocoRestorationController.RestorationState.S10_PaintJobDone;

    // The same bar the game's own service check holds the cars to before it counts them as repaired.
    private const float MinHealth = 0.5f;
    private const float MinResources = 0.05f;

    private static readonly ResourceContainerType[] Resources =
    [
        ResourceContainerType.FUEL,
        ResourceContainerType.OIL,
        ResourceContainerType.COAL,
        ResourceContainerType.WATER,
    ];

    private static readonly Lazy<AccessTools.FieldRef<LocoRestorationController, RailTrack>?> DestinationField =
        new(() => AccessTools.Field(typeof(LocoRestorationController), "destinationRailTrack") is { } field
            ? AccessTools.FieldRefAccess<LocoRestorationController, RailTrack>(field)
            : null);

    internal LocoRestorationController Controller { get; }

    internal TrainCar Loco { get; }

    internal TrainCar? SecondCar { get; }

    internal int State => (int)Controller.State;

    internal bool Derailed { get; }

    internal bool OnDestinationTrack { get; }

    internal bool Repaired { get; }

    internal bool Painted { get; }

    // Everything the cars' blockers ask for before they come down.
    internal bool LicensesOwned { get; }

    private DemonstratorCondition(LocoRestorationController controller, TrainCar loco, TrainCar? secondCar)
    {
        Controller = controller;
        Loco = loco;
        SecondCar = secondCar;

        var cars = Cars.ToList();
        Derailed = cars.Any(c => c.derailed);
        OnDestinationTrack = !Derailed && IsOnDestination(controller, cars);
        Repaired = cars.All(IsServiced);
        Painted = cars.All(c => IsPainted(controller, c.PaintExterior) && IsPainted(controller, c.PaintInterior));
        LicensesOwned = Owns(controller.requiredRestorationLicense)
            && cars.All(c => Owns(c.carLivery != null ? c.carLivery.requiredLicense : null));
    }

    internal static DemonstratorCondition? Read(LocoRestorationController controller)
    {
        var loco = WorldState.Loco(controller);
        if (loco == null) return null;

        var secondCar = WorldState.SecondCar(controller);
        // The controller treats a missing second car as a broken restoration it has to start over.
        if (controller.secondCarLivery != null && secondCar == null) return null;

        return new DemonstratorCondition(controller, loco, secondCar);
    }

    internal IEnumerable<TrainCar> Cars
    {
        get
        {
            yield return Loco;
            if (SecondCar != null) yield return SecondCar;
        }
    }

    // A wreck still lying where it spawned can't be past being rerailed, and can't be unblocked before its
    // licenses are bought: loading a save puts its blockers straight back up and takes the rerail away again.
    // Once rerailed, it can't be past reaching its restoration track until it's there. Once repaired it is free
    // to go anywhere.
    internal int Highest
    {
        get
        {
            if (Derailed)
            {
                return LicensesOwned
                    ? (int)LocoRestorationController.RestorationState.S2_LocoUnblocked
                    : (int)LocoRestorationController.RestorationState.S1_UnlockedRestorationLicense;
            }
            if (Repaired) return Painted ? PaintJobDone : Serviced;
            return OnDestinationTrack
                ? (int)LocoRestorationController.RestorationState.S8_PartInstalled
                : (int)LocoRestorationController.RestorationState.S3_RerailedCars;
        }
    }

    // A railed loco left at an earlier state would wait forever for a rerail that already happened, and one
    // left at S3 on its restoration track would move itself straight on to S4 anyway.
    private int Lowest
    {
        get
        {
            if (Derailed) return (int)LocoRestorationController.RestorationState.S0_Initialized;
            return OnDestinationTrack
                ? (int)LocoRestorationController.RestorationState.S4_OnDestinationTrack
                : (int)LocoRestorationController.RestorationState.S3_RerailedCars;
        }
    }

    // Only ever forwards. A part in transit needs a loaded car to be carrying it, so it can't be picked by hand.
    // A loco that's already painted would sit at serviced until it was painted again, where the game's own
    // service check takes it straight on to the paint job being done.
    internal IReadOnlyList<int> Choices
    {
        get
        {
            var from = Math.Max(State + 1, Lowest);
            var paintedAlready = Highest == PaintJobDone;
            return
            [
                .. Enumerable.Range(from, Math.Max(0, Highest - from + 1))
                    .Where(s => s != (int)PartInTransit && !(paintedAlready && s == Serviced)),
            ];
        }
    }

    internal string Describe()
    {
        var parts = new List<string>
        {
            Derailed ? "derailed" : OnDestinationTrack ? "on its restoration track" : "on the rails",
            Repaired ? "repaired" : "not repaired",
        };
        if (Repaired) parts.Add(Painted ? "painted" : "not painted");
        return string.Join(", ", parts);
    }

    private static bool IsOnDestination(LocoRestorationController controller, IEnumerable<TrainCar> cars)
    {
        var destination = DestinationField.Value?.Invoke(controller);
        if (destination == null) return false;

        var track = destination.LogicTrack();
        return cars.All(c => c.logicCar != null && c.logicCar.CurrentTrack == track);
    }

    private static bool Owns(GeneralLicenseType_v2? license)
    {
        if (license == null) return true;
        var manager = GarageState.Manager();
        return manager != null && manager.IsGeneralLicenseAcquired(license);
    }

    private static bool IsServiced(TrainCar car)
    {
        if (car.TryGetComponent<DamageController>(out var damage) && damage.bodyDamage != null
            && damage.bodyDamage.currentHealth < damage.bodyDamage.maxHealth * MinHealth)
        {
            return false;
        }

        var resources = car.SimController != null ? car.SimController.resourceContainerController : null;
        return resources == null || Resources.All(r => resources.IsAbovePercentage(r, MinResources));
    }

    private static bool IsPainted(LocoRestorationController controller, TrainCarPaint? paint) =>
        paint == null
        || (paint.CurrentTheme != controller.abandonedTheme && paint.CurrentTheme != controller.primerTheme);
}
