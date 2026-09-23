using System.Reflection;
using DV.Garages;
using DV.ThingTypes;
using DV.Utils;
using HarmonyLib;

namespace DRF.World;

internal static class GarageState
{
    private static readonly FieldInfo? UnlockedGarages =
        AccessTools.Field(typeof(LicenseManager), "unlockedGarages");

    private static readonly FieldInfo? UnsavedChanges =
        AccessTools.Field(typeof(LicenseManager), "unsavedChanges");

    private static readonly FieldInfo? SpawnAllowed =
        AccessTools.Field(typeof(GarageCarSpawner), "spawnAllowed");

    internal static LicenseManager? Manager() =>
        typeof(SingletonBehaviour<LicenseManager>)
            .GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)?
            .GetValue(null) as LicenseManager;

    internal static bool IsUnlocked(GarageType_v2? garage) =>
        garage != null && Manager() is var manager && manager != null && manager.IsGarageUnlocked(garage);

    internal static void Unlock(GarageType_v2? garage)
    {
        var manager = Manager();
        if (garage == null || manager == null || manager.IsGarageUnlocked(garage)) return;
        manager.UnlockGarage(garage);
        Main.Logger.Log($"Unlocked garage {garage.id}, which the restored demonstrator had already earned.");
    }

    internal static void Revoke(GarageType_v2? garage, string reason)
    {
        if (garage == null) return;

        var manager = Manager();
        if (manager == null) return;
        if (UnlockedGarages?.GetValue(manager) is not System.Collections.Generic.HashSet<GarageType_v2> unlocked) return;
        if (!unlocked.Remove(garage)) return;

        UnsavedChanges?.SetValue(manager, true);
        Main.Logger.Log($"Revoked the garage unlock for {garage.id}, {reason}");
    }

    internal static void StopSpawning(GarageCarSpawner? spawner) => SpawnAllowed?.SetValue(spawner, false);

    internal static void ClearCars(GarageCarSpawner? spawner)
    {
        if (spawner == null || spawner.garageCars == null) return;
        for (int i = 0; i < spawner.garageCars.Length; i++) spawner.garageCars[i] = null;
    }

    internal static void Link(GarageCarSpawner? spawner, TrainCar? car)
    {
        if (spawner == null || car == null) return;
        if (System.Array.IndexOf(spawner.GarageCarLiveries, car.carLivery) < 0) return;
        if (spawner.GetCar(car.carLivery) != null) return;

        if (car.TryGetComponent<HomeGarageReference>(out var home)) UnityEngine.Object.DestroyImmediate(home);

        spawner.OverrideSpawnedCarReference(car);
    }
}
