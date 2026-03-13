using Godot;
using System.Collections.Generic;
using VoxelSiege.Art;
using VoxelSiege.Combat;
using VoxelSiege.Commander;
using VoxelSiege.Core;
using VoxelSiege.Voxel;
using CommanderActor = VoxelSiege.Commander.Commander;

namespace VoxelSiege.Army;

public enum TroopAIState { Idle, Moving, Attacking, Dead }

public partial class TroopEntity : Node3D
{
    public PlayerSlot OwnerSlot { get; private set; }
    public TroopType Type { get; private set; }
    public int CurrentHP { get; private set; }
    public TroopAIState AIState { get; private set; } = TroopAIState.Idle;
    public Vector3I CurrentMicrovoxel { get; set; }
    public List<Vector3I>? CurrentPath { get; set; }
    public int PathIndex { get; set; }
    /// <summary>Click-to-move destination set by player or bot AI.</summary>
    public Vector3I? MoveTarget { get; set; }
    /// <summary>Original click target before spread offset, used as fallback if spread target is unreachable.</summary>
    public Vector3I? MoveTargetFallback { get; set; }

    /// <summary>Total damage this troop has dealt so far. Dies when it reaches MaxDamageDealt.</summary>
    public int DamageDealt { get; private set; }


    private Node3D? _modelRoot;
    private VoxelAnimator? _animator;
    private CommanderRagdoll? _ragdoll;
    private Sprite3D? _healthBar;
    private Vector3 _moveFrom;
    private Vector3 _moveTo;
    private float _moveProgress = 1f;
    private float _moveDuration = 0.3f;
    private float _attackPauseTimer;
    private Vector3 _lastImpactDirection = Vector3.Up;

    public void Initialize(TroopType type, PlayerSlot owner, Vector3I startMicrovoxel, Color teamColor)
    {
        OwnerSlot = owner;
        Type = type;
        CurrentHP = TroopDefinitions.Get(type).MaxHP;
        CurrentMicrovoxel = startMicrovoxel;
        // Faster troops have shorter step durations for smooth continuous walking
        _moveDuration = type == TroopType.Demolisher ? 0.3f : 0.22f;

        // Build character model using existing generators
        CharacterDefinition charDef = type switch
        {
            TroopType.Infantry => TroopModelGenerator.GenerateInfantry(teamColor),
            TroopType.Demolisher => TroopModelGenerator.GenerateDemolisher(teamColor),
            // Scout removed — only Infantry and Demolisher
            _ => TroopModelGenerator.GenerateInfantry(teamColor),
        };

        _modelRoot = VoxelCharacterBuilder.Build(charDef);
        VoxelCharacterBuilder.ApplyToonMaterial(_modelRoot, teamColor);
        AddChild(_modelRoot);

        _animator = new VoxelAnimator();
        _animator.Name = $"{type}Animator";
        _animator.HasGun = type == TroopType.Infantry;
        _modelRoot.AddChild(_animator);
        _animator.Initialize(_modelRoot);

        // Position in world (microvoxel coords * 0.5m, centered on voxel)
        GlobalPosition = MicrovoxelToWorld(startMicrovoxel);

        // Mini health bar (billboard sprite)
        _healthBar = new Sprite3D();
        _healthBar.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
        _healthBar.PixelSize = 0.01f;
        _healthBar.Position = new Vector3(0, charDef.VoxelSize * 16f, 0); // above head
        AddChild(_healthBar);
        UpdateHealthBar();

        AddToGroup("Troops");

        // Create ragdoll child for death physics
        _ragdoll = new CommanderRagdoll();
        _ragdoll.Name = "TroopRagdoll";
        _ragdoll.Visible = false;
        AddChild(_ragdoll);
    }

