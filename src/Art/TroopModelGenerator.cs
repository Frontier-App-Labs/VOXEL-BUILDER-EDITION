using Godot;

namespace VoxelSiege.Art;

/// <summary>
/// Generates voxel character definitions for all troop types.
/// Each troop has a distinct silhouette, proportions, and accessories
/// while sharing the universal skeleton hierarchy.
///
/// All troops are team-colored with the player's color applied to
/// uniform/armor areas, making them instantly identifiable.
/// </summary>
public static class TroopModelGenerator
{
    // ════════════════════════════════════════════════════════════════
    //  SHARED COLOR PALETTE
    // ════════════════════════════════════════════════════════════════

    private static readonly Color Skin = new(0.92f, 0.76f, 0.60f);
    private static readonly Color SkinShadow = new(0.80f, 0.64f, 0.48f);
    private static readonly Color EyeWhite = new(0.96f, 0.96f, 0.98f);
    private static readonly Color Pupil = new(0.06f, 0.06f, 0.10f);
    private static readonly Color BootBrown = new(0.25f, 0.18f, 0.10f);
    private static readonly Color BootSole = new(0.15f, 0.10f, 0.06f);
    private static readonly Color BeltBrown = new(0.40f, 0.30f, 0.15f);
    private static readonly Color GoldAccent = new(0.95f, 0.80f, 0.18f);
    private static readonly Color MetalGrey = new(0.50f, 0.52f, 0.55f);
    private static readonly Color DarkMetal = new(0.30f, 0.30f, 0.35f);

