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
    private readonly Dictionary<PowerupType, int> _usedThisMatch = new();

    /// <summary>Maximum number of times any single powerup type can be used per match.</summary>
    public const int MaxUsesPerMatch = 5;

    /// <summary>Items purchased but not yet used.</summary>
    public IReadOnlyList<PowerupType> OwnedPowerups => _owned;

    /// <summary>Currently active effects with remaining durations.</summary>
    public IReadOnlyList<ActivePowerup> ActiveEffects => _active;

    /// <summary>
    /// Attempts to buy a powerup, deducting from the player's budget.
    /// Returns true if the purchase succeeds. Fails if at max uses for this match.
    /// </summary>
    public bool TryBuy(PowerupType type, PlayerData player)
    {
        // Check per-type purchase cap (e.g. Smoke = 2, others default 5)
        PowerupDefinition def = PowerupDefinitions.Get(type);
        int totalUsed = _usedThisMatch.GetValueOrDefault(type, 0) + CountOf(type);
        if (totalUsed >= def.MaxPurchase)
        {
            return false;
        }

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
    /// Returns false if not owned. Tracks usage toward the per-match cap.
    /// </summary>
    public bool TryConsume(PowerupType type)
    {
        int index = _owned.IndexOf(type);
        if (index < 0)
        {
            return false;
        }

        _owned.RemoveAt(index);
        _usedThisMatch[type] = _usedThisMatch.GetValueOrDefault(type, 0) + 1;
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
    /// Adds a powerup for free (no cost). Used to seed bot inventories.
    /// </summary>
    public void AddFree(PowerupType type)
    {
        _owned.Add(type);
    }

    /// <summary>
    /// Clears all owned and active powerups (for match reset).
    /// </summary>
    public void Clear()
    {
        _owned.Clear();
        _active.Clear();
        _usedThisMatch.Clear();
    }

    /// <summary>
    /// Returns the count of a specific powerup type in inventory.
    /// </summary>
    public int CountOf(PowerupType type)
    {
        return _owned.Count(p => p == type);
    }

    /// <summary>
    /// Returns how many times a powerup type has been used this match.
    /// </summary>
    public int UsedThisMatch(PowerupType type)
    {
        return _usedThisMatch.GetValueOrDefault(type, 0);
    }

    /// <summary>
    /// Checks if a powerup type has reached its per-match usage cap.
    /// </summary>
    public bool IsAtMaxUses(PowerupType type)
    {
        int max = PowerupDefinitions.Get(type).MaxPurchase;
        return _usedThisMatch.GetValueOrDefault(type, 0) + CountOf(type) >= max;
    }

    /// <summary>
    /// Refunds a consumed powerup back to inventory, undoing both the removal
    /// and the usage tracking. Used when a powerup activation fails (e.g., medkit
    /// on a full-health commander) and should not count toward the per-match cap.
    /// </summary>
    public void RefundConsumed(PowerupType type)
    {
        _owned.Add(type);
        int used = _usedThisMatch.GetValueOrDefault(type, 0);
        if (used > 0)
        {
            _usedThisMatch[type] = used - 1;
        }
    }
}