    public bool ApplyDamage(int damage, PlayerSlot? instigator, Vector3? impactOrigin = null)
    {
        if (CurrentHP <= 0) return false;
        CurrentHP = System.Math.Max(0, CurrentHP - damage);
        UpdateHealthBar();

        if (impactOrigin.HasValue)
        {
            _lastImpactDirection = (GlobalPosition - impactOrigin.Value).Normalized();
        }

        if (CurrentHP <= 0)
        {
            SetAIState(TroopAIState.Dead);

            // Activate ragdoll death (like Commander)
            ActivateRagdollDeath();

            // Spawn blood splat
            SpawnBloodSplat();

            // Hide health bar
            if (_healthBar != null)
                _healthBar.Visible = false;

            // Dead troops stay as ragdolls — no QueueFree.
            // Remove from Troops group so they don't interfere with AI/pathfinding.
            RemoveFromGroup("Troops");

            EventBus.Instance?.EmitTroopKilled(new TroopKilledEvent(
                OwnerSlot, Type, GlobalPosition, instigator));
            return true;
        }

        // Flash flinch briefly
        _animator?.SetState(VoxelAnimator.AnimState.Flinch, 1f);
        // Return to previous anim after 0.3s
        var flinchTimer = GetTree().CreateTimer(0.3);
        var previousState = AIState;
        flinchTimer.Timeout += () =>
        {
            if (CurrentHP > 0) SetAIState(previousState);
        };

        return false;
    }

    /// <summary>
    /// Records damage dealt by this troop. If total damage dealt reaches MaxDamageDealt, the troop dies.
    /// </summary>
    public void RecordDamageDealt(int amount)
    {
        DamageDealt += amount;
        int maxDamage = TroopDefinitions.Get(Type).MaxDamageDealt;
        if (maxDamage > 0 && DamageDealt >= maxDamage)
        {
            // Troop has exhausted its damage potential — kill it
            ApplyDamage(CurrentHP, null);
        }
    }


    public void SetAIState(TroopAIState state)
    {
        AIState = state;
        var animState = state switch
        {
            TroopAIState.Idle => VoxelAnimator.AnimState.Idle,
            TroopAIState.Moving => VoxelAnimator.AnimState.Walk,
            TroopAIState.Attacking => Type == TroopType.Infantry ? VoxelAnimator.AnimState.Shoot : VoxelAnimator.AnimState.Attack,
            TroopAIState.Dead => VoxelAnimator.AnimState.Flinch,
            _ => VoxelAnimator.AnimState.Idle,
        };
        float speed = Type == TroopType.Demolisher ? 0.8f : 1f;
        _animator?.SetState(animState, speed);
    }

    public void StartMoveTo(Vector3I targetMicrovoxel)
    {
        _moveFrom = GlobalPosition;
        _moveTo = MicrovoxelToWorld(targetMicrovoxel);
        _moveProgress = 0f;
        CurrentMicrovoxel = targetMicrovoxel;

        // Face movement direction
        Vector3 dir = (_moveTo - _moveFrom).Normalized();
        if (dir.LengthSquared() > 0.001f && _modelRoot != null)
        {
            _modelRoot.LookAt(_moveTo with { Y = _modelRoot.GlobalPosition.Y }, Vector3.Up);
        }
    }

    /// <summary>
    /// Pauses walking for the given duration (e.g., after an attack animation).
    /// </summary>
    public bool IsSurrendered { get; private set; }

    public void Surrender()
    {
        IsSurrendered = true;
        AIState = TroopAIState.Idle;
        _animator?.SetState(VoxelAnimator.AnimState.Surrender);
    }

    public void PauseForAttack(float duration = 0.5f)
    {
        _attackPauseTimer = duration;
    }

    public override void _Process(double delta)
    {
        // Surrendered troops don't move or fight
        if (IsSurrendered) return;

        // Attack pause — play attack anim before resuming walk
        if (_attackPauseTimer > 0f)
        {
            _attackPauseTimer -= (float)delta;
            if (_attackPauseTimer <= 0f && AIState == TroopAIState.Attacking)
                SetAIState(TroopAIState.Idle);
            return;
        }

        if (_moveProgress < 1f)
        {
            // Smooth visual lerp between current step
            _moveProgress = Mathf.Min(1f, _moveProgress + (float)delta / _moveDuration);
            GlobalPosition = _moveFrom.Lerp(_moveTo, _moveProgress);
        }
        else if (CurrentPath != null && PathIndex < CurrentPath.Count)
        {
            Vector3I nextCell = CurrentPath[PathIndex];

            // Check for debris blocking the next step — destroy it if found
            Vector3 nextWorld = MicrovoxelToWorld(nextCell);
            if (DestroyDebrisBlocking(nextWorld))
            {
                // Debris was there and got destroyed — pause briefly for the "smash" effect
                PauseForAttack(0.3f);
                return;
            }

            // Check if another troop occupies the next cell — wait if so
            if (IsCellOccupiedByOtherTroop(nextCell))
            {
                // Another troop is there — idle-wait until they move
                if (AIState == TroopAIState.Moving)
                    SetAIState(TroopAIState.Idle);
                return;
            }

            // Finished current step — advance to next path node
            if (AIState != TroopAIState.Moving)
                SetAIState(TroopAIState.Moving);
            StartMoveTo(CurrentPath[PathIndex]);
            PathIndex++;
        }
        else if (CurrentPath != null && PathIndex >= CurrentPath.Count)
        {
            // Path exhausted — arrived at destination
            MoveTarget = null;
            MoveTargetFallback = null;
            CurrentPath = null;
            PathIndex = 0;
            if (AIState == TroopAIState.Moving)
                SetAIState(TroopAIState.Idle);

            // If we arrived on top of another troop, nudge to an adjacent empty cell
            if (IsCellOccupiedByOtherTroop(CurrentMicrovoxel))
            {
                NudgeOffOverlappingCell();
            }
        }
    }

