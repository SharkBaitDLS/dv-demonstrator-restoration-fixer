using DRF.Saves;
using Newtonsoft.Json.Linq;
using UnityEngine;
using DV.JObjectExtstensions;

namespace DRF.Model;

internal sealed class CarRecord
{
    internal JObject Data { get; }

    internal string Guid { get; }

    internal string Id { get; }

    internal int Type { get; }

    internal bool Derailed { get; }

    internal Vector3 Position { get; }

    internal string? PaintExterior { get; }

    internal string? PaintInterior { get; }

    internal int GadgetCount { get; set; }

    private CarRecord(JObject data, string guid, string id, int type, bool derailed, Vector3 position,
        string? paintExterior, string? paintInterior)
    {
        Data = data;
        Guid = guid;
        Id = id;
        Type = type;
        Derailed = derailed;
        Position = position;
        PaintExterior = paintExterior;
        PaintInterior = paintInterior;
    }

    internal static CarRecord? From(JObject data)
    {
        var guid = data.GetString(SaveKeys.CarGuid);
        var id = data.GetString(SaveKeys.CarId);
        var type = data.GetInt(SaveKeys.CarType);
        if (string.IsNullOrEmpty(guid) || string.IsNullOrEmpty(id) || !type.HasValue) return null;

        // A car only counts as derailed when neither bogie is on a track; a half derailed car still has one
        // track reference the game can put it back on.
        var derailed = (data.GetBool(SaveKeys.Bogie1Derailed) ?? false)
            && (data.GetBool(SaveKeys.Bogie2Derailed) ?? false);

        return new CarRecord(data, guid, id, type.Value, derailed,
            data.GetVector3(SaveKeys.Position) ?? Vector3.zero,
            data.GetString(SaveKeys.PaintExterior), data.GetString(SaveKeys.PaintInterior));
    }
}
