using Godot;

namespace VoxelSiege.Art;

/// <summary>
/// Describes the body part regions (in voxel coordinates) for ragdoll decomposition.
/// </summary>
public struct CommanderBodyPartRegion
{
    public Vector3I Min;
    public Vector3I Max;
    public Vector3 CenterOffset; // local-space center relative to model origin
}

/// <summary>
/// Contains all generated meshes and collision data for the commander's body parts.
/// </summary>
public struct CommanderBodyParts
{
    public ArrayMesh HeadMesh;
    public ArrayMesh TorsoMesh;
    public ArrayMesh LeftArmMesh;
    public ArrayMesh RightArmMesh;
    public ArrayMesh LeftLegMesh;
    public ArrayMesh RightLegMesh;

    public CommanderBodyPartRegion HeadRegion;
    public CommanderBodyPartRegion TorsoRegion;
    public CommanderBodyPartRegion LeftArmRegion;
    public CommanderBodyPartRegion RightArmRegion;
    public CommanderBodyPartRegion LeftLegRegion;
    public CommanderBodyPartRegion RightLegRegion;

    public Color?[,,] FullVoxelData;
    public ArrayMesh FullMesh;
    public ImageTexture? PaletteTexture;
}

/// <summary>
/// Generates the Commander character as a chunky toy-soldier voxel model.
/// Crossy Road / Monument Valley inspired: big round head, stubby body, bold colors.
/// Grid: 6 wide x 12 tall x 6 deep at 0.08m per voxel = 0.48m x 0.96m x 0.48m.
/// The oversized head (half the model height) makes the commander instantly
/// recognizable from any camera angle.
/// </summary>
public static class CommanderModelGenerator
{
    private const int Width = 6;
    private const int Height = 12;
    private const int Depth = 6;
    private const float VoxelSize = 0.08f;

    // Body part region definitions (min inclusive, max exclusive)
    // Head: rows 6-12 (big chunky head with helmet)
    private static readonly Vector3I HeadMin = new(1, 6, 1);
    private static readonly Vector3I HeadMax = new(5, 12, 5);

    // Torso: rows 3-6 (short blocky torso)
    private static readonly Vector3I TorsoMin = new(1, 3, 1);
    private static readonly Vector3I TorsoMax = new(5, 6, 5);

    // Left arm: rows 3-6, left side
    private static readonly Vector3I LeftArmMin = new(0, 3, 1);
    private static readonly Vector3I LeftArmMax = new(1, 6, 5);

    // Right arm: rows 3-6, right side
    private static readonly Vector3I RightArmMin = new(5, 3, 1);
    private static readonly Vector3I RightArmMax = new(6, 6, 5);

    // Left leg: rows 0-3
    private static readonly Vector3I LeftLegMin = new(1, 0, 2);
    private static readonly Vector3I LeftLegMax = new(3, 3, 4);

    // Right leg: rows 0-3
    private static readonly Vector3I RightLegMin = new(3, 0, 2);
    private static readonly Vector3I RightLegMax = new(5, 3, 4);

    /// <summary>
    /// Generate the full commander model with body part decomposition.
    /// </summary>
    public static CommanderBodyParts Generate(Color teamColor)
    {
        Color?[,,] voxels = new Color?[Width, Height, Depth];
        PaintCommander(voxels, teamColor);

        VoxelPalette palette = new();
        palette.AddColors(voxels);
        palette.Build();

        VoxelModelBuilder builder = new()
        {
            VoxelSize = VoxelSize,
            JitterAmount = 0.004f,
            OriginOffset = new Vector3(-Width * 0.5f * VoxelSize, 0, -Depth * 0.5f * VoxelSize),
        };

        CommanderBodyParts parts = new()
        {
            FullVoxelData = voxels,
            FullMesh = builder.BuildMesh(voxels, palette),
            PaletteTexture = palette.Texture,

            HeadMesh = builder.BuildMeshRegion(voxels, HeadMin, HeadMax, palette),
            TorsoMesh = builder.BuildMeshRegion(voxels, TorsoMin, TorsoMax, palette),
            LeftArmMesh = builder.BuildMeshRegion(voxels, LeftArmMin, LeftArmMax, palette),
            RightArmMesh = builder.BuildMeshRegion(voxels, RightArmMin, RightArmMax, palette),
            LeftLegMesh = builder.BuildMeshRegion(voxels, LeftLegMin, LeftLegMax, palette),
            RightLegMesh = builder.BuildMeshRegion(voxels, RightLegMin, RightLegMax, palette),

            HeadRegion = MakeRegion(builder, HeadMin, HeadMax),
            TorsoRegion = MakeRegion(builder, TorsoMin, TorsoMax),
            LeftArmRegion = MakeRegion(builder, LeftArmMin, LeftArmMax),
            RightArmRegion = MakeRegion(builder, RightArmMin, RightArmMax),
            LeftLegRegion = MakeRegion(builder, LeftLegMin, LeftLegMax),
            RightLegRegion = MakeRegion(builder, RightLegMin, RightLegMax),
        };

        return parts;
    }