    private void UpdateHealthBar()
    {
        if (_healthBar == null) return;
        int maxHP = TroopDefinitions.Get(Type).MaxHP;
        // Create a simple colored bar using an Image
        int barWidth = 16;
        int barHeight = 3;
        Image img = Image.CreateEmpty(barWidth, barHeight, false, Image.Format.Rgba8);
        float hpFrac = (float)CurrentHP / maxHP;
        Color barColor = hpFrac > 0.5f ? Colors.Green : hpFrac > 0.25f ? Colors.Yellow : Colors.Red;
        for (int x = 0; x < barWidth; x++)
        {
            for (int y = 0; y < barHeight; y++)
            {
                img.SetPixel(x, y, x < (int)(barWidth * hpFrac) ? barColor : new Color(0.2f, 0.2f, 0.2f, 0.8f));
            }
        }
        var tex = ImageTexture.CreateFromImage(img);
        _healthBar.Texture = tex;
    }

    /// <summary>
    /// Convert the troop's animated body parts into a physics ragdoll.
    /// Mirrors Commander.ActivateRagdollDeath() but with smaller masses for troops.
    /// </summary>
    private void ActivateRagdollDeath()
    {
        if (_ragdoll == null || _modelRoot == null)
            return;

        // Traverse skeleton hierarchy (same structure as Commander)
        Node3D? hips = _modelRoot.GetNodeOrNull<Node3D>("Hips");
        Node3D? spine = hips?.GetNodeOrNull<Node3D>("Spine");
        Node3D? neck = spine?.GetNodeOrNull<Node3D>("Neck");
        Node3D? leftShoulder = spine?.GetNodeOrNull<Node3D>("LeftShoulder");
        Node3D? rightShoulder = spine?.GetNodeOrNull<Node3D>("RightShoulder");
        Node3D? leftHip = hips?.GetNodeOrNull<Node3D>("LeftHip");
        Node3D? rightHip = hips?.GetNodeOrNull<Node3D>("RightHip");

        MeshInstance3D? headMesh = neck?.GetNodeOrNull<MeshInstance3D>("Head");
        MeshInstance3D? torsoMesh = spine?.GetNodeOrNull<MeshInstance3D>("Torso");
        MeshInstance3D? leftArmMesh = leftShoulder?.GetNodeOrNull<MeshInstance3D>("LeftUpperArm");
        MeshInstance3D? rightArmMesh = rightShoulder?.GetNodeOrNull<MeshInstance3D>("RightUpperArm");
        MeshInstance3D? leftLegMesh = leftHip?.GetNodeOrNull<MeshInstance3D>("LeftThigh");
        MeshInstance3D? rightLegMesh = rightHip?.GetNodeOrNull<MeshInstance3D>("RightThigh");

        // Hide the animated model
        _modelRoot.Visible = false;

        // Death impulse direction
        float deathForce = 4f; // smaller than commander's 6f
        Vector3 impulseDir = _lastImpactDirection;

        if (impulseDir.LengthSquared() < 0.01f)
        {
            float angle = GD.Randf() * Mathf.Tau;
            impulseDir = new Vector3(Mathf.Cos(angle), 0.3f, Mathf.Sin(angle)).Normalized();
        }

        // Build ragdoll parts with smaller masses (troops are smaller, voxelSize=0.06 vs 0.08)
        var ragdollParts = new RagdollSkeletonPart[]
        {
            new() { Name = "Head", SourceMesh = headMesh, Joint = neck, Mass = 0.6f },
            new() { Name = "Torso", SourceMesh = torsoMesh, Joint = spine, Mass = 1.8f },
            new() { Name = "LeftArm", SourceMesh = leftArmMesh, Joint = leftShoulder, Mass = 0.5f },
            new() { Name = "RightArm", SourceMesh = rightArmMesh, Joint = rightShoulder, Mass = 0.5f },
            new() { Name = "LeftLeg", SourceMesh = leftLegMesh, Joint = leftHip, Mass = 0.9f },
            new() { Name = "RightLeg", SourceMesh = rightLegMesh, Joint = rightHip, Mass = 0.9f },
        };

        _ragdoll.ActivateFromSkeleton(ragdollParts, GlobalTransform, impulseDir, deathForce);
    }

