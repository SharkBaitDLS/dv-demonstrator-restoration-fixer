using System;
using System.Collections.Generic;
using System.Linq;
using DRF.Saves;
using DV.JObjectExtstensions;
using Newtonsoft.Json.Linq;

namespace DRF.Model;

internal sealed class RestorationSurvey
{
    internal SaveGameData Save { get; }

    internal JObject Root { get; }

    internal string? TracksHash { get; }

    internal string? GameVersion { get; }

    internal JObject? CarsBlock { get; }

    internal IReadOnlyDictionary<string, DemonstratorRecord> Demonstrators { get; }

    internal IReadOnlyList<string> CustomDemonstratorKeys { get; }

    private readonly Dictionary<string, CarRecord> _carsByGuid;

    private RestorationSurvey(SaveGameData save, JObject root, string? tracksHash, string? gameVersion,
        JObject? carsBlock, Dictionary<string, CarRecord> carsByGuid,
        Dictionary<string, DemonstratorRecord> demonstrators, List<string> customDemonstratorKeys)
    {
        Save = save;
        Root = root;
        TracksHash = tracksHash;
        GameVersion = gameVersion;
        CarsBlock = carsBlock;
        _carsByGuid = carsByGuid;
        Demonstrators = demonstrators;
        CustomDemonstratorKeys = customDemonstratorKeys;
    }

    internal CarRecord? Car(string? guid) =>
        !string.IsNullOrEmpty(guid) && _carsByGuid.TryGetValue(guid!, out var car) ? car : null;

    internal bool HasCustomDemonstratorData => CustomDemonstratorKeys.Count > 0;

    internal static RestorationSurvey Read(SaveGameData save)
    {
        var root = save.GetJsonObject();
        var tracksHash = save.GetString(SaveKeys.LastTracksHash);
        var carsBlock = SaveKeys.CarsBlock(root, tracksHash);

        var carsByGuid = IndexCars(carsBlock);
        CountGadgets(save, carsByGuid);

        var demonstrators = new Dictionary<string, DemonstratorRecord>(StringComparer.Ordinal);
        foreach (var entry in save.GetJObject(SaveKeys.RestorationLocos)?.Properties() ?? Enumerable.Empty<JProperty>())
        {
            if (entry.Value is not JObject data) continue;
            var record = ReadDemonstrator(entry.Name, data);
            record.Loco = Lookup(carsByGuid, record.LocoGuid);
            record.SecondCar = Lookup(carsByGuid, record.SecondCarGuid);
            demonstrators[entry.Name] = record;
        }

        var customKeys = root.Properties()
            .Select(p => p.Name)
            .Where(n => n.StartsWith(SaveKeys.CustomDemonstratorsPrefix, StringComparison.Ordinal))
            .ToList();

        return new RestorationSurvey(save, root, tracksHash, save.GetString(SaveKeys.GameVersion),
            carsBlock, carsByGuid, demonstrators, customKeys);
    }

    private static DemonstratorRecord ReadDemonstrator(string saveId, JObject data) => new(
        saveId,
        data.GetInt(SaveKeys.RestorationState) ?? RestorationStates.Unknown,
        data.GetString(SaveKeys.RestorationLoco),
        data.GetString(SaveKeys.RestorationSecondCar));

    private static CarRecord? Lookup(Dictionary<string, CarRecord> cars, string? guid) =>
        !string.IsNullOrEmpty(guid) && cars.TryGetValue(guid!, out var car) ? car : null;

    private static Dictionary<string, CarRecord> IndexCars(JObject? carsBlock)
    {
        var cars = new Dictionary<string, CarRecord>(StringComparer.Ordinal);
        foreach (var entry in carsBlock?.GetJObjectArray(SaveKeys.CarsData) ?? [])
        {
            var car = CarRecord.From(entry);
            if (car != null) cars[car.Guid] = car;
        }
        return cars;
    }

    // Installed gadgets aren't part of a car's own record, they're reverse-linked back to the car
    // by GUID, which is why they end up in lost and found when that car fails to load.
    private static void CountGadgets(SaveGameData save, Dictionary<string, CarRecord> cars)
    {
        foreach (var gadget in StorageLists.Read(save, SaveKeys.StorageInstalledGadgets))
        {
            var placedOn = gadget.state?.GetString(SaveKeys.GadgetPlacedOn);
            if (string.IsNullOrEmpty(placedOn)) continue;
            if (cars.TryGetValue(placedOn!, out var car)) car.GadgetCount++;
        }
    }
}