    private static CommanderBodyPartRegion MakeRegion(VoxelModelBuilder builder, Vector3I min, Vector3I max)
    {
        return new CommanderBodyPartRegion
        {
            Min = min,
            Max = max,
            CenterOffset = builder.GetRegionCenter(min, max),
        };
    }

    private static void PaintCommander(Color?[,,] v, Color teamColor)
    {
        // --- Color palette ---
        Color uniform = teamColor;
        Color uniformDark = teamColor.Darkened(0.25f);
        Color uniformLight = teamColor.Lightened(0.15f);
        Color skin = new Color(0.78f, 0.62f, 0.48f);
        Color skinShadow = new Color(0.65f, 0.50f, 0.38f);
        Color boots = new Color(0.20f, 0.14f, 0.10f);
        Color belt = new Color(0.35f, 0.25f, 0.12f);
        Color buckle = new Color(0.95f, 0.80f, 0.18f);
        Color helmet = teamColor.Darkened(0.20f);
        Color helmetRim = teamColor.Darkened(0.35f);
        Color helmetBadge = new Color(0.95f, 0.82f, 0.20f);
        Color epaulette = new Color(0.95f, 0.82f, 0.18f);
        Color eyeWhite = new Color(0.96f, 0.96f, 0.98f);
        Color eyePupil = new Color(0.06f, 0.06f, 0.10f);

        // =============================================
        // BOOTS (rows 0-1): chunky dark boots
        // =============================================
        FillBlock(v, 1, 0, 2, 3, 1, 4, boots);   // left boot
        FillBlock(v, 3, 0, 2, 5, 1, 4, boots);   // right boot
        // Boot soles - slightly wider at front
        FillBlock(v, 1, 0, 1, 3, 1, 4, boots);   // left sole front
        FillBlock(v, 3, 0, 1, 5, 1, 4, boots);   // right sole front

        // =============================================
        // LEGS (rows 1-3): uniform-colored pants
        // =============================================
        FillBlock(v, 1, 1, 2, 3, 3, 4, uniformDark);  // left leg
        FillBlock(v, 3, 1, 2, 5, 3, 4, uniformDark);  // right leg

        // =============================================
        // TORSO (rows 3-5): military jacket
        // =============================================
        // Belt row (row 3)
        FillBlock(v, 1, 3, 1, 5, 4, 5, belt);
        v[2, 3, 1] = buckle;  // belt buckle on front
        v[3, 3, 1] = buckle;

        // Main jacket body (rows 4-5)
        FillBlock(v, 1, 4, 1, 5, 6, 5, uniform);

        // Front jacket lighter center strip (buttons)
        for (int y = 4; y < 6; y++)
        {
            v[2, y, 1] = uniformLight;
            v[3, y, 1] = uniformLight;
        }

        // Collar at top of torso (row 5, front only)
        v[1, 5, 1] = uniformLight;
        v[4, 5, 1] = uniformLight;

        // =============================================
        // ARMS (rows 3-5): stubby arms, one voxel wide
        // =============================================
        // Left arm
        FillBlock(v, 0, 3, 1, 1, 6, 5, uniform);
        v[0, 5, 1] = epaulette;  // left epaulette
        v[0, 5, 2] = epaulette;
        v[0, 5, 3] = epaulette;
        v[0, 5, 4] = epaulette;
        // Left hand (bottom of arm)
        v[0, 3, 1] = skin;
        v[0, 3, 2] = skin;
        v[0, 3, 3] = skin;
        v[0, 3, 4] = skin;

        // Right arm
        FillBlock(v, 5, 3, 1, 6, 6, 5, uniform);
        v[5, 5, 1] = epaulette;  // right epaulette
        v[5, 5, 2] = epaulette;
        v[5, 5, 3] = epaulette;
        v[5, 5, 4] = epaulette;
        // Right hand
        v[5, 3, 1] = skin;
        v[5, 3, 2] = skin;
        v[5, 3, 3] = skin;
        v[5, 3, 4] = skin;

        // =============================================
        // HEAD (rows 6-10): big chunky 4x4x4 head (still large for chibi)
        // =============================================
        // Full head block (skin)
        FillBlock(v, 1, 6, 1, 5, 10, 5, skin);

        // Slightly darker skin on sides for depth
        for (int y = 6; y < 10; y++)
        {
            for (int z = 1; z < 5; z++)
            {
                v[1, y, z] = skinShadow;
                v[4, y, z] = skinShadow;
            }
        }
        // Slightly darker on back too
        for (int y = 6; y < 10; y++)
        {
            for (int x = 2; x < 4; x++)
            {
                v[x, y, 4] = skinShadow;
            }
        }

        // --- Eyes: 2 voxels tall per eye (white below, dark pupil above) ---
        // Lower eye whites (row 8)
        v[2, 8, 1] = eyeWhite;
        v[3, 8, 1] = eyeWhite;
        // Upper pupil/iris (row 9) — pupils sit on top of whites
        v[2, 9, 1] = eyePupil;
        v[3, 9, 1] = eyePupil;

        // Mouth: tiny line on row 7
        v[2, 7, 1] = new Color(0.75f, 0.50f, 0.40f);
        v[3, 7, 1] = new Color(0.75f, 0.50f, 0.40f);

        // Cheek blush
        v[1, 7, 1] = new Color(0.95f, 0.70f, 0.62f);
        v[4, 7, 1] = new Color(0.95f, 0.70f, 0.62f);

        // =============================================
        // HAIR (row 9-10): visible around sides/back under helmet
        // =============================================
        Color hair = new Color(0.18f, 0.12f, 0.08f); // dark brown
        // Hair on sides of head (row 9, left and right edges)
        for (int z = 2; z < 5; z++)
        {
            v[1, 9, z] = hair;
            v[4, 9, z] = hair;
        }
        // Hair on back of head (rows 9-10)
        for (int x = 2; x < 4; x++)
        {
            v[x, 9, 4] = hair;
            v[x, 10, 4] = hair; // peeks out below helmet back
        }
        // Hair on top corners (row 10, visible around helmet edges)
        v[1, 10, 1] = hair;
        v[4, 10, 1] = hair;
        v[1, 10, 4] = hair;
        v[4, 10, 4] = hair;

        // =============================================
        // HELMET (rows 10-11): military helmet with badge
        // =============================================
        // Helmet base - covers top of head
        FillBlock(v, 1, 10, 1, 5, 11, 5, helmet);

        // Helmet dome (row 11) — slightly inset for rounded look
        FillBlock(v, 1, 11, 1, 5, 12, 5, helmet);

        // Helmet rim - darker band around bottom edge
        for (int x = 1; x < 5; x++)
        {
            v[x, 10, 1] = helmetRim;
            v[x, 10, 4] = helmetRim;
        }
        for (int z = 1; z < 5; z++)
        {
            v[1, 10, z] = helmetRim;
            v[4, 10, z] = helmetRim;
        }

        // Front visor brim - extends forward slightly
        FillBlock(v, 2, 10, 0, 4, 11, 1, helmetRim);

        // Center badge/emblem on front of helmet
        v[2, 11, 1] = helmetBadge;
        v[3, 11, 1] = helmetBadge;

        // Helmet top highlight (slightly lighter)
        FillBlock(v, 2, 11, 2, 4, 12, 4, teamColor.Lightened(0.05f));
    }

    /// <summary>
    /// Fill a rectangular block of voxels with a color. Max is exclusive.
    /// </summary>
    private static void FillBlock(Color?[,,] v, int x0, int y0, int z0, int x1, int y1, int z1, Color color)
    {
        for (int x = x0; x < x1; x++)
        {
            for (int y = y0; y < y1; y++)
            {
                for (int z = z0; z < z1; z++)
                {
                    if (x >= 0 && x < v.GetLength(0) && y >= 0 && y < v.GetLength(1) && z >= 0 && z < v.GetLength(2))
                    {
                        v[x, y, z] = color;
                    }
                }
            }
        }
    }
}
