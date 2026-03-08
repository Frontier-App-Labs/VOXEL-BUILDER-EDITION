using Godot;
using System;

namespace VoxelSiege.Core;

public readonly record struct VoxelChangeEvent(Vector3I Position, ushort BeforeData, ushort AfterData, PlayerSlot? Instigator);
public readonly record struct PhaseChangedEvent(GamePhase PreviousPhase, GamePhase CurrentPhase);
public readonly record struct TurnChangedEvent(PlayerSlot CurrentPlayer, int RoundNumber, float TurnTimeSeconds);
public readonly record struct BudgetChangedEvent(PlayerSlot Player, int NewBudget, int Delta);
public readonly record struct CommanderDamagedEvent(PlayerSlot Player, int Damage, int RemainingHealth, Vector3 WorldPosition);
public readonly record struct CommanderKilledEvent(PlayerSlot Victim, PlayerSlot? Killer, Vector3 WorldPosition);
public readonly record struct WeaponFiredEvent(PlayerSlot Owner, string WeaponId, Vector3 Origin, Vector3 Direction);

/// <summary>
/// Lightweight global event hub used by systems that should not hold direct references.
/// </summary>
public partial class EventBus : Node
{
    public static EventBus? Instance { get; private set; }

    public event Action<VoxelChangeEvent>? VoxelChanged;
    public event Action<PhaseChangedEvent>? PhaseChanged;
    public event Action<TurnChangedEvent>? TurnChanged;
    public event Action<BudgetChangedEvent>? BudgetChanged;
    public event Action<CommanderDamagedEvent>? CommanderDamaged;
    public event Action<CommanderKilledEvent>? CommanderKilled;
    public event Action<WeaponFiredEvent>? WeaponFired;

    public override void _EnterTree()
    {
        Instance = this;
    }

    public override void _ExitTree()
    {
        if (ReferenceEquals(Instance, this))
        {
            Instance = null;
        }
    }

    public void EmitVoxelChanged(VoxelChangeEvent payload) => VoxelChanged?.Invoke(payload);
    public void EmitPhaseChanged(PhaseChangedEvent payload) => PhaseChanged?.Invoke(payload);
    public void EmitTurnChanged(TurnChangedEvent payload) => TurnChanged?.Invoke(payload);
    public void EmitBudgetChanged(BudgetChangedEvent payload) => BudgetChanged?.Invoke(payload);
    public void EmitCommanderDamaged(CommanderDamagedEvent payload) => CommanderDamaged?.Invoke(payload);
    public void EmitCommanderKilled(CommanderKilledEvent payload) => CommanderKilled?.Invoke(payload);
    public void EmitWeaponFired(WeaponFiredEvent payload) => WeaponFired?.Invoke(payload);
}
