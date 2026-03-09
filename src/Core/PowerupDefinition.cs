using Godot;
using System.Collections.Generic;

namespace VoxelSiege.Core;

/// <summary>
/// Static data for a powerup type: name, cost, description, duration in turns, and icon glyph.
/// </summary>
public readonly record struct PowerupDefinition(
    string Name,
    int Cost,
    string Description,
    int DurationTurns,
    string IconGlyph,
    Color AccentColor);

/// <summary>
/// Central registry of all powerup definitions.
/// </summary>
public static class PowerupDefinitions
{
    private static readonly Dictionary<PowerupType, PowerupDefinition> Definitions = new()
    {
        [PowerupType.SmokeScreen] = new PowerupDefinition(
            "Smoke Screen",
            300,
            "Deploys smoke over your fortress for 2 turns. Projectiles passing through are deflected +/-15 deg.",
            2,
            "\u2601", // cloud
            new Color("8b949e")),

        [PowerupType.RepairKit] = new PowerupDefinition(
            "Repair Kit",
            400,
            "Instantly repairs up to 20 damaged voxels to full HP. Targets most damaged first.",
            0, // instant
            "\u2695", // medical cross
            new Color("2ea043")),

        [PowerupType.SpyDrone] = new PowerupDefinition(
            "Spy Drone",
            500,
            "Reveals enemy commander's approximate location for 1 turn (accurate within 3 build units).",
            1,
            "\u25ce", // target/bullseye
            new Color("d4a029")),

        [PowerupType.ShieldGenerator] = new PowerupDefinition(
            "Shield Generator",
            600,
            "Creates a force field over a 5x5x5 area for 2 turns. Shielded blocks take 50% less damage.",
            2,
            "\u2726", // four-pointed star
            new Color("3e96ff")),

        [PowerupType.AirstrikeBeacon] = new PowerupDefinition(
            "Airstrike",
            800,
            "Calls in 3 bombardment shells on an 8x8 area of an enemy's fortress.",
            0, // instant
            "\u2708", // airplane
            new Color("d73a49")),

        [PowerupType.EmpBlast] = new PowerupDefinition(
            "EMP Blast",
            700,
            "Disables one enemy weapon for 2 turns. The weapon can't fire while disabled.",
            2,
            "\u26a1", // lightning bolt
            new Color("3e96ff")),
    };

    public static PowerupDefinition Get(PowerupType type)
    {
        return Definitions[type];
    }

    public static IEnumerable<PowerupType> AllTypes => Definitions.Keys;
}
