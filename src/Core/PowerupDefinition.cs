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
            "Deploys thick smoke over your fortress, making it invisible for a full rotation of turns. Enemies fire blind. Debris from hits is still visible.",
            0, // duration managed by rotation tracking, not simple turn count
            "\u2601", // cloud
            new Color("8b949e")),

        [PowerupType.Medkit] = new PowerupDefinition(
            "Medkit",
            400,
            "Instantly heals your Commander to full HP.",
            0, // instant
            "\u2695", // medical cross
            new Color("2ea043")),

        [PowerupType.ShieldGenerator] = new PowerupDefinition(
            "Shield Generator",
            600,
            "All damage to your fortress and Commander is halved for one full rotation of turns.",
            0, // duration managed by rotation tracking
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
            "Sends an electromagnetic pulse. Each enemy weapon has a 1/3 chance of being disabled for 2 turns (minimum 1 weapon hit).",
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
