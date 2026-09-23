using System.Collections.Generic;
using System.Linq;

namespace DRF.Model;

internal sealed class DemonstratorRecord
{
    internal string SaveId { get; }

    internal int State { get; }

    internal string? LocoGuid { get; }

    internal string? SecondCarGuid { get; }

    internal CarRecord? Loco { get; set; }

    internal CarRecord? SecondCar { get; set; }

    internal DemonstratorRecord(string saveId, int state, string? locoGuid, string? secondCarGuid)
    {
        SaveId = saveId;
        State = state;
        LocoGuid = locoGuid;
        SecondCarGuid = secondCarGuid;
    }

    internal IEnumerable<string> CarGuids
    {
        get
        {
            if (!string.IsNullOrEmpty(LocoGuid)) yield return LocoGuid!;
            if (!string.IsNullOrEmpty(SecondCarGuid)) yield return SecondCarGuid!;
        }
    }

    internal IEnumerable<CarRecord> KnownCars
    {
        get
        {
            if (Loco != null) yield return Loco;
            if (SecondCar != null) yield return SecondCar;
        }
    }

    internal int GadgetCount => KnownCars.Sum(c => c.GadgetCount);

    internal bool IsComplete => Loco != null;

    internal string Describe() => Loco == null
        ? RestorationStates.Describe(State) + " (cars missing from this save)"
        : Describe(State, Loco.Id, Loco.PaintExterior, GadgetCount);

    internal static string Describe(int state, string locoId, string? paint, int gadgets)
    {
        var parts = new List<string> { RestorationStates.Describe(state), locoId };
        if (!string.IsNullOrEmpty(paint)) parts.Add("paint: " + paint);
        if (gadgets > 0) parts.Add($"{gadgets} gadget{(gadgets == 1 ? "" : "s")}");
        return string.Join(", ", parts);
    }
}
