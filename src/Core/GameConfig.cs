using Godot;
using System.Collections.Generic;
using VoxelSiege.Voxel;

namespace VoxelSiege.Core;

/// <summary>
/// Central tweak point for all gameplay and technical constants.
/// </summary>
public static class GameConfig
{
    /// <summary>
    /// Currency earned per block destroyed, keyed by material type.
    /// </summary>
    public static readonly Dictionary<VoxelMaterialType, int> MaterialEarnValues = new Dictionary<VoxelMaterialType, int>
    {
        [VoxelMaterialType.Dirt] = 1,
        [VoxelMaterialType.Sand] = 1,
        [VoxelMaterialType.Wood] = 2,
        [VoxelMaterialType.Stone] = 3,
        [VoxelMaterialType.Brick] = 5,
        [VoxelMaterialType.Concrete] = 8,
        [VoxelMaterialType.Metal] = 12,
        [VoxelMaterialType.Ice] = 3,
        [VoxelMaterialType.Glass] = 4,
        [VoxelMaterialType.ArmorPlate] = 15,
        [VoxelMaterialType.ReinforcedSteel] = 20,
        [VoxelMaterialType.Obsidian] = 18,
        [VoxelMaterialType.Foundation] = 2,
        [VoxelMaterialType.Bark] = 1,
        [VoxelMaterialType.Leaves] = 1,
    };

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
    public const int DefaultBudget = 15000;
    public const int BotBudgetEasy = 8000;
    public const int BotBudgetMedium = 18000;
    public const int BotBudgetHard = 35000;
    public const int MaxBlueprintSlots = 20;
    public const int SandboxBudget = 15000;
    public const int SandboxSlotCost = 50000;
    public const int MaxUndoActions = 100;
    public const int MaxObsidianBlocks = 20;

    public const float DefaultTurnTime = 60f;
    public const int MaxDebrisObjects = 10000;
    public const int MaxVisualDebris = 10000;
    public const int MaxGpuParticlesGlobal = 500;
    public const float DebrisDespawnTime = 8f;
    public const int MaxRuinObjects = 10000;
    public const float SlowMoTimeScale = 0.3f;
    public const float SlowMoDuration = 2f;
    public const float FireSpreadInterval = 0.6f;        // seconds between spread checks (real-time)
    public const float FireDamageTickInterval = 0.15f;    // seconds between damage ticks (real-time)
    public const float FireDamagePerSecond = 5f;
    public const float FireIgniteChance = 0.3f;
    public const float FireDuration = 10f;
    public const int FireSpreadRadius = 3;                // microvoxels – fire jumps gaps to reach flammable material
    public const float FireJumpChance = 0.15f;            // lower chance for non-adjacent spread (gap jumping)
    public const int MaxWeaponSelectionsPerTurn = 4;

    // Troop system
    public const int MaxTroopsPerPlayer = 20;
    public const float TroopMeleeRange = 2f;       // microvoxels
    public const float TroopMoveLerpDuration = 0.3f; // seconds for smooth movement
    public const int MaxDoorsPerPlayer = 4;

    public const int CommanderHP = 15;
    public const int MinBlocksAroundCommander = 6;
    public const int MinWeaponCommanderGap = 2;

    // Commander fall damage
    public const float CommanderGravity = 9.8f;
    public const float CommanderFallDamageMinHeight = 2f;   // metres – no damage below this
    public const float CommanderFallDamagePerMeter = 10f;    // HP per metre fallen above threshold
    public const float CommanderVoidKillY = -10f;            // instant death below this world Y

    // Commander critical direct hit
    public const float CommanderDirectHitMultiplier = 2.5f;  // damage multiplier for direct projectile/beam hits
    public const float CommanderExposedMultiplier = 1.5f;     // damage multiplier when commander's IsExposed is true
    public const int MaxExplosionCommanderDamage = 8;         // cap per-explosion commander damage (2-shot kill at 15 HP)
    public const float CommanderCriticalShakeIntensity = 1.2f;
    public const float CommanderCriticalShakeDuration = 0.5f;

    public const int NetTickRate = 20;
    public const int MaxPlayers = 4;
    public const int MinPlayers = 2;
    public const int ProjectileInterpolationRate = 60;

    public const int MaxChunkMeshesPerFrame = 16;
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
