using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace DRF.Saves;

internal static class SaveKeys
{
    // Top level
    internal const string RestorationLocos = "Restoration_Locos";
    internal const string LastTracksHash = "Last_Tracks_Hash";
    internal const string UniqueCars = "Unique_cars";
    internal const string GameVersion = "Game_version_latest";
    internal const string SaveVersion = "Version";
    internal const string GameMode = "Game_mode";

    internal const string StorageInstalledGadgets = "Storage_InstalledGadgets";
    internal const string StorageLostAndFound = "Storage_LostAndFound";
    internal const string StorageWorld = "Storage_World";

    internal const string CustomDemonstratorsPrefix = "CustomDemonstrators_";

    internal const string CustomDemonstratorsCargoIds = CustomDemonstratorsPrefix + "CargoIds";
    internal const string CustomDemonstratorsCargoValues = CustomDemonstratorsPrefix + "CargoValues";

    // Cars are stored under a key that carries the hash of the railway they were saved on, which is the
    // whole reason a track update orphans them.
    internal const string CarsPrefix = "Cars#";

    // Inside a Cars# block
    internal const string TrackHash = "trackHash";
    internal const string CarsData = "carsData";

    // Inside one entry of carsData
    internal const string CarId = "id";
    internal const string CarGuid = "carGuid";
    internal const string CarType = "type";
    internal const string PlayerSpawned = "playerSpawn";
    internal const string Unique = "unique";
    internal const string Position = "position";
    internal const string Rotation = "rotation";
    internal const string Bogie1Derailed = "bog1Derailed";
    internal const string Bogie2Derailed = "bog2Derailed";
    internal const string Bogie1TrackIndex = "bog1TrackChildInd";
    internal const string Bogie2TrackIndex = "bog2TrackChildInd";
    internal const string Bogie1Span = "bog1PosOnTrack";
    internal const string Bogie2Span = "bog2PosOnTrack";
    internal const string Exploded = "exploded";
    internal const string PaintExterior = "paintExterior";
    internal const string PaintInterior = "paintInterior";
    internal const string LoadedCargo = "loadedCargo";
    internal const string LoadedCargoModel = "loadedCargoModel";
    internal const string Handbrake = "hb";
    internal const string BrakePipe = "bp";
    internal const string AuxReservoir = "aux";
    internal const string MainReservoir = "mr";
    internal const string ControlReservoir = "cr";
    internal const string BrakeCylinder = "bc";
    internal const string VisitChecker = "visit";
    internal const string CarState = "carState";
    internal const string SimCarState = "simCarState";
    internal const string ModCarState = "modCarState";
    internal const string CouplerStateFront = "couplerStateF";
    internal const string CouplerStateRear = "couplerStateR";
    internal const string AirHoseFront = "airHoseF";
    internal const string AirHoseRear = "airHoseR";
    internal const string AirCockFront = "airCockF";
    internal const string AirCockRear = "airCockR";

    // Inside one entry of Restoration_Locos
    internal const string RestorationState = "state";
    internal const string RestorationLoco = "loco";
    internal const string RestorationSecondCar = "secondCar";
    internal const string RestorationTransportingCars = "transCars";

    // Inside one entry of a storage list (StorageItemData, serialized by name)
    internal const string ItemPrefabName = "itemPrefabName";
    internal const string ItemCarGuid = "carGuid";
    internal const string ItemState = "state";

    // Inside the state of a gadget item
    internal const string GadgetPlacedOn = "placedOn";

    internal static IEnumerable<string> CarsKeys(JObject root) =>
        [.. root.Properties().Select(p => p.Name).Where(n => n.StartsWith(CarsPrefix))];

    internal static JObject? CarsBlock(JObject root, string? preferredHash)
    {
        if (!string.IsNullOrEmpty(preferredHash))
        {
            var preferred = root[CarsPrefix + preferredHash] as JObject;
            if (preferred != null) return preferred;
        }

        JObject? best = null;
        int bestCount = -1;
        foreach (var key in CarsKeys(root))
        {
            if (root[key] is not JObject block) continue;
            int count = (block[CarsData] as JArray)?.Count ?? 0;
            if (count <= bestCount) continue;
            best = block;
            bestCount = count;
        }
        return best;
    }
}
