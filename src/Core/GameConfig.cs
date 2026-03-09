using Godot;

namespace VoxelSiege.Core;

/// <summary>
/// Central tweak point for all gameplay and technical constants.
/// </summary>
public static class GameConfig
{
    public const int ChunkSize = 16;
    public const int ChunkPadding = 1;
    public const int SmallArena = 64;
    public const int MediumArena = 96;
    public const int LargeArena = 128;
    public const int MicrovoxelsPerBuildUnit = 2;
    public const float BuildUnitMeters = 1.0f;
    public const float MicrovoxelMeters = BuildUnitMeters / MicrovoxelsPerBuildUnit;
    public const int PrototypeArenaWidth = 128;
    public const int PrototypeArenaDepth = 128;
    public const int PrototypeGroundThickness = 6;
    public const int PrototypeBuildZoneWidth = 32;
    public const int PrototypeBuildZoneHeight = 24;
    public const int PrototypeBuildZoneDepth = 32;
    public const int PrototypeBuildZoneSpacing = 16;

    public const float DefaultBuildTime = 300f;
    public const int DefaultBudget = 10000;
    public const int MaxBlueprintSlots = 20;
    public const int MaxUndoActions = 100;
    public const int MaxObsidianBlocks = 20;

    public const float DefaultTurnTime = 60f;
    public const int MaxDebrisObjects = 500;
    public const int MaxVisualDebris = 1000;
    public const int MaxGpuParticlesGlobal = 500;
    public const float DebrisDespawnTime = 8f;
    public const int MaxRuinObjects = 2000;
    public const float SlowMoTimeScale = 0.3f;
    public const float SlowMoDuration = 2f;
    public const float FireSpreadInterval = 1f;
    public const float FireDamagePerSecond = 5f;
    public const float FireIgniteChance = 0.3f;
    public const float FireDuration = 10f;
    public const int MaxWeaponSelectionsPerTurn = 4;

    // Troop system
    public const int MaxTroopsPerPlayer = 10;
    public const float TroopMeleeRange = 2f;       // microvoxels
    public const float TroopMoveLerpDuration = 0.3f; // seconds for smooth movement
    public const int MaxDoorsPerPlayer = 4;
    public const int TroopLifespanTicks = 6;        // ticks before a deployed troop dies automatically

    public const int CommanderHP = 100;
    public const int MinBlocksAroundCommander = 6;
    public const int MinWeaponCommanderGap = 2;
    public const float MaxWeaponPlacementRange = 60f; // build units from zone center – allows terrain placement

    // Commander fall damage
    public const float CommanderGravity = 9.8f;
    public const float CommanderFallDamageMinHeight = 2f;   // metres – no damage below this
    public const float CommanderFallDamagePerMeter = 10f;    // HP per metre fallen above threshold
    public const float CommanderVoidKillY = -10f;            // instant death below this world Y

    // Commander critical direct hit
    public const float CommanderDirectHitMultiplier = 2.5f;  // damage multiplier for direct projectile/beam hits
    public const float CommanderCriticalShakeIntensity = 1.2f;
    public const float CommanderCriticalShakeDuration = 0.5f;

    public const int NetTickRate = 20;
    public const int MaxPlayers = 4;
    public const int MinPlayers = 2;
    public const int ProjectileInterpolationRate = 60;

    public const int MaxChunkMeshesPerFrame = 8;
    public const float ChunkLODDistance = 200f;
    public const float ChunkCollisionRefreshDelay = 0.02f;

    public const int XPPerMatch = 100;
    public const int XPPerWin = 200;
    public const int XPPerKill = 50;
    public const int XPPerVoxelDestroyed = 10;
    public const int XPPerLevel = 500;

    public static readonly Vector3I PrototypePlayerOneZoneOrigin = new Vector3I(8, PrototypeGroundThickness, 8);
    public static readonly Vector3I PrototypePlayerTwoZoneOrigin = new Vector3I(PrototypePlayerOneZoneOrigin.X + PrototypeBuildZoneWidth + PrototypeBuildZoneSpacing, PrototypeGroundThickness, 8);

    // 4-player zone layout: each zone is 24x24x24 build units, placed in 4 quadrants.
    // Arena spans -32 to +32 build units (128 microvoxels / 2 microvoxels per BU).
    public const int FourPlayerBuildZoneWidth = 24;
    public const int FourPlayerBuildZoneHeight = 24;
    public const int FourPlayerBuildZoneDepth = 24;
    public static readonly Vector3I FourPlayerBuildZoneSize = new Vector3I(FourPlayerBuildZoneWidth, FourPlayerBuildZoneHeight, FourPlayerBuildZoneDepth);

    // Zone origins for 4-player layout (in build unit coords)
    public static readonly Vector3I[] FourPlayerZoneOrigins =
    {
        new Vector3I(-30, PrototypeGroundThickness / MicrovoxelsPerBuildUnit, -30), // Player1: top-left
        new Vector3I(6, PrototypeGroundThickness / MicrovoxelsPerBuildUnit, -30),   // Player2: top-right
        new Vector3I(-30, PrototypeGroundThickness / MicrovoxelsPerBuildUnit, 6),   // Player3: bottom-left
        new Vector3I(6, PrototypeGroundThickness / MicrovoxelsPerBuildUnit, 6),     // Player4: bottom-right
    };

    public static readonly Color[] PlayerColors =
    {
        new Color("57c84d"),
        new Color("d74f4f"),
        new Color("3e96ff"),
        new Color("8a8a8a"),
    };
}
