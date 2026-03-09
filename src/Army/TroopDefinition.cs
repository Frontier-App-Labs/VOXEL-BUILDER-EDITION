using Godot;

namespace VoxelSiege.Army;

public enum TroopType { Infantry, Demolisher, Scout }

public readonly record struct TroopStats(
    string Name,
    int Cost,
    int MaxHP,
    float SpeedMetersPerSec,
    int MoveStepsPerTick,   // grid cells moved per turn tick
    int AttackDamage,       // damage to commander per tick when adjacent
    float AttackRange,      // microvoxels
    bool CanDamageWalls,    // only Demolisher
    int MaxDamageDealt      // total damage a troop can deal before dying
);

public static class TroopDefinitions
{
    private static readonly TroopStats[] Stats =
    {
        new("Infantry",   50,  3, 2.5f, 5, 1, 2f, false, 30),
        new("Demolisher", 100, 5, 2.0f, 4, 2, 2f, true,  50),
        new("Scout",      75,  2, 4.0f, 8, 0, 0f, false, 20),
    };

    public static TroopStats Get(TroopType type) => Stats[(int)type];
    public static TroopType[] AllTypes => new[] { TroopType.Infantry, TroopType.Demolisher, TroopType.Scout };
}
