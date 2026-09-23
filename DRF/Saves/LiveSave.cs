using System.Reflection;
using DV.Utils;
using Newtonsoft.Json.Linq;

namespace DRF.Saves;

internal static class LiveSave
{
    internal static SaveGameData? Data() => Manager() is var manager && manager != null ? manager.data : null;

    internal static JObject? Json() => Data()?.GetJsonObject();

    internal static bool IsLoaded => Data() != null;

    private static SaveGameManager? Manager() =>
        typeof(SingletonBehaviour<SaveGameManager>)
            .GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)?
            .GetValue(null) as SaveGameManager;
}
