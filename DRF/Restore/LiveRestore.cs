using System;
using System.Collections.Generic;
using System.Linq;
using DRF.Model;
using DRF.Saves;
using DRF.World;
using DV.JObjectExtstensions;
using DV.LocoRestoration;
using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace DRF.Restore;

internal static class LiveRestore
{
    private static readonly System.Reflection.FieldInfo? LoadingDoneField =
        AccessTools.Field(typeof(LocoRestorationController), "loadingDone");

    // One controller partway through a restore that has been emptied out and is waiting for its new car(s).
    private sealed class Pending(string saveId, LocoRestorationController controller, JObject entry,
        DemonstratorRecord wanted, object? loadingDone)
    {
        internal string SaveId { get; } = saveId;
        internal LocoRestorationController Controller { get; } = controller;
        internal JObject Entry { get; } = entry;
        internal DemonstratorRecord Wanted { get; } = wanted;
        internal object? LoadingDone { get; } = loadingDone;
        internal int Asked { get; set; }

        internal bool Ready { get; set; }
    }

    internal static RestoreOutcome Apply(RestorationSurvey source, ICollection<string> saveIds,
        ICollection<string> asNewSlots)
    {
        var outcome = new RestoreOutcome();

        RestoreCustomDemonstrators(source, saveIds, asNewSlots, outcome);

        // Every controller is emptied out before any car goes back, and every car is placed and coupled up
        // before any controller is handed its metadata. Cars of different controllers can be coupled
        // together in one consist, and a consist has to go back as a whole.
        var pending = new List<Pending>();
        try
        {
            foreach (var saveId in saveIds)
                Attempt(saveId, outcome, () => Prepare(source, saveId, pending, outcome));

            var ready = pending.Where(p => p.Ready).ToList();
            var batch = new CarInjector.Batch(source.TracksHash, ready.SelectMany(p => Records(source, p.Wanted)));
            try
            {
                CarInjector.Spawn(batch, outcome);
                CarInjector.Connect(batch);
            }
            catch (Exception ex)
            {
                Main.Logger.LogException("Putting the cars back failed:", ex);
                outcome.Warn("Putting the cars back failed partway through. See the log; the save may need "
                    + "reloading to settle.");
                outcome.Failed = true;
            }

            foreach (var p in ready) Attempt(p.SaveId, outcome, () => Finish(source, p, batch, outcome));
        }
        finally
        {
            // Restore the quest popups no matter what the outcome
            foreach (var p in pending) LoadingDoneField?.SetValue(p.Controller, p.LoadingDone ?? true);
        }

        if (outcome.Demonstrators > 0) CommsRadio.Refresh();
        return outcome;
    }

    private static void Attempt(string saveId, RestoreOutcome outcome, Action step)
    {
        try
        {
            step();
        }
        catch (Exception ex)
        {
            Main.Logger.LogException($"Restoring '{saveId}' failed:", ex);
            outcome.Warn($"Restoring '{saveId}' failed partway through. See the log; this demonstrator "
                + "may need the save reloaded to settle.");
            outcome.Failed = true;
        }
    }

    private static void Prepare(RestorationSurvey source, string saveId, List<Pending> pending,
        RestoreOutcome outcome)
    {
        if (!source.Demonstrators.TryGetValue(saveId, out var wanted) || !wanted.IsComplete)
        {
            outcome.Warn($"The selected save no longer holds the cars for '{saveId}', skipping it.");
            return;
        }

        var controller = LocoRestorationController.allLocoRestorationControllers
            .FirstOrDefault(c => c != null && c.SaveID == saveId);
        if (controller == null)
        {
            outcome.Warn($"'{saveId}' has no restoration in this world to put anything back into, "
                + "skipping it.");
            return;
        }

        if (source.Save.GetJObject(SaveKeys.RestorationLocos)?[saveId]?.DeepClone() is not JObject entry)
        {
            outcome.Warn($"The selected save has no readable record for '{saveId}', skipping it.");
            return;
        }

        // Listed before anything is touched, so its popups are turned back on whatever happens next.
        var p = new Pending(saveId, controller, entry, wanted, LoadingDoneField?.GetValue(controller));
        pending.Add(p);
        LoadingDoneField?.SetValue(controller, false);

        ControllerTeardown.Detach(controller, outcome);
        p.Asked = DropPartInTransit(entry, wanted.State, saveId, outcome);
        p.Ready = true;
    }

    private static void Finish(RestorationSurvey source, Pending p, CarInjector.Batch batch,
        RestoreOutcome outcome)
    {
        var cars = p.Wanted.CarGuids
            .Where(batch.Present.ContainsKey)
            .ToDictionary(guid => guid, guid => batch.Present[guid], StringComparer.Ordinal);

        p.Controller.LoadData(p.Entry);
        ItemReattach.Apply(source.Save, cars, outcome);
        ReconcileGarage(p.Controller, cars, outcome);

        outcome.Demonstrators++;

        // What the controller settled on rather than what it was asked for, so for example if a player
        // now owned the license and a prior S1 quest moved to S2, or an S6 rolled back to S5.
        var settled = (int)p.Controller.State;
        if (settled == p.Asked)
        {
            outcome.Note($"Restored '{p.SaveId}' to {RestorationStates.Describe(settled)}.");
        }
        else
        {
            outcome.Warn($"'{p.SaveId}' came back at {RestorationStates.Describe(settled)} rather than "
                + $"{RestorationStates.Describe(p.Asked)}, because the game could not rebuild that step. "
                + "Its quest carries on from where it landed; see the log for what it could not place.");
        }
    }

