using System;
using System.Collections.Generic;
using System.Linq;

namespace VoxelSiege.Core;

/// <summary>
/// Tracks an active powerup effect with remaining duration.
/// </summary>
public sealed class ActivePowerup
{
    public PowerupType Type { get; init; }
    public int RemainingTurns { get; set; }
    public PlayerSlot Owner { get; init; }

    /// <summary>Optional target data: e.g. shield position, EMP weapon ID, smoke zone owner.</summary>
    public object? TargetData { get; set; }
}

/// <summary>
/// Per-player powerup inventory. Manages purchasing during build phase
/// and activation/consumption during combat.
/// </summary>
public sealed class PowerupInventory
{
    private readonly List<PowerupType> _owned = new();
    private readonly List<ActivePowerup> _active = new();

    /// <summary>Items purchased but not yet used.</summary>
    public IReadOnlyList<PowerupType> OwnedPowerups => _owned;

    /// <summary>Currently active effects with remaining durations.</summary>
    public IReadOnlyList<ActivePowerup> ActiveEffects => _active;

    /// <summary>
    /// Attempts to buy a powerup, deducting from the player's budget.
    /// Returns true if the purchase succeeds.
    /// </summary>
    public bool TryBuy(PowerupType type, PlayerData player)
    {
        PowerupDefinition def = PowerupDefinitions.Get(type);
        if (!player.TrySpend(def.Cost))
        {
            return false;
        }

        _owned.Add(type);
        EventBus.Instance?.EmitBudgetChanged(new BudgetChangedEvent(player.Slot, player.Budget, -def.Cost));
        return true;
    }

    /// <summary>
    /// Sells one instance of a powerup back, refunding the full cost to the player.
    /// Returns true if the sell succeeds.
    /// </summary>
    public bool TrySell(PowerupType type, PlayerData player)
    {
        int index = _owned.IndexOf(type);
        if (index < 0)
        {
            return false;
        }

        PowerupDefinition def = PowerupDefinitions.Get(type);
        _owned.RemoveAt(index);
        player.Refund(def.Cost);
        EventBus.Instance?.EmitBudgetChanged(new BudgetChangedEvent(player.Slot, player.Budget, def.Cost));
        return true;
    }

    /// <summary>
    /// Checks whether the player has at least one of this powerup in inventory.
    /// </summary>
    public bool HasPowerup(PowerupType type)
    {
        return _owned.Contains(type);
    }

    /// <summary>
    /// Consumes one instance of the powerup from inventory and returns true.
    /// Returns false if not owned.
    /// </summary>
    public bool TryConsume(PowerupType type)
    {
        int index = _owned.IndexOf(type);
        if (index < 0)
        {
            return false;
        }

        _owned.RemoveAt(index);
        return true;
    }

    /// <summary>
    /// Adds a timed powerup effect to the active list.
    /// </summary>
    public ActivePowerup AddActiveEffect(PowerupType type, PlayerSlot owner, int durationTurns, object? targetData = null)
    {
        ActivePowerup effect = new ActivePowerup
        {
            Type = type,
            Owner = owner,
            RemainingTurns = durationTurns,
            TargetData = targetData,
        };
        _active.Add(effect);
        return effect;
    }

    /// <summary>
    /// Ticks all active effects, decrementing their durations by 1.
    /// Removes expired effects and returns them so the caller can clean up visuals.
    /// </summary>
    public List<ActivePowerup> TickAndExpire()
    {
        List<ActivePowerup> expired = new();
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            _active[i].RemainingTurns--;
            if (_active[i].RemainingTurns <= 0)
            {
                expired.Add(_active[i]);
                _active.RemoveAt(i);
            }
        }
        return expired;
    }

    /// <summary>
    /// Checks if this player has an active effect of the given type.
    /// </summary>
    public bool HasActiveEffect(PowerupType type)
    {
        return _active.Any(a => a.Type == type);
    }

    /// <summary>
    /// Gets all active effects of a given type.
    /// </summary>
    public IEnumerable<ActivePowerup> GetActiveEffects(PowerupType type)
    {
        return _active.Where(a => a.Type == type);
    }

    /// <summary>
    /// Clears all owned and active powerups (for match reset).
    /// </summary>
    public void Clear()
    {
        _owned.Clear();
        _active.Clear();
    }

    /// <summary>
    /// Returns the count of a specific powerup type in inventory.
    /// </summary>
    public int CountOf(PowerupType type)
    {
        return _owned.Count(p => p == type);
    }
}
