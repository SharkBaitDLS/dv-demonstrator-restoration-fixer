using UnityModManagerNet;

namespace DRF;

public class Settings : UnityModManager.ModSettings
{
    // How far from where a car used to be that the mod will search to try to place it.
    // This will come into play with world updates from Double Track or similar mods.
    public float SearchRangeMeters = 2000f;
}
