using Godot;
using VoxelSiege.Commander;
using VoxelSiege.Combat;
using VoxelSiege.Networking;
using CommanderActor = VoxelSiege.Commander.Commander;

namespace VoxelSiege.AI;

public sealed class BotCombatPlanner
{
    public AimUpdatePayload CreateAim(WeaponBase weapon, CommanderActor target, BotDifficulty difficulty)
    {
        Vector3 delta = target.GlobalPosition - weapon.GlobalPosition;
        float yaw = Mathf.Atan2(delta.X, delta.Z);
        float pitch = difficulty == BotDifficulty.Hard ? -0.5f : -0.6f;
        float power = Mathf.Clamp(delta.Length() / 24f, 0.4f, 1f);
        return new AimUpdatePayload(weapon.WeaponId, yaw, pitch, power);
    }
}