    // ════════════════════════════════════════════════════════════════
    //  COMMANDER (Redesigned - bigger, more detailed, clear leader)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates the Commander character. Larger than troops, has oversized
    /// helmet with plume, epaulettes, and a visible rank insignia.
    /// Grid: 8 wide x 14 tall x 6 deep at 0.08m = ~0.64x1.12x0.48m
    /// </summary>
    public static CharacterDefinition GenerateCommander(Color teamColor)
    {
        Color uniform = teamColor;
        Color uniformDark = teamColor.Darkened(0.25f);
        Color uniformLight = teamColor.Lightened(0.15f);
        Color helmetColor = teamColor.Darkened(0.20f);
        Color helmetHighlight = teamColor.Lightened(0.10f);

        // ── HEAD (5x5x5) with large helmet ──
        Color?[,,] head = new Color?[5, 5, 5];
        Color hair = new Color(0.18f, 0.12f, 0.08f);

        // Full face block — fills the helmet (x=0..4, y=0..2, z=0..3)
        for (int x = 0; x < 5; x++)
            for (int y = 0; y < 3; y++)
                for (int z = 0; z < 4; z++)
                    head[x, y, z] = Skin;

        // Side shading (edges of face)
        for (int y = 0; y < 3; y++)
            for (int z = 0; z < 4; z++)
            {
                head[0, y, z] = SkinShadow;
                head[4, y, z] = SkinShadow;
            }

        // Back of head: hair (z=4)
        for (int x = 0; x < 5; x++)
            for (int y = 0; y < 3; y++)
                head[x, y, 4] = hair;

        // Eyes (row 2, front face z=0): whites on outer edges, pupils inside
        head[0, 2, 0] = EyeWhite; head[1, 2, 0] = Pupil;
        head[3, 2, 0] = Pupil;    head[4, 2, 0] = EyeWhite;

        // Mouth (row 0, front face)
        head[2, 0, 0] = new Color(0.70f, 0.45f, 0.40f);

        // Helmet (rows 3-4) — full coverage
        for (int x = 0; x < 5; x++)
            for (int z = 0; z < 5; z++)
            {
                head[x, 3, z] = helmetColor;
                head[x, 4, z] = helmetHighlight;
            }
        // Helmet rim (darker band at row 3 front and sides)
        for (int x = 0; x < 5; x++)
            head[x, 3, 0] = helmetColor.Darkened(0.15f);
        for (int z = 0; z < 5; z++)
        {
            head[0, 3, z] = helmetColor.Darkened(0.15f);
            head[4, 3, z] = helmetColor.Darkened(0.15f);
        }
        // Badge on front
        head[2, 3, 0] = GoldAccent;

        // ── TORSO (4x4x4) ──
        Color?[,,] torso = new Color?[4, 4, 4];
        for (int x = 0; x < 4; x++)
            for (int y = 0; y < 4; y++)
                for (int z = 0; z < 4; z++)
                    torso[x, y, z] = uniform;
        // Belt row
        for (int x = 0; x < 4; x++)
            for (int z = 0; z < 4; z++)
                torso[x, 0, z] = BeltBrown;
        torso[1, 0, 0] = GoldAccent; torso[2, 0, 0] = GoldAccent; // buckle
        // Button line
        for (int y = 1; y < 4; y++)
            torso[2, y, 0] = uniformLight;
        // Epaulettes (shoulders)
        torso[0, 3, 0] = GoldAccent; torso[0, 3, 1] = GoldAccent;
        torso[3, 3, 0] = GoldAccent; torso[3, 3, 1] = GoldAccent;

        // ── UPPER ARMS (2x3x2) ──
        Color?[,,] leftUpperArm = new Color?[2, 3, 2];
        Color?[,,] rightUpperArm = new Color?[2, 3, 2];
        for (int x = 0; x < 2; x++)
            for (int y = 0; y < 3; y++)
                for (int z = 0; z < 2; z++)
                {
                    leftUpperArm[x, y, z] = uniform;
                    rightUpperArm[x, y, z] = uniform;
                }
        // Shoulder pads
        leftUpperArm[0, 2, 0] = uniformDark; leftUpperArm[1, 2, 0] = uniformDark;
        rightUpperArm[0, 2, 0] = uniformDark; rightUpperArm[1, 2, 0] = uniformDark;

        // ── FOREARMS (2x3x2) with hands ──
        Color?[,,] leftForearm = new Color?[2, 3, 2];
        Color?[,,] rightForearm = new Color?[2, 3, 2];
        for (int x = 0; x < 2; x++)
            for (int z = 0; z < 2; z++)
            {
                leftForearm[x, 2, z] = uniform; rightForearm[x, 2, z] = uniform;
                leftForearm[x, 1, z] = uniform; rightForearm[x, 1, z] = uniform;
                leftForearm[x, 0, z] = Skin; rightForearm[x, 0, z] = Skin; // hands
            }

        // ── THIGHS (2x3x2) ──
        Color?[,,] leftThigh = new Color?[2, 3, 2];
        Color?[,,] rightThigh = new Color?[2, 3, 2];
        for (int x = 0; x < 2; x++)
            for (int y = 0; y < 3; y++)
                for (int z = 0; z < 2; z++)
                {
                    leftThigh[x, y, z] = uniformDark;
                    rightThigh[x, y, z] = uniformDark;
                }

        // ── SHINS (2x3x2) with boots ──
        Color?[,,] leftShin = new Color?[2, 3, 2];
        Color?[,,] rightShin = new Color?[2, 3, 2];
        for (int x = 0; x < 2; x++)
            for (int z = 0; z < 2; z++)
            {
                leftShin[x, 2, z] = uniformDark; rightShin[x, 2, z] = uniformDark;
                leftShin[x, 1, z] = BootBrown; rightShin[x, 1, z] = BootBrown;
                leftShin[x, 0, z] = BootSole; rightShin[x, 0, z] = BootSole;
            }

        return new CharacterDefinition
        {
            Name = "Commander",
            VoxelSize = 0.08f,
            Jitter = 0.003f,
            Head = new BodyPartDefinition
            {
                Name = "Head", Voxels = head,
                PivotVoxelCoords = new Vector3(2.5f, 0, 2.5f),
                AttachOffset = new Vector3(0, 4, 0)
            },
            Torso = new BodyPartDefinition
            {
                Name = "Torso", Voxels = torso,
                PivotVoxelCoords = new Vector3(2, 0, 2),
                AttachOffset = Vector3.Zero
            },
            LeftUpperArm = new BodyPartDefinition
            {
                Name = "LeftUpperArm", Voxels = leftUpperArm,
                PivotVoxelCoords = new Vector3(1, 3, 1),
                AttachOffset = new Vector3(-3, 3.5f, 0)
            },
            LeftForearm = new BodyPartDefinition
            {
                Name = "LeftForearm", Voxels = leftForearm,
                PivotVoxelCoords = new Vector3(1, 3, 1),
                AttachOffset = new Vector3(0, -3, 0)
            },
            RightUpperArm = new BodyPartDefinition
            {
                Name = "RightUpperArm", Voxels = rightUpperArm,
                PivotVoxelCoords = new Vector3(1, 3, 1),
                AttachOffset = new Vector3(3, 3.5f, 0)
            },
            RightForearm = new BodyPartDefinition
            {
                Name = "RightForearm", Voxels = rightForearm,
                PivotVoxelCoords = new Vector3(1, 3, 1),
                AttachOffset = new Vector3(0, -3, 0)
            },
            LeftThigh = new BodyPartDefinition
            {
                Name = "LeftThigh", Voxels = leftThigh,
                PivotVoxelCoords = new Vector3(1, 3, 1),
                AttachOffset = new Vector3(-1, 0, 0)
            },
            LeftShin = new BodyPartDefinition
            {
                Name = "LeftShin", Voxels = leftShin,
                PivotVoxelCoords = new Vector3(1, 3, 1),
                AttachOffset = new Vector3(0, -3, 0)
            },
            RightThigh = new BodyPartDefinition
            {
                Name = "RightThigh", Voxels = rightThigh,
                PivotVoxelCoords = new Vector3(1, 3, 1),
                AttachOffset = new Vector3(1, 0, 0)
            },
            RightShin = new BodyPartDefinition
            {
                Name = "RightShin", Voxels = rightShin,
                PivotVoxelCoords = new Vector3(1, 3, 1),
                AttachOffset = new Vector3(0, -3, 0)
            },
        };
    }