    /// <summary>
    /// Spawns a burst of blood-red voxel debris from the troop's position.
    /// Mirrors Commander.SpawnBloodSplat() but with slightly fewer particles.
    /// </summary>
    private void SpawnBloodSplat()
    {
        Vector3 pos = GlobalPosition + Vector3.Up * 0.3f; // center-mass height (troops are smaller)
        Vector3 center = pos;

        Color[] bloodColors = new Color[]
        {
            new Color(0.85f, 0.0f, 0.0f),
            new Color(1.0f, 0.0f, 0.0f),
            new Color(0.7f, 0.0f, 0.0f),
            new Color(0.95f, 0.02f, 0.02f),
            new Color(0.55f, 0.0f, 0.0f),
            new Color(1.0f, 0.05f, 0.0f),
        };

        float bloodVoxelScale = 0.04f; // smaller than commander's 0.06f
        for (int i = 0; i < 7; i++)
        {
            Vector3 offset = new Vector3(
                (float)GD.RandRange(-0.3, 0.3),
                (float)GD.RandRange(-0.2, 0.4),
                (float)GD.RandRange(-0.3, 0.3));
            Color color = bloodColors[i % bloodColors.Length];
            FX.DebrisFX.SpawnDebrisWithEmission(this, pos + offset, color, center, 8,
                VoxelMaterialType.Stone, bloodVoxelScale, color);
        }
    }

