using System.Collections.Generic;

namespace DRF.Restore;

internal sealed class RestoreOutcome
{
    internal List<string> Notes { get; } = [];

    internal int Demonstrators { get; set; }

    internal int CarsRemoved { get; set; }

    internal int CarsReturned { get; set; }

    internal int CarsAlreadyPresent { get; set; }

    internal int CarsFailed { get; set; }

    internal int ItemsReattached { get; set; }

    internal int ItemsUnaccounted { get; set; }

    internal bool CustomDemonstratorsRestored { get; set; }

    internal bool Failed { get; set; }

    internal void Note(string note)
    {
        Notes.Add(note);
        Main.Logger.Log(note);
    }

    internal void Warn(string note)
    {
        Notes.Add(note);
        Main.Logger.Warning(note);
    }

    internal string Summary()
    {
        var lines = new List<string>
        {
            $"Restored {Demonstrators} demonstrator{(Demonstrators == 1 ? "" : "s")}.",
        };
        if (CarsReturned > 0) lines.Add($"Put {CarsReturned} car(s) back into the world.");
        if (CarsRemoved > 0) lines.Add($"Removed {CarsRemoved} unwanted car(s).");
        if (CarsAlreadyPresent > 0) lines.Add($"{CarsAlreadyPresent} car(s) were already there.");
        if (CarsFailed > 0) lines.Add($"{CarsFailed} car(s) could not be placed.");
        if (ItemsReattached > 0) lines.Add($"Reattached {ItemsReattached} item(s) from lost and found.");
        if (ItemsUnaccounted > 0) lines.Add($"{ItemsUnaccounted} item(s) had nothing in lost and found to claim.");
        if (CustomDemonstratorsRestored) lines.Add("Restored the Custom Demonstrators save record.");
        return string.Join("\n", lines);
    }
}
