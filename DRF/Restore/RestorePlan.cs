using System;
using System.Collections.Generic;
using System.Linq;
using DRF.Model;
using DV.LocoRestoration;

namespace DRF.Restore;

internal sealed class RestoreRow(string saveId, DemonstratorRecord? current, DemonstratorRecord? source,
    WorldState.Demonstrator? world)
{
    internal string SaveId { get; } = saveId;

    internal DemonstratorRecord? Current { get; } = current;

    internal DemonstratorRecord? Source { get; } = source;

    internal WorldState.Demonstrator? World { get; } = world;

    internal int SourceState => Source?.State ?? RestorationStates.Unknown;

    internal int StateNow => World?.State ?? Current?.State ?? RestorationStates.Unknown;

    internal bool HasCarsNow => World?.HasLoco ?? Current?.IsComplete == true;

    internal bool CanRestore => Source?.IsComplete == true;

    internal bool IsRecommended => CanRestore && (SourceState > StateNow || !HasCarsNow);

    internal string? NewSlotReason { get; set; } = "Custom Demonstrators isn't here to add one";

    internal string? Displaces { get; set; }

    internal int DisplacedState { get; set; } = RestorationStates.Unknown;

    internal bool WouldDisplaceProgress => CanRestore && Displaces != null
        && DisplacedState >= (int)LocoRestorationController.RestorationState.S3_RerailedCars;

    internal bool OffersNewSlot => WouldDisplaceProgress && NewSlotReason == null;

    internal string Note
    {
        get
        {
            if (Source == null) return "not in the selected save";
            if (!CanRestore) return "the selected save no longer holds its cars";
            if (SourceState > StateNow) return "further along in the selected save";
            if (!HasCarsNow) return "its locomotive is missing here";
            if (SourceState < StateNow) return "it is currently further progressed, this would revert it";
            return "no difference";
        }
    }
}

internal sealed class RestorePlan
{
    internal IReadOnlyList<RestoreRow> Rows { get; }

    internal RestorationSurvey Source { get; }

    internal RestorationSurvey Current { get; }

    internal bool RailwayChanged { get; }

    private RestorePlan(IReadOnlyList<RestoreRow> rows, RestorationSurvey source, RestorationSurvey current,
        bool railwayChanged)
    {
        Rows = rows;
        Source = source;
        Current = current;
        RailwayChanged = railwayChanged;
    }

    internal IEnumerable<string> RecommendedIds => Rows.Where(r => r.IsRecommended).Select(r => r.SaveId);

    internal static RestorePlan Build(RestorationSurvey current, RestorationSurvey source)
    {
        var world = WorldState.IsInGame ? WorldState.Read() : null;

        var ids = current.Demonstrators.Keys
            .Concat(source.Demonstrators.Keys)
            .Concat(world?.Keys ?? [])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal);

        var rows = ids
            .Select(id => new RestoreRow(id, Lookup(current, id), Lookup(source, id), InWorld(world, id)))
            .ToList();

        ReadSlotChoices(rows, source);

        var railwayChanged = !string.IsNullOrEmpty(current.TracksHash)
            && !string.IsNullOrEmpty(source.TracksHash)
            && current.TracksHash != source.TracksHash;

        return new RestorePlan(rows, source, current, railwayChanged);
    }

    private static void ReadSlotChoices(List<RestoreRow> rows, RestorationSurvey source)
    {
        if (!source.HasCustomDemonstratorData) return;

        var restorable = rows.Where(row => row.CanRestore).Select(row => row.SaveId).ToList();
        if (restorable.Count == 0) return;

        var reasons = CustomDemonstratorsBridge.NewSlotReasons(source.Root, restorable);
        var occupants = CustomDemonstratorsBridge.SlotOccupants(source.Root, restorable);
        var byId = rows.ToDictionary(row => row.SaveId, StringComparer.Ordinal);

        foreach (var row in rows.Where(row => row.CanRestore))
        {
            if (reasons.TryGetValue(row.SaveId, out var reason)) row.NewSlotReason = reason;

            if (!occupants.TryGetValue(row.SaveId, out var occupant)) continue;
            row.Displaces = occupant;
            row.DisplacedState = byId.TryGetValue(occupant, out var standing)
                ? standing.StateNow
                : RestorationStates.Unknown;
        }
    }

    private static DemonstratorRecord? Lookup(RestorationSurvey survey, string id) =>
        survey.Demonstrators.TryGetValue(id, out var record) ? record : null;

    private static WorldState.Demonstrator? InWorld(IReadOnlyDictionary<string, WorldState.Demonstrator>? world,
        string id) =>
        world != null && world.TryGetValue(id, out var state) ? state : null;
}
