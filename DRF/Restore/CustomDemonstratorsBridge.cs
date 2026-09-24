using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityModManagerNet;

namespace DRF.Restore;

internal static class CustomDemonstratorsBridge
{
    private const string ModId = "CustomDemonstrators";
    private const string TypeName = "CustomDemonstrators.Api.SaveRecord";

    internal enum Result
    {
        Unavailable,
        Done,
        Failed,
    }

    internal static Result RestoreDemonstrators(JObject source, ICollection<string> saveIds,
        ICollection<string> asNewSlots) =>
        Invoke("RestoreDemonstratorsFrom",
            [typeof(JObject), typeof(IEnumerable<string>), typeof(IEnumerable<string>)],
            [source, saveIds, asNewSlots]);

    internal static IDictionary<string, string?> NewSlotReasons(JObject source, IEnumerable<string> saveIds) =>
        Ask<string?>("NewSlotEligibility", source, saveIds);

    internal static IDictionary<string, string> SlotOccupants(JObject source, IEnumerable<string> saveIds) =>
        Ask<string>("SlotOccupants", source, saveIds);

    private static IDictionary<string, TValue> Ask<TValue>(string name, JObject source,
        IEnumerable<string> saveIds)
    {
        var empty = new Dictionary<string, TValue>();

        var mod = UnityModManager.FindMod(ModId);
        if (mod == null || !mod.Active || !mod.HasAssembly) return empty;

        var method = mod.Assembly.GetType(TypeName, throwOnError: false)?.GetMethod(
            name, BindingFlags.Public | BindingFlags.Static, null,
            [typeof(JObject), typeof(IEnumerable<string>)], null);
        if (method == null) return empty;

        try
        {
            if (method.Invoke(null, [source, saveIds]) is IDictionary<string, TValue> answer) return answer;
        }
        catch (Exception ex)
        {
            Main.Logger.LogException($"{TypeName}.{name}() threw:", ex);
        }
        return empty;
    }

    private static Result Invoke(string name, Type[] signature, object[] arguments)
    {
        var mod = UnityModManager.FindMod(ModId);
        if (mod == null || !mod.Active || !mod.HasAssembly) return Result.Unavailable;

        var method = mod.Assembly.GetType(TypeName, throwOnError: false)
            ?.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, signature, null);
        if (method == null)
        {
            Main.Logger.Log($"{ModId} {mod.Info?.Version} has no {TypeName}.{name}(), so its record is "
                + "copied over whole instead.");
            return Result.Unavailable;
        }

        try
        {
            if (method.Invoke(null, arguments) is true) return Result.Done;

            Main.Logger.Warning($"{TypeName}.{name}() did not accept the update.");
            return Result.Failed;
        }
        catch (Exception ex)
        {
            Main.Logger.LogException($"{TypeName}.{name}() threw:", ex);
            return Result.Failed;
        }
    }
}
