using System;
using System.Reflection;
using DV.LocoRestoration;
using HarmonyLib;

namespace DRF.World;

internal static class QuestPopups
{
    private static readonly FieldInfo? LoadingDoneField =
        AccessTools.Field(typeof(LocoRestorationController), "loadingDone");

    internal sealed class Held(LocoRestorationController controller, object? loadingDone) : IDisposable
    {
        public void Dispose() => LoadingDoneField?.SetValue(controller, loadingDone ?? true);
    }

    internal static Held Hold(LocoRestorationController controller)
    {
        var held = new Held(controller, LoadingDoneField?.GetValue(controller));
        LoadingDoneField?.SetValue(controller, false);
        return held;
    }
}