    /// <summary>
    /// Attacks a voxel, reducing its HP by this troop's attack damage.
    /// Destroys the voxel if HP reaches zero.
    /// Projectile collision: raycasts from troop to target — if a wall blocks
    /// the path, damage is redirected to the first voxel hit (grenade/bullet
    /// impacts the wall instead of phasing through).
    /// </summary>
    public void AttackVoxel(VoxelWorld world, Vector3I targetPos)
    {
        Voxel.Voxel voxel = world.GetVoxel(targetPos);

        // If the target voxel was already destroyed, try to find the nearest
        // solid neighbor so the troop doesn't waste its turn doing nothing.
        if (!voxel.IsSolid || voxel.Material == VoxelMaterialType.Foundation)
        {
            Vector3I? fallback = FindNearestSolidNeighbor(world, targetPos);
            if (!fallback.HasValue) return;
            targetPos = fallback.Value;
            voxel = world.GetVoxel(targetPos);
        }

        TroopStats stats = TroopDefinitions.Get(Type);
        Vector3 targetWorld = new Vector3(
            targetPos.X * GameConfig.MicrovoxelMeters + GameConfig.MicrovoxelMeters * 0.5f,
            targetPos.Y * GameConfig.MicrovoxelMeters + GameConfig.MicrovoxelMeters * 0.5f,
            targetPos.Z * GameConfig.MicrovoxelMeters + GameConfig.MicrovoxelMeters * 0.5f);

        // Projectile collision: raycast from troop eye-height toward the target.
        // If a different voxel blocks the path, redirect damage to that voxel instead.
        Vector3 eyePos = GlobalPosition + Vector3.Up * 0.2f;
        Vector3 dir = (targetWorld - eyePos).Normalized();
        float dist = eyePos.DistanceTo(targetWorld);
        float checkDist = dist - GameConfig.MicrovoxelMeters * 1.5f;
        if (checkDist > 0f && world.RaycastVoxel(eyePos, dir, checkDist, out Vector3I hitPos, out Vector3I _))
        {
            Voxel.Voxel hitVoxel = world.GetVoxel(hitPos);
            if (hitVoxel.IsSolid && hitVoxel.Material != VoxelMaterialType.Foundation)
            {
                targetPos = hitPos;
                voxel = hitVoxel;
                targetWorld = new Vector3(
                    hitPos.X * GameConfig.MicrovoxelMeters + GameConfig.MicrovoxelMeters * 0.5f,
                    hitPos.Y * GameConfig.MicrovoxelMeters + GameConfig.MicrovoxelMeters * 0.5f,
                    hitPos.Z * GameConfig.MicrovoxelMeters + GameConfig.MicrovoxelMeters * 0.5f);
            }
        }

        // Face the final impact point (after any redirect), then fire
        FaceTarget(targetWorld);

        // Spawn visible projectile toward actual impact point
        SpawnAttackProjectile(targetWorld);

        // Apply damage to primary target
        int newHP = System.Math.Max(0, voxel.HitPoints - stats.AttackDamage);
        if (newHP <= 0)
            world.SetVoxel(targetPos, Voxel.Voxel.Air);
        else
            world.SetVoxel(targetPos, voxel.WithHitPoints(newHP).WithDamaged(true));

        int totalDamage = stats.AttackDamage;

        // Demolisher grenades explode: deal half damage to the 6 face-adjacent voxels
        if (Type == TroopType.Demolisher)
        {
            int splashDmg = stats.AttackDamage / 2;
            Vector3I[] neighbors = { Vector3I.Up, Vector3I.Down, Vector3I.Left, Vector3I.Right,
                                     new Vector3I(0, 0, 1), new Vector3I(0, 0, -1) };
            foreach (Vector3I offset in neighbors)
            {
                Vector3I adj = targetPos + offset;
                Voxel.Voxel adjVoxel = world.GetVoxel(adj);
                if (!adjVoxel.IsSolid || adjVoxel.Material == VoxelMaterialType.Foundation) continue;
                int adjNewHP = System.Math.Max(0, adjVoxel.HitPoints - splashDmg);
                if (adjNewHP <= 0)
                    world.SetVoxel(adj, Voxel.Voxel.Air);
                else
                    world.SetVoxel(adj, adjVoxel.WithHitPoints(adjNewHP).WithDamaged(true));
                totalDamage += splashDmg;
            }

            // Grenade explosion SFX + extra debris
            AudioDirector.Instance?.PlaySFX("explosion_impact", targetWorld);
            SpawnGrenadeExplosionVFX(targetWorld);
        }

        RecordDamageDealt(totalDamage);

        // VFX: debris burst at impact point
        SpawnAttackVFX(targetPos, voxel.Material);
    }

    /// <summary>
    /// Attacks an enemy troop directly. Full AttackDamage applied.
    /// </summary>
    public void AttackTroop(TroopEntity enemy)
    {
        TroopStats stats = TroopDefinitions.Get(Type);

        // Face the target
        FaceTarget(enemy.GlobalPosition);

        // Spawn projectile toward enemy
        SpawnAttackProjectile(enemy.GlobalPosition);

        // Apply full damage
        enemy.ApplyDamage(stats.AttackDamage, OwnerSlot, GlobalPosition);
        RecordDamageDealt(stats.AttackDamage);

        GD.Print($"[Troop] {Name} attacked enemy troop {enemy.Name} for {stats.AttackDamage} dmg");
    }

    /// <summary>
    /// Attacks an enemy commander directly. Reduced damage (1/3 of AttackDamage)
    /// since commander only has 15 HP and troops shouldn't one-shot.
    /// </summary>
    public void AttackCommander(CommanderActor commander)
    {
        TroopStats stats = TroopDefinitions.Get(Type);
        int damage = Mathf.Max(1, stats.AttackDamage / 3);

        // Face the target
        FaceTarget(commander.GlobalPosition);

        // Spawn projectile toward commander
        SpawnAttackProjectile(commander.GlobalPosition);

        // Apply damage to commander
        commander.ApplyDamage(damage, OwnerSlot, GlobalPosition);
        RecordDamageDealt(damage);

        GD.Print($"[Troop] {Name} attacked enemy commander for {damage} dmg");
    }

