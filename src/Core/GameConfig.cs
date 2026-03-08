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
    public const int PrototypeArenaWidth = 96;
    public const int PrototypeArenaDepth = 96;
    public const int PrototypeGroundThickness = 4;
    public const int PrototypeBuildZoneWidth = 24;
    public const int PrototypeBuildZoneHeight = 20;
    public const int PrototypeBuildZoneDepth = 24;
    public const int PrototypeBuildZoneSpacing = 12;

    public const float DefaultBuildTime = 300f;
    public const int DefaultBudget = 1000;
    public const int MaxBlueprintSlots = 20;
    public const int MaxUndoActions = 100;
    public const int MaxObsidianBlocks = 20;

    public const float DefaultTurnTime = 60f;
    public const int MaxDebrisObjects = 200;
    public const float DebrisDespawnTime = 5f;
    public const float SlowMoTimeScale = 0.3f;
    public const float SlowMoDuration = 2f;
    public const float FireSpreadInterval = 1f;
    public const float FireDamagePerSecond = 5f;
    public const float FireIgniteChance = 0.3f;
    public const float FireDuration = 10f;
    public const int MaxWeaponSelectionsPerTurn = 4;

    public const int CommanderHP = 100;
    public const int MinBlocksAroundCommander = 6;
    public const int MinWeaponCommanderGap = 2;

    public const int NetTickRate = 20;
    public const int MaxPlayers = 4;
    public const int MinPlayers = 2;
    public const int ProjectileInterpolationRate = 60;

    public const int MaxChunkMeshesPerFrame = 4;
    public const float ChunkLODDistance = 64f;
    public const float ChunkCollisionRefreshDelay = 0.02f;

    public const int XPPerMatch = 100;
    public const int XPPerWin = 200;
    public const int XPPerKill = 50;
    public const int XPPerVoxelDestroyed = 10;
    public const int XPPerLevel = 500;

    public static readonly Vector3I PrototypePlayerOneZoneOrigin = new Vector3I(8, PrototypeGroundThickness, 8);
    public static readonly Vector3I PrototypePlayerTwoZoneOrigin = new Vector3I(PrototypePlayerOneZoneOrigin.X + PrototypeBuildZoneWidth + PrototypeBuildZoneSpacing, PrototypeGroundThickness, 8);
    public static readonly Color[] PlayerColors =
    {
        new Color("57c84d"),
        new Color("d74f4f"),
        new Color("3e96ff"),
        new Color("8a8a8a"),
    };
}