    // ════════════════════════════════════════════════════════════════
    //  INFANTRY TROOP (standard soldier, smallest, most common)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates an Infantry troop. Small, helmet, simple uniform.
    /// Grid parts at 0.06m voxels = ~0.36m wide, 0.54m tall.
    /// Visually distinct from Commander by being shorter and simpler.
    /// </summary>
    public static CharacterDefinition GenerateInfantry(Color teamColor)
    {
        Color uniform = teamColor;
        Color uniformDark = teamColor.Darkened(0.30f);
        Color helmetColor = teamColor.Darkened(0.15f);

        // ── HEAD (4x4x4) simple round helmet ──
        Color?[,,] head = new Color?[4, 4, 4];
        // Face (front z=0 and z=1 for depth)
        for (int x = 1; x < 3; x++)
            for (int y = 0; y < 2; y++)
                for (int z = 0; z < 2; z++)
                    head[x, y, z] = Skin;
        head[0, 1, 0] = SkinShadow; head[3, 1, 0] = SkinShadow;
        // Eyes: whites on outer edges, pupils in center (matching commander style)
        head[0, 1, 0] = EyeWhite; head[1, 1, 0] = Pupil;
        head[2, 1, 0] = Pupil; head[3, 1, 0] = EyeWhite;
        // Helmet
        for (int x = 0; x < 4; x++)
            for (int z = 0; z < 4; z++)
            {
                head[x, 2, z] = helmetColor;
                head[x, 3, z] = helmetColor;
            }
        for (int x = 0; x < 4; x++)
            for (int y = 0; y < 4; y++)
                for (int z = 2; z < 4; z++)
                    head[x, y, z] ??= helmetColor;

        // ── TORSO (3x3x3) ──
        Color?[,,] torso = new Color?[3, 3, 3];
        for (int x = 0; x < 3; x++)
            for (int y = 0; y < 3; y++)
                for (int z = 0; z < 3; z++)
                    torso[x, y, z] = uniform;
        // Belt
        for (int x = 0; x < 3; x++)
            for (int z = 0; z < 3; z++)
                torso[x, 0, z] = BeltBrown;

        // ── ARMS (1x2x2 each) ──
        Color?[,,] leftUpperArm = new Color?[1, 2, 2];
        leftUpperArm[0, 1, 0] = uniform; leftUpperArm[0, 1, 1] = uniform;
        leftUpperArm[0, 0, 0] = Skin; leftUpperArm[0, 0, 1] = Skin;

        Color?[,,] rightUpperArm = new Color?[1, 2, 2];
        rightUpperArm[0, 1, 0] = uniform; rightUpperArm[0, 1, 1] = uniform;
        rightUpperArm[0, 0, 0] = Skin; rightUpperArm[0, 0, 1] = Skin;

        // ── LEFT FOREARM (1x2x2) — supports rifle from below ──
        Color?[,,] leftForearm = new Color?[1, 2, 2];
        leftForearm[0, 1, 0] = uniform; leftForearm[0, 1, 1] = uniform;
        leftForearm[0, 0, 0] = Skin; leftForearm[0, 0, 1] = Skin;

        // ── RIGHT FOREARM (2x5x2) — holding a pixel rifle ──
        // Gun runs along Y axis: y=0 is barrel tip (bottom/down at rest, forward when aiming),
        // y=4 is stock (near elbow). z=0 is front-facing, z=1 is magazine/back side.
        Color WoodStock = new Color(0.42f, 0.28f, 0.14f);
        Color WoodStockDark = new Color(0.30f, 0.18f, 0.08f);
        Color?[,,] rightForearm = new Color?[2, 5, 2];

        // y=0: Barrel muzzle (bottom — points down at rest, forward when aiming)
        rightForearm[1, 0, 0] = MetalGrey;  // barrel tip

        // y=1: Barrel + front sight
        rightForearm[1, 1, 0] = MetalGrey;  // barrel
        rightForearm[1, 1, 1] = DarkMetal;  // sight rail

        // y=2: Hand grip + handguard
        rightForearm[0, 2, 0] = Skin;       // hand gripping
        rightForearm[1, 2, 0] = DarkMetal;  // handguard front
        rightForearm[1, 2, 1] = MetalGrey;  // handguard back

        // y=3: Forearm + receiver
        rightForearm[0, 3, 0] = uniform;    // forearm
        rightForearm[0, 3, 1] = uniform;    // forearm
        rightForearm[1, 3, 0] = MetalGrey;  // receiver front
        rightForearm[1, 3, 1] = DarkMetal;  // magazine / trigger guard

        // y=4: Forearm near elbow + stock
        rightForearm[0, 4, 0] = uniform;    // forearm
        rightForearm[0, 4, 1] = uniform;    // forearm
        rightForearm[1, 4, 0] = WoodStock;      // stock body
        rightForearm[1, 4, 1] = WoodStockDark;  // buttplate

        // ── THIGH (2x2x2) ──
        Color?[,,] thigh = new Color?[2, 2, 2];
        for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
                for (int z = 0; z < 2; z++)
                    thigh[x, y, z] = uniformDark;

        // ── SHIN (2x2x2) with boot ──
        Color?[,,] shin = new Color?[2, 2, 2];
        for (int x = 0; x < 2; x++)
            for (int z = 0; z < 2; z++)
            {
                shin[x, 1, z] = uniformDark;
                shin[x, 0, z] = BootBrown;
            }

        return new CharacterDefinition
        {
            Name = "Infantry",
            VoxelSize = 0.06f,
            Jitter = 0.002f,
            Head = new BodyPartDefinition
            {
                Name = "Head", Voxels = head,
                PivotVoxelCoords = new Vector3(2, 0, 2),
                AttachOffset = new Vector3(0, 3, 0)
            },
            Torso = new BodyPartDefinition
            {
                Name = "Torso", Voxels = torso,
                PivotVoxelCoords = new Vector3(1.5f, 0, 1.5f),
                AttachOffset = Vector3.Zero
            },
            LeftUpperArm = new BodyPartDefinition
            {
                Name = "LeftUpperArm", Voxels = leftUpperArm,
                PivotVoxelCoords = new Vector3(0.5f, 2, 1),
                AttachOffset = new Vector3(-2, 2.5f, 0)
            },
            LeftForearm = new BodyPartDefinition
            {
                Name = "LeftForearm", Voxels = leftForearm,
                PivotVoxelCoords = new Vector3(0.5f, 2, 1),
                AttachOffset = new Vector3(0, -2, 0)
            },
            RightUpperArm = new BodyPartDefinition
            {
                Name = "RightUpperArm", Voxels = rightUpperArm,
                PivotVoxelCoords = new Vector3(0.5f, 2, 1),
                AttachOffset = new Vector3(2, 2.5f, 0)
            },
            RightForearm = new BodyPartDefinition
            {
                Name = "RightForearm", Voxels = rightForearm,
                PivotVoxelCoords = new Vector3(1, 4, 1),
                AttachOffset = new Vector3(0, -2, 0)
            },
            LeftThigh = new BodyPartDefinition
            {
                Name = "LeftThigh", Voxels = thigh,
                PivotVoxelCoords = new Vector3(1, 2, 1),
                AttachOffset = new Vector3(-0.5f, 0, 0)
            },
            LeftShin = new BodyPartDefinition
            {
                Name = "LeftShin", Voxels = shin,
                PivotVoxelCoords = new Vector3(1, 2, 1),
                AttachOffset = new Vector3(0, -2, 0)
            },
            RightThigh = new BodyPartDefinition
            {
                Name = "RightThigh", Voxels = thigh,
                PivotVoxelCoords = new Vector3(1, 2, 1),
                AttachOffset = new Vector3(0.5f, 0, 0)
            },
            RightShin = new BodyPartDefinition
            {
                Name = "RightShin", Voxels = shin,
                PivotVoxelCoords = new Vector3(1, 2, 1),
                AttachOffset = new Vector3(0, -2, 0)
            },
        };
    }

