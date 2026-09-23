using System.Linq;
using System.Reflection;
using DV;
using HarmonyLib;
using UnityEngine;

namespace DRF.World;

internal static class CommsRadio
{
    private static CommsRadioCrewVehicle? _radio;
    private static MethodInfo? _update;

    internal static void Refresh()
    {
        if (_radio == null)
        {
            _radio = Resources.FindObjectsOfTypeAll<CommsRadioCrewVehicle>()
                .FirstOrDefault(r => r.gameObject.scene.IsValid());
        }
        if (_radio == null) return;

        _update ??= AccessTools.Method(typeof(CommsRadioCrewVehicle), "UpdateAvailableVehicles");
        _update?.Invoke(_radio, null);
    }
}
