using System;
using System.Collections.Generic;
using System.Linq;
using DRF.Model;
using DRF.Saves;
using DV.JObjectExtstensions;
using DV.LocoRestoration;
using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace DRF.Restore;

// Custom Demonstrators gives each demonstrator's restoration parts a cargo number of their own and keeps
// which id got which number in the save, as a pair of parallel arrays. That table is append-only by design.
// As a result, this one pair of keys is merged rather than replaced. Only a car actively carrying a number
// fixes it in place. Everything else is free to move, so nothing is traded away on either side.
internal static class CargoTable
{
    private const int PartOnACar = (int)LocoRestorationController.RestorationState.S6_PartPickedUp;

    internal sealed class Merge
    {
        internal Dictionary<string, int> Table { get; } = new(StringComparer.Ordinal);

        internal List<string> Notes { get; } = [];

        internal void Write(JObject live)
        {
            if (Table.Count == 0) return;
            live.SetStringArray(SaveKeys.CustomDemonstratorsCargoIds, [.. Table.Keys]);
            live.SetIntArray(SaveKeys.CustomDemonstratorsCargoValues, [.. Table.Values]);
        }
    }

    internal static Merge Build(RestorationSurvey source)
    {
        var live = Read(LiveSave.Json());
        var wanted = Read(source.Root);
        var merge = new Merge();
        var used = new HashSet<int>();

        foreach (var number in NumbersOnLoadedCars())
        {
            var id = IdOf(live, number);
            if (id == null || merge.Table.ContainsKey(id)) continue;
            merge.Table[id] = number;
            used.Add(number);
        }
        foreach (var entry in wanted) Place(merge, used, entry.Key, entry.Value);
        foreach (var entry in live) Place(merge, used, entry.Key, entry.Value);

        Describe(merge, live, wanted);
        return merge;
    }

    private static void Place(Merge merge, HashSet<int> used, string id, int preferred)
    {
        if (merge.Table.ContainsKey(id)) return;

        if (used.Add(preferred))
        {
            merge.Table[id] = preferred;
            return;
        }

        var moved = used.Max() + 1;
        used.Add(moved);
        merge.Table[id] = moved;

        merge.Notes.Add($"Parts cargo '{id}' moved to {moved}: a car standing in the world is already "
            + $"carrying {preferred}.");
    }

    private static IEnumerable<int> NumbersOnLoadedCars()
    {
        foreach (var controller in LocoRestorationController.allLocoRestorationControllers)
        {
            if (controller == null || (int)controller.State != PartOnACar) continue;

            var carrying = Traverse.Create(controller).Field("transportingCars").GetValue<List<TrainCar>>();
            foreach (var car in carrying ?? [])
            {
                if (car != null) yield return (int)car.LoadedCargo;
            }
        }
    }

    private static string? IdOf(IReadOnlyDictionary<string, int> table, int number) =>
        table.FirstOrDefault(entry => entry.Value == number).Key;

    private static Dictionary<string, int> Read(JObject? root)
    {
        var table = new Dictionary<string, int>(StringComparer.Ordinal);
        var ids = root?.GetStringArray(SaveKeys.CustomDemonstratorsCargoIds);
        var values = root?.GetIntArray(SaveKeys.CustomDemonstratorsCargoValues);
        if (ids == null || values == null) return table;

        for (int i = 0; i < Math.Min(ids.Length, values.Length); i++) table[ids[i]] = values[i];
        return table;
    }

    private static void Describe(Merge merge, Dictionary<string, int> live, Dictionary<string, int> wanted)
    {
        if (merge.Table.Count == 0) return;

        var kept = live.Keys.Count(id => !wanted.ContainsKey(id));
        if (kept > 0)
        {
            merge.Notes.Add($"Kept {kept} parts cargo number(s) this save had allocated since the one being "
                + "restored from.");
        }
    }
}
