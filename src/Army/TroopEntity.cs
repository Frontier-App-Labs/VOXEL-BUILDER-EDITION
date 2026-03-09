using Godot;
using System.Collections.Generic;
using VoxelSiege.Art;
using VoxelSiege.Core;

namespace VoxelSiege.Army;

public enum TroopAIState { Idle, ExitingBase, Marching, Breaching, Attacking, Returning, EnteringBase, Dead }

public partial class TroopEntity : Node3D
{
    public PlayerSlot OwnerSlot { get; private set; }
    public TroopType Type { get; private set; }
    public int CurrentHP { get; private set; }
    public TroopAIState AIState { get; private set; } = TroopAIState.Idle;
    public Vector3I CurrentMicrovoxel { get; set; }
    public Vector3I HomeMicrovoxel { get; private set; }
    public PlayerSlot TargetEnemy { get; set; }
    public List<Vector3I>? CurrentPath { get; set; }
    public int PathIndex { get; set; }
    /// <summary>Whether the troop has completed its attack and should return home.</summary>
    public bool HasAttacked { get; set; }

    /// <summary>Total damage this troop has dealt so far. Dies when it reaches MaxDamageDealt.</summary>
    public int DamageDealt { get; private set; }

    /// <summary>Remaining ticks before this troop dies automatically.</summary>
    public int RemainingLifespan { get; set; } = GameConfig.TroopLifespanTicks;

    private Node3D? _modelRoot;
    private VoxelAnimator? _animator;
    private Sprite3D? _healthBar;
    private Vector3 _moveFrom;
    private Vector3 _moveTo;
    private float _moveProgress = 1f;
    private float _moveDuration = 0.3f;

    public void Initialize(TroopType type, PlayerSlot owner, Vector3I startMicrovoxel, Color teamColor)
    {
        OwnerSlot = owner;
        Type = type;
        CurrentHP = TroopDefinitions.Get(type).MaxHP;
        CurrentMicrovoxel = startMicrovoxel;
        HomeMicrovoxel = startMicrovoxel;

        // Build character model using existing generators
        CharacterDefinition charDef = type switch
        {
            TroopType.Infantry => TroopModelGenerator.GenerateInfantry(teamColor),
            TroopType.Demolisher => TroopModelGenerator.GenerateDemolisher(teamColor),
            TroopType.Scout => TroopModelGenerator.GenerateScout(teamColor),
            _ => TroopModelGenerator.GenerateInfantry(teamColor),
        };

        _modelRoot = VoxelCharacterBuilder.Build(charDef);
        VoxelCharacterBuilder.ApplyToonMaterial(_modelRoot, teamColor);
        AddChild(_modelRoot);

        _animator = new VoxelAnimator();
        _animator.Name = $"{type}Animator";
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
    }

    public bool ApplyDamage(int damage, PlayerSlot? instigator)
    {
        if (CurrentHP <= 0) return false;
        CurrentHP = System.Math.Max(0, CurrentHP - damage);
        UpdateHealthBar();

        if (CurrentHP <= 0)
        {
            SetAIState(TroopAIState.Dead);
            // Death: play flinch anim, then queue free after delay
            _animator?.SetState(VoxelAnimator.AnimState.Flinch, 1f);
            var timer = GetTree().CreateTimer(1.0);
            timer.Timeout += () => QueueFree();

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

    /// <summary>
    /// Decrements lifespan by one tick. Returns true if the troop died from expiration.
    /// </summary>
    public bool TickLifespan()
    {
        if (CurrentHP <= 0 || AIState == TroopAIState.Dead) return false;
        RemainingLifespan--;
        if (RemainingLifespan <= 0)
        {
            ApplyDamage(CurrentHP, null);
            return true;
        }
        return false;
    }

    public void SetAIState(TroopAIState state)
    {
        AIState = state;
        var animState = state switch
        {
            TroopAIState.Idle => VoxelAnimator.AnimState.Idle,
            TroopAIState.ExitingBase => VoxelAnimator.AnimState.Walk,
            TroopAIState.Marching => VoxelAnimator.AnimState.Walk,
            TroopAIState.Breaching => VoxelAnimator.AnimState.Attack,
            TroopAIState.Attacking => VoxelAnimator.AnimState.Attack,
            TroopAIState.Returning => VoxelAnimator.AnimState.Walk,
            TroopAIState.EnteringBase => VoxelAnimator.AnimState.Walk,
            TroopAIState.Dead => VoxelAnimator.AnimState.Flinch,
            _ => VoxelAnimator.AnimState.Idle,
        };
        float speed = Type == TroopType.Scout ? 1.6f : Type == TroopType.Demolisher ? 0.8f : 1f;
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

    public override void _Process(double delta)
    {
        if (_moveProgress < 1f)
        {
            _moveProgress = Mathf.Min(1f, _moveProgress + (float)delta / _moveDuration);
            GlobalPosition = _moveFrom.Lerp(_moveTo, _moveProgress);
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

    private static Vector3 MicrovoxelToWorld(Vector3I mv)
    {
        return new Vector3(
            mv.X * GameConfig.MicrovoxelMeters + GameConfig.MicrovoxelMeters * 0.5f,
            mv.Y * GameConfig.MicrovoxelMeters,
            mv.Z * GameConfig.MicrovoxelMeters + GameConfig.MicrovoxelMeters * 0.5f);
    }
}
