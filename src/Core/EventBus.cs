using Godot;
using System;
using VoxelSiege.Army;

namespace VoxelSiege.Core;

public readonly record struct VoxelChangeEvent(Vector3I Position, ushort BeforeData, ushort AfterData, PlayerSlot? Instigator);
public readonly record struct PhaseChangedEvent(GamePhase PreviousPhase, GamePhase CurrentPhase);
public readonly record struct TurnChangedEvent(PlayerSlot CurrentPlayer, int RoundNumber, float TurnTimeSeconds);
public readonly record struct BudgetChangedEvent(PlayerSlot Player, int NewBudget, int Delta);
public readonly record struct CommanderDamagedEvent(PlayerSlot Player, int Damage, int RemainingHealth, Vector3 WorldPosition, bool IsCriticalHit = false);
public readonly record struct CommanderKilledEvent(PlayerSlot Victim, PlayerSlot? Killer, Vector3 WorldPosition);
public readonly record struct WeaponFiredEvent(PlayerSlot Owner, string WeaponId, Vector3 Origin, Vector3 Direction);
public readonly record struct PowerupActivatedEvent(PowerupType Type, PlayerSlot Player, Vector3 WorldPosition);
public readonly record struct PowerupExpiredEvent(PowerupType Type, PlayerSlot Player);
public readonly record struct WeaponDestroyedEvent(PlayerSlot Owner, string WeaponId, Vector3 WorldPosition);
public readonly record struct TroopDeployedEvent(PlayerSlot Owner, int TroopCount, PlayerSlot TargetEnemy);
public readonly record struct TroopKilledEvent(PlayerSlot Owner, TroopType Type, Vector3 WorldPosition, PlayerSlot? Killer);
public readonly record struct TroopDamagedEvent(PlayerSlot Owner, Vector3 WorldPosition, int Damage, int RemainingHP);
public readonly record struct TroopAttackedCommanderEvent(PlayerSlot TroopOwner, PlayerSlot VictimPlayer, int Damage);
public readonly record struct DoorPlacedEvent(PlayerSlot Owner, Vector3I BaseMicrovoxel);
public readonly record struct RailgunBeamFiredEvent(PlayerSlot Owner, Vector3 Start, Vector3 End);

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
    public event Action<PowerupActivatedEvent>? PowerupActivated;
    public event Action<PowerupExpiredEvent>? PowerupExpired;
    public event Action<WeaponDestroyedEvent>? WeaponDestroyed;
    public event Action<TroopDeployedEvent>? TroopDeployed;
    public event Action<TroopKilledEvent>? TroopKilled;
    public event Action<TroopDamagedEvent>? TroopDamaged;
    public event Action<TroopAttackedCommanderEvent>? TroopAttackedCommander;
    public event Action<DoorPlacedEvent>? DoorPlaced;
    public event Action<RailgunBeamFiredEvent>? RailgunBeamFired;

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
    public void EmitPowerupActivated(PowerupActivatedEvent payload) => PowerupActivated?.Invoke(payload);
    public void EmitPowerupExpired(PowerupExpiredEvent payload) => PowerupExpired?.Invoke(payload);
    public void EmitWeaponDestroyed(WeaponDestroyedEvent payload) => WeaponDestroyed?.Invoke(payload);
    public void EmitTroopDeployed(TroopDeployedEvent payload) => TroopDeployed?.Invoke(payload);
    public void EmitTroopKilled(TroopKilledEvent payload) => TroopKilled?.Invoke(payload);
    public void EmitTroopDamaged(TroopDamagedEvent payload) => TroopDamaged?.Invoke(payload);
    public void EmitTroopAttackedCommander(TroopAttackedCommanderEvent payload) => TroopAttackedCommander?.Invoke(payload);
    public void EmitDoorPlaced(DoorPlacedEvent payload) => DoorPlaced?.Invoke(payload);
    public void EmitRailgunBeamFired(RailgunBeamFiredEvent payload) => RailgunBeamFired?.Invoke(payload);
}