    /// <summary>
    /// Attacks an enemy weapon directly. Full AttackDamage applied.
    /// </summary>
    public void AttackWeapon(WeaponBase weapon)
    {
        TroopStats stats = TroopDefinitions.Get(Type);

        // Face the target
        FaceTarget(weapon.GlobalPosition);

        // Spawn projectile toward weapon
        SpawnAttackProjectile(weapon.GlobalPosition);

        // Apply damage to weapon
        weapon.ApplyDamage(stats.AttackDamage);
        RecordDamageDealt(stats.AttackDamage);

        GD.Print($"[Troop] {Name} attacked enemy weapon {weapon.WeaponId} for {stats.AttackDamage} dmg");
    }

    /// <summary>
    /// Finds the nearest solid non-foundation voxel adjacent to the given position.
    /// Used when the original target was already destroyed so the troop can redirect
    /// its attack to a nearby block instead of wasting its turn.
    /// </summary>
    private static Vector3I? FindNearestSolidNeighbor(VoxelWorld world, Vector3I center)
    {
        Vector3I[] offsets = { Vector3I.Up, Vector3I.Down, Vector3I.Left, Vector3I.Right,
                               new Vector3I(0, 0, 1), new Vector3I(0, 0, -1) };
        foreach (Vector3I offset in offsets)
        {
            Vector3I candidate = center + offset;
            Voxel.Voxel v = world.GetVoxel(candidate);
            if (v.IsSolid && v.Material != VoxelMaterialType.Foundation)
                return candidate;
        }
        return null;
    }

    /// <summary>
    /// Finds and destroys the nearest FallingChunk debris within attack range.
    /// Used when no other targets are available so troops can clear rubble
    /// piled around objectives (commanders, weapons, paths).
    /// Returns true if debris was found and destroyed.
    /// </summary>
    public bool AttackNearbyDebris()
    {
        PhysicsDirectSpaceState3D? space = GetWorld3D()?.DirectSpaceState;
        if (space == null) return false;

        TroopStats stats = TroopDefinitions.Get(Type);
        float rangeMeters = stats.AttackRange * GameConfig.MicrovoxelMeters;

        PhysicsShapeQueryParameters3D query = new();
        query.Shape = new SphereShape3D { Radius = rangeMeters };
        query.Transform = new Transform3D(Basis.Identity, GlobalPosition + Vector3.Up * 0.3f);
        query.CollisionMask = 1 << 3; // Layer 4 (debris/FallingChunks)
        query.CollideWithBodies = true;

        var results = space.IntersectShape(query, 8);
        if (results.Count == 0) return false;

        // Find the closest debris chunk
        Node3D? closest = null;
        float closestDist = float.MaxValue;
        foreach (var result in results)
        {
            if (result.TryGetValue("collider", out var colliderVar) && colliderVar.Obj is Node3D debris)
            {
                if (!IsInstanceValid(debris)) continue;
                float dist = GlobalPosition.DistanceTo(debris.GlobalPosition);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = debris;
                }
            }
        }

        if (closest == null) return false;

