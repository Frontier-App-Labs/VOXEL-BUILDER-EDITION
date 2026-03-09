using Godot;
using System.Collections.Generic;
using VoxelSiege.Building;
using VoxelSiege.Combat;
using VoxelSiege.Core;
using VoxelSiege.Voxel;

namespace VoxelSiege.Networking;

public partial class AntiCheat : Node
{
    private readonly BuildValidator _buildValidator = new BuildValidator();

    public bool ValidateBuildStamp(VoxelWorld world, PlayerData player, BuildZone zone, BuildStamp stamp, VoxelMaterialType material)
    {
        return _buildValidator.ValidateStamp(world, player, zone, stamp, material).Success;
    }

    public bool ValidateCommanderPlacement(VoxelWorld world, BuildZone zone, Vector3I commanderBuildPosition, IReadOnlyCollection<Vector3I> weaponPositions)
    {
        return _buildValidator.ValidateCommanderPlacement(world, zone, commanderBuildPosition, weaponPositions);
    }

    public bool ValidateWeaponPlacement(VoxelWorld world, BuildZone zone, Vector3I weaponBuildPosition)
    {
        return _buildValidator.ValidateWeaponPlacement(world, zone, weaponBuildPosition);
    }

    public bool ValidateAimUpdate(AimingSystem aimingSystem)
    {
        Vector3 direction = aimingSystem.GetDirection();
        return direction != Vector3.Zero && Mathf.Abs(direction.Y) <= 1f;
    }

    public bool ValidateCurrentTurn(PlayerSlot actingPlayer, PlayerSlot? currentPlayer)
    {
        return currentPlayer.HasValue && currentPlayer.Value == actingPlayer;
    }
}
