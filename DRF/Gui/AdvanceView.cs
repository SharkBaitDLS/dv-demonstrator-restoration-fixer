using System;
using System.Collections.Generic;
using System.Linq;
using DRF.Model;
using DV.LocoRestoration;
using UnityEngine;

namespace DRF.Gui;

internal static class AdvanceView
{
    private const float NameWidth = 260f;
    private const float LabelWidth = 110f;
    private const float ButtonWidth = 200f;
    private const float Indent = 24f;

    // The state picked for each demonstrator, kept between frames until it's applied or no longer on offer.
    private static readonly Dictionary<string, int> Picked = new(StringComparer.Ordinal);

    // Either a state to move a demonstrator forward to, or no state to revoke the garage it unlocked early.
    internal readonly struct Request(string saveId, int? state)
    {
        internal readonly string SaveId = saveId;

        internal readonly int? State = state;
    }

    internal static Request? Draw()
    {
        GUILayout.Label("A demonstrator whose cars are still in the world can be moved forward through its quest "
            + "by hand, as far as where its cars are and what shape they're in allows. It can't be moved "
            + "backwards, and a parts order can't be put in transit by hand.", GUILayout.ExpandWidth(true));
        GUILayout.Space(4);

        Request? request = null;
        var controllers = LocoRestorationController.allLocoRestorationControllers
            .Where(c => c != null)
            .OrderBy(c => c.SaveID, StringComparer.Ordinal)
            .ToList();

        foreach (var controller in controllers)
        {
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label(controller.SaveID, GUILayout.Width(NameWidth));
            GUILayout.Label(RestorationStates.Describe((int)controller.State), GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            if (DrawGarageCheck(controller) is { } revoke) request = revoke;

            var condition = DemonstratorCondition.Read(controller);
            if (condition == null)
            {
                Line("In the world:", "its cars are missing, so restore it from a save instead");
            }
            else
            {
                Line("In the world:", condition.Describe());
                if (DrawChoices(condition) is { } picked) request = picked;
            }

            GUILayout.EndVertical();
        }

        if (controllers.Count == 0) GUILayout.Label("This world has no demonstrators.");
        return request;
    }

    private static Request? DrawGarageCheck(LocoRestorationController controller)
    {
        if (!WorldState.GarageUnlockedEarly(controller)) return null;

        Request? request = null;
        GUILayout.BeginHorizontal();
        GUILayout.Space(Indent);
        GUILayout.Label("Garage:", GUILayout.Width(LabelWidth));
        if (GUILayout.Button("Revoke garage", GUILayout.Width(ButtonWidth)))
            request = new Request(controller.SaveID, null);
        GUILayout.Label("unlocked before it was repaired, which the quest doesn't allow yet",
            GUILayout.ExpandWidth(true));
        GUILayout.EndHorizontal();
        return request;
    }

    private static Request? DrawChoices(DemonstratorCondition condition)
    {
        var saveId = condition.Controller.SaveID;
        var choices = condition.Choices;
        if (choices.Count == 0)
        {
            Picked.Remove(saveId);
            Line("Can move to:", WhatNext(condition));
            return null;
        }

        if (!Picked.TryGetValue(saveId, out var state) || !choices.Contains(state)) state = choices[choices.Count - 1];

        GUILayout.BeginHorizontal();
        GUILayout.Space(Indent);
        GUILayout.Label("Can move to:", GUILayout.Width(LabelWidth));
        var index = GUILayout.Toolbar(IndexOf(choices, state), [.. choices.Select(s => $"S{s}")]);
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        state = choices[index];
        Picked[saveId] = state;

        Request? request = null;
        GUILayout.BeginHorizontal();
        GUILayout.Space(Indent + LabelWidth);
        if (GUILayout.Button($"Move forward to S{state}", GUILayout.Width(ButtonWidth)))
        {
            Picked.Remove(saveId);
            request = new Request(saveId, state);
        }
        GUILayout.Label(RestorationStates.Describe(state), GUILayout.ExpandWidth(true));
        GUILayout.EndHorizontal();
        return request;
    }

    private static int IndexOf(IReadOnlyList<int> choices, int state)
    {
        for (var i = 0; i < choices.Count; i++)
            if (choices[i] == state) return i;
        return choices.Count - 1;
    }

    private static string WhatNext(DemonstratorCondition condition)
    {
        if (condition.State >= (int)LocoRestorationController.RestorationState.S10_PaintJobDone)
            return "nothing, it is fully restored";
        if (condition.Derailed && !condition.LicensesOwned
            && condition.State < (int)LocoRestorationController.RestorationState.S2_LocoUnblocked)
        {
            return "nothing further until its licenses are bought";
        }
        if (condition.Derailed) return "nothing further until it is rerailed";
        if (!condition.Repaired && !condition.OnDestinationTrack)
            return "nothing further until it reaches its restoration track";
        if (!condition.Repaired) return "nothing further until it is repaired";
        return "nothing further until it is painted";
    }

    private static void Line(string label, string value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Space(Indent);
        GUILayout.Label(label, GUILayout.Width(LabelWidth));
        GUILayout.Label(value, GUILayout.ExpandWidth(true));
        GUILayout.EndHorizontal();
    }
}