        FaceTarget(closest.GlobalPosition);
        SpawnAttackProjectile(closest.GlobalPosition);
        SetAIState(TroopAIState.Attacking);
        closest.QueueFree();
        GD.Print($"[Troop] {Name} cleared debris at {closest.GlobalPosition}");
        return true;
    }

    /// <summary>Faces the model toward a world position.</summary>
    public void FaceTarget(Vector3 targetWorld)
    {
        if (_modelRoot != null)
        {
            Vector3 lookPos = targetWorld with { Y = _modelRoot.GlobalPosition.Y };
            if (lookPos.DistanceSquaredTo(_modelRoot.GlobalPosition) > 0.01f)
                _modelRoot.LookAt(lookPos, Vector3.Up);
        }
    }

    /// <summary>
    /// Spawns a visible projectile (bullet for Infantry, grenade for Demolisher)
    /// that travels from the troop's weapon position to the target.
    /// </summary>
    private void SpawnAttackProjectile(Vector3 targetWorld)
    {
        Vector3 origin = GlobalPosition + Vector3.Up * 0.3f; // chest height
        bool isGrenade = Type == TroopType.Demolisher;

        MeshInstance3D projectile = new MeshInstance3D();
        if (isGrenade)
        {
            // Grenade: dark sphere
            SphereMesh grenadeMesh = new SphereMesh();
            grenadeMesh.Radius = 0.08f;
            grenadeMesh.Height = 0.16f;
            grenadeMesh.RadialSegments = 8;
            grenadeMesh.Rings = 4;
            StandardMaterial3D grenadeMat = new StandardMaterial3D();
            grenadeMat.AlbedoColor = new Color(0.2f, 0.2f, 0.15f);
            grenadeMat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            grenadeMesh.Material = grenadeMat;
            projectile.Mesh = grenadeMesh;
        }
        else
        {
            // Bullet: bright yellow tracer
            BoxMesh bulletMesh = new BoxMesh();
            bulletMesh.Size = new Vector3(0.03f, 0.03f, 0.12f);
            StandardMaterial3D bulletMat = new StandardMaterial3D();
            bulletMat.AlbedoColor = new Color(1f, 0.9f, 0.3f);
            bulletMat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            bulletMat.EmissionEnabled = true;
            bulletMat.Emission = new Color(1f, 0.8f, 0.2f);
            bulletMat.EmissionEnergyMultiplier = 2f;
            bulletMesh.Material = bulletMat;
            projectile.Mesh = bulletMesh;
        }

        GetTree().Root.AddChild(projectile);
        projectile.GlobalPosition = origin;

        // Orient toward target
        Vector3 dir = (targetWorld - origin);
        if (dir.LengthSquared() > 0.001f)
        {
            projectile.LookAt(targetWorld, Vector3.Up);
        }

        // Tween the projectile to the target
        float travelTime = isGrenade ? 0.35f : 0.15f;
        Tween tween = projectile.CreateTween();

        if (isGrenade)
        {
            // Arc trajectory: rise then fall
            Vector3 midPoint = (origin + targetWorld) * 0.5f + Vector3.Up * 1.5f;
            tween.TweenProperty(projectile, "global_position", midPoint, travelTime * 0.5f)
                .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            tween.TweenProperty(projectile, "global_position", targetWorld, travelTime * 0.5f)
                .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        }
        else
        {
            // Straight line
            tween.TweenProperty(projectile, "global_position", targetWorld, travelTime)
                .SetTrans(Tween.TransitionType.Linear);
        }

        tween.TweenCallback(Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(projectile))
                projectile.QueueFree();
        }));
    }

    /// <summary>
    /// Spawns a small particle burst at the attacked voxel position.
    /// </summary>
    private void SpawnAttackVFX(Vector3I targetPos, VoxelMaterialType material)
    {
        Vector3 worldPos = MicrovoxelToWorld(targetPos);
        // Use a neutral debris color based on the material (brownish/grey for most blocks)
        Color debrisColor = material switch
        {
            VoxelMaterialType.Wood => new Color(0.55f, 0.35f, 0.15f),
            VoxelMaterialType.Stone => new Color(0.5f, 0.5f, 0.5f),
            VoxelMaterialType.Metal => new Color(0.6f, 0.6f, 0.65f),
            _ => new Color(0.45f, 0.4f, 0.35f),
        };
        FX.DebrisFX.SpawnDebrisWithEmission(
            this, worldPos, debrisColor, GlobalPosition, 8,
            material, 0.04f, default);
    }

    /// <summary>
    /// Spawns a small explosion burst for Demolisher grenades — fire-colored debris spray.
    /// </summary>
    private void SpawnGrenadeExplosionVFX(Vector3 worldPos)
    {
        Color[] fireColors = {
            new Color(1f, 0.6f, 0.1f),
            new Color(1f, 0.4f, 0.05f),
            new Color(1f, 0.8f, 0.2f),
            new Color(0.9f, 0.2f, 0.0f),
        };
        for (int i = 0; i < 5; i++)
        {
            Vector3 offset = new Vector3(
                (float)GD.RandRange(-0.2, 0.2),
                (float)GD.RandRange(0.0, 0.4),
                (float)GD.RandRange(-0.2, 0.2));
            Color color = fireColors[i % fireColors.Length];
            FX.DebrisFX.SpawnDebrisWithEmission(
                this, worldPos + offset, color, worldPos, 6,
                VoxelMaterialType.Stone, 0.05f, color);
        }
    }

    /// <summary>
    /// Checks if another troop occupies the given microvoxel cell.
    /// Blocks if any other troop claims this cell UNLESS that troop is
    /// actively mid-step away from it (their destination is different).
    /// </summary>
    private bool IsCellOccupiedByOtherTroop(Vector3I cell)
    {
        foreach (Node node in GetTree().GetNodesInGroup("Troops"))
        {
            if (node == this || node is not TroopEntity other) continue;
            if (!IsInstanceValid(other) || other.CurrentHP <= 0) continue;
            if (other.CurrentMicrovoxel != cell) continue;

            // Allow passing through if the other troop is actively walking
            // to a DIFFERENT cell (they're leaving this one)
            if (other._moveProgress < 1f)
            {
                Vector3I movingTo = Utility.MathHelpers.WorldToMicrovoxel(other._moveTo);
                if (movingTo != cell) continue; // they're leaving, let us in
            }

            return true;
        }
        return false;
    }

    /// <summary>
    /// Nudges the troop to an adjacent unoccupied cell when it arrives on top of
    /// another troop. Searches the 4 cardinal neighbors for the nearest empty cell.
    /// </summary>
    private void NudgeOffOverlappingCell()
    {
        Vector3I[] offsets = { new(1, 0, 0), new(-1, 0, 0), new(0, 0, 1), new(0, 0, -1) };
        foreach (Vector3I offset in offsets)
        {
            Vector3I candidate = CurrentMicrovoxel + offset;
            if (!IsCellOccupiedByOtherTroop(candidate))
            {
                StartMoveTo(candidate);
                PathIndex = 0;
                if (AIState != TroopAIState.Moving)
                    SetAIState(TroopAIState.Moving);
                return;
            }
        }
        // All 4 neighbors occupied — try diagonals
        Vector3I[] diags = { new(1, 0, 1), new(1, 0, -1), new(-1, 0, 1), new(-1, 0, -1) };
        foreach (Vector3I offset in diags)
        {
            Vector3I candidate = CurrentMicrovoxel + offset;
            if (!IsCellOccupiedByOtherTroop(candidate))
            {
                StartMoveTo(candidate);
                PathIndex = 0;
                if (AIState != TroopAIState.Moving)
                    SetAIState(TroopAIState.Moving);
                return;
            }
        }
    }

    /// <summary>
    /// Checks if fallen debris (FallingChunk RigidBody3D on layer 4) blocks the given
    /// world position. If found, destroys the debris and returns true so the troop
    /// can continue through rubble piles instead of getting permanently stuck.
    /// </summary>
    private bool DestroyDebrisBlocking(Vector3 worldPos)
    {
        PhysicsDirectSpaceState3D? space = GetWorld3D()?.DirectSpaceState;
        if (space == null) return false;

        // Check a small sphere at the destination for debris (layer 4 = bit 3)
        PhysicsShapeQueryParameters3D query = new();
        query.Shape = new SphereShape3D { Radius = 0.3f };
        query.Transform = new Transform3D(Basis.Identity, worldPos + Vector3.Up * 0.3f);
        query.CollisionMask = 1 << 3; // Layer 4 (debris)
        query.CollideWithBodies = true;

        var results = space.IntersectShape(query, 4);
        if (results.Count == 0) return false;

        // Destroy all debris chunks blocking this cell
        foreach (var result in results)
        {
            if (result.TryGetValue("collider", out var colliderVar) && colliderVar.Obj is Node3D debris)
            {
                if (IsInstanceValid(debris))
                    debris.QueueFree();
            }
        }

        // Face the debris and play attack animation
        FaceTarget(worldPos);
        SetAIState(TroopAIState.Attacking);
        return true;
    }

    /// <summary>
    /// Converts microvoxel coords to world position with a vertical offset so
    /// the character model's feet rest on the ground surface. The skeleton's
    /// legs extend below the root node, so we push the root up by the leg
    /// depth (thigh attach + shin attach = 4 voxels at troop voxelSize).
    /// </summary>
    private Vector3 MicrovoxelToWorld(Vector3I mv)
    {
        const float legOffsetVoxels = 4f; // thigh(2) + shin(2) below hips
        float troopVoxelSize = Type == TroopType.Demolisher ? 0.07f : 0.06f;
        float legOffset = legOffsetVoxels * troopVoxelSize;
        return new Vector3(
            mv.X * GameConfig.MicrovoxelMeters + GameConfig.MicrovoxelMeters * 0.5f,
            mv.Y * GameConfig.MicrovoxelMeters + legOffset,
            mv.Z * GameConfig.MicrovoxelMeters + GameConfig.MicrovoxelMeters * 0.5f);
    }
}
