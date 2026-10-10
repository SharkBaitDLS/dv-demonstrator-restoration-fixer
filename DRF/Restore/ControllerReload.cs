using System.Collections.Generic;
using System.Linq;
using DRF.Model;
using DRF.Saves;
using DRF.World;
using DV.JObjectExtstensions;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DRF.Restore;

internal static class ControllerReload
{
    internal static void At(DemonstratorCondition condition, int state, RestoreOutcome outcome)
    {
        var controller = condition.Controller;
        var entry = Traverse.Create(controller).Field("saveData").GetValue<JObject>()?.DeepClone() as JObject
            ?? [];
        entry.SetInt(SaveKeys.RestorationState, state);
        entry.SetString(SaveKeys.RestorationLoco, condition.Loco.CarGUID);
        if (condition.SecondCar != null) entry.SetString(SaveKeys.RestorationSecondCar, condition.SecondCar.CarGUID);
        entry.Remove(SaveKeys.RestorationTransportingCars);

        ControllerTeardown.Release(controller, outcome);

        using var popups = QuestPopups.Hold(controller);
        var lent = LendBlockers(condition.Cars);
        try
        {
            controller.LoadData(entry);
        }
        finally
        {
            ReturnBlockers(lent);
        }
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
}