    // ════════════════════════════════════════════════════════════════
    //  DEMOLISHER (heavy troop, bulkier, hard hat, drill accessory)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a Demolisher troop. Stockier than infantry, yellow hard hat,
    /// orange vest over team uniform. Carries a small drill tool.
    /// </summary>
    public static CharacterDefinition GenerateDemolisher(Color teamColor)
    {
        Color uniform = teamColor;
        Color uniformDark = teamColor.Darkened(0.25f);
        Color hardHat = new Color(0.95f, 0.85f, 0.15f); // safety yellow
        Color vest = new Color(0.95f, 0.55f, 0.10f); // hi-vis orange

        // ── HEAD (4x4x4) yellow hard hat ──
        Color?[,,] head = new Color?[4, 4, 4];
        for (int x = 1; x < 3; x++)
            for (int y = 0; y < 2; y++)
                for (int z = 0; z < 2; z++)
                    head[x, y, z] = Skin;
        head[0, 1, 0] = SkinShadow; head[3, 1, 0] = SkinShadow;
        // Eyes: whites on outer edges, pupils in center (matching commander style)
        head[0, 1, 0] = EyeWhite; head[1, 1, 0] = Pupil;
        head[2, 1, 0] = Pupil; head[3, 1, 0] = EyeWhite;
        // Hard hat (wider brim)
        for (int x = 0; x < 4; x++)
            for (int z = 0; z < 4; z++)
            {
                head[x, 2, z] = hardHat;
                head[x, 3, z] = hardHat;
            }
        for (int x = 0; x < 4; x++)
            for (int y = 0; y < 4; y++)
                for (int z = 2; z < 4; z++)
                    head[x, y, z] ??= hardHat;

        // ── TORSO (4x3x3) wider, with hi-vis vest ──
        Color?[,,] torso = new Color?[4, 3, 3];
        for (int x = 0; x < 4; x++)
            for (int y = 0; y < 3; y++)
                for (int z = 0; z < 3; z++)
                    torso[x, y, z] = uniform;
        // Hi-vis vest front
        for (int y = 1; y < 3; y++)
        {
            torso[1, y, 0] = vest; torso[2, y, 0] = vest;
        }
        // Belt
        for (int x = 0; x < 4; x++)
            for (int z = 0; z < 3; z++)
                torso[x, 0, z] = BeltBrown;

        // ── ARMS (2x2x2 - chunkier) ──
        Color?[,,] upperArm = new Color?[2, 2, 2];
        for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
                for (int z = 0; z < 2; z++)
                    upperArm[x, y, z] = uniform;

        // Left forearm with grenade in hand
        Color?[,,] leftForearm = new Color?[2, 2, 3];
        for (int x = 0; x < 2; x++)
            for (int z = 0; z < 2; z++)
            {
                leftForearm[x, 1, z] = uniform;
                leftForearm[x, 0, z] = Skin;
            }
        // Grenade (olive green sphere at z=2, hand level)
        Color grenadeOlive = new Color(0.30f, 0.35f, 0.15f);
        leftForearm[0, 0, 2] = grenadeOlive;
        leftForearm[1, 0, 2] = grenadeOlive;
        leftForearm[0, 1, 2] = grenadeOlive;
        leftForearm[1, 1, 2] = new Color(0.45f, 0.40f, 0.18f); // grenade top/pin

        // Right forearm (normal)
        Color?[,,] rightForearm = new Color?[2, 2, 2];
        for (int x = 0; x < 2; x++)
            for (int z = 0; z < 2; z++)
            {
                rightForearm[x, 1, z] = uniform;
                rightForearm[x, 0, z] = Skin;
            }

        // ── LEGS (2x3x2 - stockier) ──
        Color?[,,] thigh = new Color?[2, 3, 2];
        for (int x = 0; x < 2; x++)
            for (int y = 0; y < 3; y++)
                for (int z = 0; z < 2; z++)
                    thigh[x, y, z] = uniformDark;

        Color?[,,] shin = new Color?[2, 2, 2];
        for (int x = 0; x < 2; x++)
            for (int z = 0; z < 2; z++)
            {
                shin[x, 1, z] = uniformDark;
                shin[x, 0, z] = BootBrown;
            }

        return new CharacterDefinition
        {
            Name = "Demolisher",
            VoxelSize = 0.07f,
            Jitter = 0.002f,
            Head = new BodyPartDefinition
            {
                Name = "Head", Voxels = head,
                PivotVoxelCoords = new Vector3(2, 0, 2),
                AttachOffset = new Vector3(0, 3, 0)
            },
            Torso = new BodyPartDefinition
            {
                Name = "Torso", Voxels = torso,
                PivotVoxelCoords = new Vector3(2, 0, 1.5f),
                AttachOffset = Vector3.Zero
            },
            LeftUpperArm = new BodyPartDefinition
            {
                Name = "LeftUpperArm", Voxels = upperArm,
                PivotVoxelCoords = new Vector3(1, 2, 1),
                AttachOffset = new Vector3(-3, 2.5f, 0)
            },
            LeftForearm = new BodyPartDefinition
            {
                Name = "LeftForearm", Voxels = leftForearm,
                PivotVoxelCoords = new Vector3(1, 2, 1),
                AttachOffset = new Vector3(0, -2, 0)
            },
            RightUpperArm = new BodyPartDefinition
            {
                Name = "RightUpperArm", Voxels = upperArm,
                PivotVoxelCoords = new Vector3(1, 2, 1),
                AttachOffset = new Vector3(3, 2.5f, 0)
            },
            RightForearm = new BodyPartDefinition
            {
                Name = "RightForearm", Voxels = rightForearm,
                PivotVoxelCoords = new Vector3(1, 2, 1),
                AttachOffset = new Vector3(0, -2, 0)
            },
            LeftThigh = new BodyPartDefinition
            {
                Name = "LeftThigh", Voxels = thigh,
                PivotVoxelCoords = new Vector3(1, 3, 1),
                AttachOffset = new Vector3(-1, 0, 0)
            },
            LeftShin = new BodyPartDefinition
            {
                Name = "LeftShin", Voxels = shin,
                PivotVoxelCoords = new Vector3(1, 2, 1),
                AttachOffset = new Vector3(0, -3, 0)
            },
            RightThigh = new BodyPartDefinition
            {
                Name = "RightThigh", Voxels = thigh,
                PivotVoxelCoords = new Vector3(1, 3, 1),
                AttachOffset = new Vector3(1, 0, 0)
            },
            RightShin = new BodyPartDefinition
            {
                Name = "RightShin", Voxels = shin,
                PivotVoxelCoords = new Vector3(1, 2, 1),
                AttachOffset = new Vector3(0, -3, 0)
            },
        };
    }

