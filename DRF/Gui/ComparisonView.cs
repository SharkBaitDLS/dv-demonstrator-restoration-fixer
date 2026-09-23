using System.Collections.Generic;
using System.Linq;
using DRF.Model;
using DRF.Restore;
using UnityEngine;

namespace DRF.Gui;

internal static class ComparisonView
{
    private const float NameWidth = 260f;
    private const float LabelWidth = 110f;
    private const float Indent = 24f;

    internal static void Draw(RestorePlan plan, ISet<string> chosen, ISet<string> asNewSlots)
    {
        DrawRailwayNotice(plan);

        foreach (var row in plan.Rows)
        {
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.BeginHorizontal();
            var enabled = GUI.enabled;
            GUI.enabled = row.CanRestore;
            var wanted = GUILayout.Toggle(chosen.Contains(row.SaveId), row.SaveId, GUILayout.Width(NameWidth));
            GUI.enabled = enabled;

            if (row.CanRestore && wanted) chosen.Add(row.SaveId);
            else chosen.Remove(row.SaveId);

            GUILayout.Label(row.Note, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            if (row.World is { } world) Line("In the world now:", Describe(world));
            Line("Last loaded save:", Describe(row.Current));
            Line("Selected save:", Describe(row.Source));

            if (row.CanRestore && row.SourceState
                == (int)DV.LocoRestoration.LocoRestorationController.RestorationState.S6_PartPickedUp)
            {
                Detail("Its parts order was in transit on a car. It will get restored one step earlier with "
                    + "the parts ready to load instead.");
            }

            DrawSlotChoice(row, chosen, asNewSlots);

            GUILayout.EndVertical();
        }

        if (plan.Rows.Count == 0)
            GUILayout.Label("Neither save has any demonstrators recorded.");
    }

    // Where a restore would cost another demonstrator its progress, Custom Demonstrators can offer to
    // place this one in a slot of its own instead and leave that one alone.
    private static void DrawSlotChoice(RestoreRow row, ISet<string> chosen, ISet<string> asNewSlots)
    {
        if (!row.WouldDisplaceProgress || !chosen.Contains(row.SaveId))
        {
            asNewSlots.Remove(row.SaveId);
            return;
        }

        // Still worth saying what it costs even when there is nothing to be done about it.
        if (!row.OffersNewSlot)
        {
            asNewSlots.Remove(row.SaveId);
            Detail($"Putting it back where it was resets {row.Displaces}, which is at "
                + $"{RestorationStates.Describe(row.DisplacedState)}. It can't be given a slot of its own: "
                + $"{row.NewSlotReason}.");
            return;
        }

        GUILayout.BeginHorizontal();
        GUILayout.Space(Indent);
        if (GUILayout.Toggle(asNewSlots.Contains(row.SaveId), "Give it a slot of its own"))
            asNewSlots.Add(row.SaveId);
        else
            asNewSlots.Remove(row.SaveId);
        GUILayout.EndHorizontal();

        Detail(asNewSlots.Contains(row.SaveId)
            ? $"{row.Displaces} keeps its slot and its progress. This one comes back in a free museum "
                + "stall."
            : $"Putting it back where it was resets {row.Displaces}, which is at "
                + $"{RestorationStates.Describe(row.DisplacedState)}.");
    }

    private static void DrawRailwayNotice(RestorePlan plan)
    {
        if (!plan.RailwayChanged) return;

        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label("These two saves were made on different railways. Their locomotives will come "
            + "back with everything they had but on the nearest piece of track with room for them "
            + "rather than exactly where they stood, as the track IDs they were on are no longer valid.");
        GUILayout.EndVertical();
        GUILayout.Space(4);
    }

    private static void Detail(string text)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Space(Indent * 2);
        GUILayout.Label(text, GUILayout.ExpandWidth(true));
        GUILayout.EndHorizontal();
    }

    private static void Line(string label, string value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Space(Indent);
        GUILayout.Label(label, GUILayout.Width(LabelWidth));
        GUILayout.Label(value, GUILayout.ExpandWidth(true));
        GUILayout.EndHorizontal();
    }

    private static string Describe(DemonstratorRecord? record) =>
        record?.Describe() ?? "not recorded";

    private static string Describe(WorldState.Demonstrator world) => world.LocoId is { } id
        ? DemonstratorRecord.Describe(world.State, id, world.Paint, world.GadgetCount)
        : RestorationStates.Describe(world.State) + (world.HasLoco ? "" : ", no locomotive");

    internal static string Summarize(RestorationSurvey survey)
    {
        var demonstrators = survey.Demonstrators.Values.ToList();
        if (demonstrators.Count == 0) return "no demonstrators recorded";

        var complete = demonstrators.Count(d => d.IsComplete);
        var restored = demonstrators.Count(d =>
            d.State >= (int)DV.LocoRestoration.LocoRestorationController.RestorationState.S9_LocoServiced);
        return $"{demonstrators.Count} demonstrator(s), {complete} with their cars, {restored} restored";
    }
}
