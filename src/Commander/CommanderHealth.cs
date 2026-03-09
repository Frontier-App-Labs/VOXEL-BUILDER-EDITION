using Godot;
using System;
using VoxelSiege.Core;

namespace VoxelSiege.Commander;

/// <summary>
/// Tracks Commander HP, emits damage/death events through both local C# events
/// and the global EventBus for cross-system communication.
/// </summary>
public partial class CommanderHealth : Node
{
    public event Action<int, int>? Damaged;
    public event Action? Died;

    [Export]
    public int MaxHealth { get; set; } = GameConfig.CommanderHP;

    public int CurrentHealth { get; private set; }
    public bool IsDead { get; private set; }

    public override void _Ready()
    {
        ResetHealth();
    }

    public void ResetHealth()
    {
        CurrentHealth = MaxHealth;
        IsDead = false;
    }

    /// <summary>
    /// Apply damage to the Commander. Returns true if this blow killed them.
    /// Fires EventBus.CommanderDamaged and (on death) EventBus.CommanderKilled.
    /// </summary>
    public bool ApplyDamage(int damage)
    {
        if (IsDead || damage <= 0)
        {
            return false;
        }

        CurrentHealth = Math.Max(0, CurrentHealth - damage);
        Damaged?.Invoke(damage, CurrentHealth);

        if (CurrentHealth <= 0)
        {
            IsDead = true;
            Died?.Invoke();
            return true;
        }

        return false;
    }
}
