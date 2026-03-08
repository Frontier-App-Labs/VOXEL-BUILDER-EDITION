using Godot;
using System;
using VoxelSiege.Core;

namespace VoxelSiege.Commander;

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

    public bool ApplyDamage(int damage)
    {
        if (IsDead || damage <= 0)
        {
            return false;
        }

        CurrentHealth = Math.Max(0, CurrentHealth - damage);
        Damaged?.Invoke(damage, CurrentHealth);
        if (CurrentHealth == 0)
        {
            IsDead = true;
            Died?.Invoke();
            return true;
        }

        return false;
    }
}
