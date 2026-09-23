using System;
using System.Collections.Generic;
using System.Linq;
using DV.Customization.Gadgets;
using DV.Customization.Paint;
using DV.LocoRestoration;
using HarmonyLib;

namespace DRF.Model;

internal static class WorldState
{
    internal readonly struct Demonstrator(int state, bool hasLoco, string? locoId, string? paint, int gadgets)
    {
        internal readonly int State = state;

        internal readonly bool HasLoco = hasLoco;

        internal readonly string? LocoId = locoId;

        internal readonly string? Paint = paint;

        internal readonly int GadgetCount = gadgets;
    }

    private static readonly Lazy<AccessTools.FieldRef<LocoRestorationController, TrainCar>?> LocoField =
        new(() => Car("loco"));

    private static readonly Lazy<AccessTools.FieldRef<LocoRestorationController, TrainCar>?> SecondCarField =
        new(() => Car("secondCar"));

    private static AccessTools.FieldRef<LocoRestorationController, TrainCar>? Car(string name) =>
        AccessTools.Field(typeof(LocoRestorationController), name) is { } field
            ? AccessTools.FieldRefAccess<LocoRestorationController, TrainCar>(field)
            : null;

    internal static bool IsInGame =>
        LocoRestorationController.allLocoRestorationControllers.Any(c => c != null);

    internal static IReadOnlyDictionary<string, Demonstrator> Read()
    {
        var result = new Dictionary<string, Demonstrator>(StringComparer.Ordinal);
        var locoField = LocoField.Value;
        var secondCarField = SecondCarField.Value;

        foreach (var controller in LocoRestorationController.allLocoRestorationControllers)
        {
            if (controller == null) continue;

            var loco = locoField?.Invoke(controller);
            var secondCar = secondCarField?.Invoke(controller);
            var hasLoco = locoField == null || loco != null;

            result[controller.SaveID] = new Demonstrator(
                (int)controller.State,
                hasLoco,
                loco != null ? loco.ID : null,
                loco != null ? PaintName(loco.PaintExterior) : null,
                Gadgets(loco) + Gadgets(secondCar));
        }
        return result;
    }

    private static string? PaintName(TrainCarPaint? paint) =>
        paint != null && paint.CurrentTheme != null ? paint.CurrentTheme.AssetName : null;

    private static int Gadgets(TrainCar? car) =>
        car != null && car.Customization != null ? car.Customization.Customizers.OfType<GadgetBase>().Count() : 0;
}