    // ════════════════════════════════════════════════════════════════
    //  SCOUT (light, fast, goggles, small backpack)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a Scout troop. Smallest, lightest, has goggles and a
    /// small radio backpack. Fastest movement speed.
    /// </summary>
    public static CharacterDefinition GenerateScout(Color teamColor)
    {
        Color uniform = teamColor;
        Color uniformDark = teamColor.Darkened(0.20f);
        Color goggleFrame = DarkMetal;
        Color goggleLens = new Color(0.20f, 0.75f, 0.90f); // teal lens

        // ── HEAD (3x3x3) with goggles ──
        Color?[,,] head = new Color?[3, 3, 3];
        // Face
        head[1, 0, 0] = Skin; head[1, 1, 0] = Skin;
        head[0, 1, 0] = SkinShadow; head[2, 1, 0] = SkinShadow;
        // Cap (painted first so goggles can overwrite front face)
        for (int x = 0; x < 3; x++)
            for (int z = 0; z < 3; z++)
                head[x, 2, z] = uniformDark;
        for (int x = 0; x < 3; x++)
            for (int y = 0; y < 3; y++)
                for (int z = 1; z < 3; z++)
                    head[x, y, z] ??= uniformDark;
        // Goggle frames (row 2, front face) - painted after cap
        head[0, 2, 0] = goggleFrame; head[1, 2, 0] = goggleFrame; head[2, 2, 0] = goggleFrame;
        // Goggle lenses (row 1, front face)
        head[0, 1, 0] = goggleLens; head[2, 1, 0] = goggleLens;

        // ── TORSO (3x3x2) slim ──
        Color?[,,] torso = new Color?[3, 3, 2];
        for (int x = 0; x < 3; x++)
            for (int y = 0; y < 3; y++)
                for (int z = 0; z < 2; z++)
                    torso[x, y, z] = uniform;
        for (int x = 0; x < 3; x++)
            for (int z = 0; z < 2; z++)
                torso[x, 0, z] = BeltBrown;

        // ── ARMS (1x2x1 - slim) ──
        Color?[,,] arm = new Color?[1, 2, 1];
        arm[0, 1, 0] = uniform;
        arm[0, 0, 0] = Skin;

        Color?[,,] forearm = new Color?[1, 2, 1];
        forearm[0, 1, 0] = uniform;
        forearm[0, 0, 0] = Skin;

        // ── LEGS (1x2x2 - slim) ──
        Color?[,,] thigh = new Color?[1, 2, 2];
        for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
                thigh[0, y, z] = uniformDark;

        Color?[,,] shin = new Color?[1, 2, 2];
        for (int z = 0; z < 2; z++)
        {
            shin[0, 1, z] = uniformDark;
            shin[0, 0, z] = BootBrown;
        }

        // Backpack accessory
        Color?[,,] backpack = new Color?[2, 2, 1];
        backpack[0, 0, 0] = DarkMetal; backpack[1, 0, 0] = DarkMetal;
        backpack[0, 1, 0] = MetalGrey; backpack[1, 1, 0] = MetalGrey;

        return new CharacterDefinition
        {
            Name = "Scout",
            VoxelSize = 0.05f,
            Jitter = 0.001f,
            Head = new BodyPartDefinition
            {
                Name = "Head", Voxels = head,
                PivotVoxelCoords = new Vector3(1.5f, 0, 1.5f),
                AttachOffset = new Vector3(0, 3, 0)
            },
            Torso = new BodyPartDefinition
            {
                Name = "Torso", Voxels = torso,
                PivotVoxelCoords = new Vector3(1.5f, 0, 1),
                AttachOffset = Vector3.Zero
            },
            LeftUpperArm = new BodyPartDefinition
            {
                Name = "LeftUpperArm", Voxels = arm,
                PivotVoxelCoords = new Vector3(0.5f, 2, 0.5f),
                AttachOffset = new Vector3(-2, 2.5f, 0)
            },
            LeftForearm = new BodyPartDefinition
            {
                Name = "LeftForearm", Voxels = forearm,
                PivotVoxelCoords = new Vector3(0.5f, 2, 0.5f),
                AttachOffset = new Vector3(0, -2, 0)
            },
            RightUpperArm = new BodyPartDefinition
            {
                Name = "RightUpperArm", Voxels = arm,
                PivotVoxelCoords = new Vector3(0.5f, 2, 0.5f),
                AttachOffset = new Vector3(2, 2.5f, 0)
            },
            RightForearm = new BodyPartDefinition
            {
                Name = "RightForearm", Voxels = forearm,
                PivotVoxelCoords = new Vector3(0.5f, 2, 0.5f),
                AttachOffset = new Vector3(0, -2, 0)
            },
            LeftThigh = new BodyPartDefinition
            {
                Name = "LeftThigh", Voxels = thigh,
                PivotVoxelCoords = new Vector3(0.5f, 2, 1),
                AttachOffset = new Vector3(-0.5f, 0, 0)
            },
            LeftShin = new BodyPartDefinition
            {
                Name = "LeftShin", Voxels = shin,
                PivotVoxelCoords = new Vector3(0.5f, 2, 1),
                AttachOffset = new Vector3(0, -2, 0)
            },
            RightThigh = new BodyPartDefinition
            {
                Name = "RightThigh", Voxels = thigh,
                PivotVoxelCoords = new Vector3(0.5f, 2, 1),
                AttachOffset = new Vector3(0.5f, 0, 0)
            },
            RightShin = new BodyPartDefinition
            {
                Name = "RightShin", Voxels = shin,
                PivotVoxelCoords = new Vector3(0.5f, 2, 1),
                AttachOffset = new Vector3(0, -2, 0)
            },
            Accessory1 = new BodyPartDefinition
            {
                Name = "Backpack", Voxels = backpack,
                PivotVoxelCoords = new Vector3(1, 1, 0),
                AttachOffset = new Vector3(0, 1, 2)
            },
        };
    }
}
