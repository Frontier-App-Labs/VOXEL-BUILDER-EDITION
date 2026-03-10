using System;
using System.Collections.Generic;
using VoxelSiege.Building;

namespace VoxelSiege.AI.BotBases;

/// <summary>
/// Delegate for a bot base builder function.
/// Each base design is a static method matching this signature.
/// </summary>
public delegate BotBuildPlan BotBaseBuilder(BuildZone zone, int budget, Random rng);

/// <summary>
/// Registry of all bot base designs, organized by difficulty.
/// Add new bases by registering them in the static constructor.
/// </summary>
public static class BotBaseRegistry
{
    private static readonly List<BotBaseBuilder> EasyBases = new();
    private static readonly List<BotBaseBuilder> MediumBases = new();
    private static readonly List<BotBaseBuilder> HardBases = new();

    static BotBaseRegistry()
    {
        // ── Easy bases (budget ~8k, 3-4 weapons) ──
        EasyBases.Add(EasyStockade.Build);
        EasyBases.Add(EasyWatchtower.Build);
        EasyBases.Add(EasyBunker.Build);
        EasyBases.Add(EasyPalisade.Build);
        EasyBases.Add(EasyOutpost.Build);
        EasyBases.Add(EasyRedoubt.Build);
        EasyBases.Add(EasyBlockhouse.Build);

        // ── Medium bases (budget ~18k, 4-5 weapons) ──
        MediumBases.Add(MediumCastleKeep.Build);
        MediumBases.Add(MediumSteelFortress.Build);
        MediumBases.Add(MediumBrickCitadel.Build);
        MediumBases.Add(MediumStarFort.Build);
        MediumBases.Add(MediumTwinTower.Build);
        MediumBases.Add(MediumCourtyard.Build);
        MediumBases.Add(MediumBastion.Build);

        // ── Hard bases (budget ~35k, 5-7 weapons) ──
        HardBases.Add(HardGrandCastle.Build);
        HardBases.Add(HardWarFactory.Build);
        HardBases.Add(HardMountainStronghold.Build);
        HardBases.Add(HardLabyrinth.Build);
        HardBases.Add(HardSkyFortress.Build);
        HardBases.Add(HardIronCitadel.Build);
    }

    /// <summary>
    /// Returns a random base builder for the given difficulty.
    /// </summary>
    public static BotBaseBuilder GetRandom(BotDifficulty difficulty, Random rng)
    {
        List<BotBaseBuilder> pool = difficulty switch
        {
            BotDifficulty.Easy => EasyBases,
            BotDifficulty.Medium => MediumBases,
            BotDifficulty.Hard => HardBases,
            _ => EasyBases,
        };

        return pool[rng.Next(pool.Count)];
    }
}
