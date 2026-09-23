using System;
using DV.LocoRestoration;

namespace DRF.Model;

internal static class RestorationStates
{
    internal const int Unknown = -1;

    internal static bool IsKnown(int state) =>
        Enum.IsDefined(typeof(LocoRestorationController.RestorationState), state);

    internal static string Describe(int state) => state switch
    {
        Unknown => "not started",
        (int)LocoRestorationController.RestorationState.S0_Initialized => "0 — wrecked, museum license not bought",
        (int)LocoRestorationController.RestorationState.S1_UnlockedRestorationLicense => "1 — wrecked, museum license bought",
        (int)LocoRestorationController.RestorationState.S2_LocoUnblocked => "2 — wrecked, locomotive license bought",
        (int)LocoRestorationController.RestorationState.S3_RerailedCars => "3 — rerailed",
        (int)LocoRestorationController.RestorationState.S4_OnDestinationTrack => "4 — delivered to its restoration destination",
        (int)LocoRestorationController.RestorationState.S5_PartOrdered => "5 — parts ordered",
        (int)LocoRestorationController.RestorationState.S6_PartPickedUp => "6 — parts in transit",
        (int)LocoRestorationController.RestorationState.S7_PartDelivered => "7 — parts delivered",
        (int)LocoRestorationController.RestorationState.S8_PartInstalled => "8 — parts installed",
        (int)LocoRestorationController.RestorationState.S9_LocoServiced => "9 — repaired",
        (int)LocoRestorationController.RestorationState.S10_PaintJobDone => "10 — painted",
        _ => $"{state} — unrecognised",
    };
}