    private static IEnumerable<CarRecord> Records(RestorationSurvey source, DemonstratorRecord wanted) =>
        wanted.CarGuids.Select(source.Car).Where(car => car != null)!;

    // Too many edge cases around trying to put carried cargo back into the world when the DM1U and/or flatcar
    // could be in any manner of state in the live world. Ain't dealing with that, they can live with S5.
    private static int DropPartInTransit(JObject entry, int state, string saveId, RestoreOutcome outcome)
    {
        const int InTransit = (int)LocoRestorationController.RestorationState.S6_PartPickedUp;
        const int OnOrder = (int)LocoRestorationController.RestorationState.S5_PartOrdered;

        if (state != InTransit) return state;

        entry.SetInt(SaveKeys.RestorationState, OnOrder);
        entry.Remove(SaveKeys.RestorationTransportingCars);

        outcome.Note($"'{saveId}' had its part in transit, which was not restored. The parts are back on "
            + "order instead and ready to be picked up at the loading track.");
        return OnOrder;
    }

    private static void ReconcileGarage(LocoRestorationController controller,
        IReadOnlyDictionary<string, TrainCar> cars, RestoreOutcome outcome)
    {
        var garage = controller.garageSpawner;
        if (garage == null) return;

        if (controller.State < LocoRestorationController.RestorationState.S9_LocoServiced)
        {
            GarageState.Revoke(garage.garageType,
                $"{controller.SaveID} is back to {RestorationStates.Describe((int)controller.State)} "
                + "and has to earn it again.");
            GarageState.StopSpawning(garage);
            return;
        }

        foreach (var car in cars.Values) GarageState.Link(garage, car);
        GarageState.Unlock(garage.garageType);
        outcome.Note($"{controller.SaveID} is restored, so its garage holds it again.");
    }

    private static void RestoreCustomDemonstrators(RestorationSurvey source,
        ICollection<string> saveIds, ICollection<string> asNewSlots, RestoreOutcome outcome)
    {
        if (!source.HasCustomDemonstratorData)
        {
            outcome.Note("The selected save has no Custom Demonstrators data, so this save keeps its own.");
            return;
        }

        var live = LiveSave.Json();
        if (live == null)
        {
            outcome.Warn("No save is loaded, so the Custom Demonstrators record could not be restored.");
            return;
        }

        var cargo = CargoTable.Build(source);

        var merged = CustomDemonstratorsBridge.MergeDemonstrators(source.Root, saveIds, asNewSlots);
        if (merged == CustomDemonstratorsBridge.Result.Unavailable)
            CopyWholeRecord(source, saveIds, live, outcome);
        else if (asNewSlots.Count > 0)
            outcome.Note($"{asNewSlots.Count} demonstrator(s) were given a slot of their own, in the museum "
                + "instead of replacing an existing demonstrator in their former slot.");

        cargo.Write(live);
        foreach (var note in cargo.Notes) outcome.Note(note);

        outcome.CustomDemonstratorsRestored = true;

        switch (merged == CustomDemonstratorsBridge.Result.Unavailable
            ? merged
            : CustomDemonstratorsBridge.Reload())
        {
            case CustomDemonstratorsBridge.Result.Done:
                outcome.Note("Custom Demonstrators has taken the restored demonstrators into its record and "
                    + "read it back, so its slots match this save again.");
                break;
            case CustomDemonstratorsBridge.Result.Failed:
                outcome.Warn("Custom Demonstrators could not accept the restored record. This may cause it to "
                    + " not recognize the demonstrators you restored on next save load.");
                break;
            default:
                outcome.Note("Custom Demonstrators isn't here to rebuild its record, or is out of date, "
                    + $"so all {source.CustomDemonstratorKeys.Count} of the selected save's key(s) "
                    + "travel with this save as they are, to be read whenever it is next loaded with that "
                    + "mod installed.");
                break;
        }
    }

    private static void CopyWholeRecord(RestorationSurvey source, ICollection<string> saveIds, JObject live,
        RestoreOutcome outcome)
    {
        var wanted = source.CustomDemonstratorKeys.ToHashSet(StringComparer.Ordinal);
        var stale = live.Properties().Select(p => p.Name)
            .Where(n => n.StartsWith(SaveKeys.CustomDemonstratorsPrefix, StringComparison.Ordinal))
            .Where(n => !wanted.Contains(n))
            .ToList();
        foreach (var key in stale) live.Remove(key);

        foreach (var key in source.CustomDemonstratorKeys)
        {
            var value = source.Root[key];
            if (value != null) live[key] = value.DeepClone();
        }

        if (saveIds.Count < source.Demonstrators.Count)
        {
            outcome.Warn("That record describes every demonstrator the selected save had, including ones "
                + "not being restored here. Run this restoration with Custom Demonstrators up-to-date and "
                + "installed for it to work properly.");
        }
    }
}
