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
    // Head: rows 6-12 (big chunky head with helmet, full width for oversized look)
    private static readonly Vector3I HeadMin = new(0, 6, 0);
    private static readonly Vector3I HeadMax = new(6, 12, 6);

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
        // HEAD (rows 6-10): big chunky head, full grid width for oversized look
        // =============================================
        // Full head block (skin) — full width x=0..5, full depth z=0..5
        FillBlock(v, 0, 6, 0, 6, 10, 6, skin);

        // Slightly darker skin on sides (only edges, leave face area bright)
        for (int y = 6; y < 10; y++)
        {
            v[0, y, 4] = skinShadow;
            v[0, y, 5] = skinShadow;
            v[5, y, 4] = skinShadow;
            v[5, y, 5] = skinShadow;
        }
        // Slightly darker on back
        for (int y = 6; y < 10; y++)
        {
            for (int x = 1; x < 5; x++)
            {
                v[x, y, 5] = skinShadow;
            }
        }

        // --- Face features across z=0..3 (4 deep) for a round, full face ---
        // Cheek blush on sides of face (z=0..1)
        Color blush = new Color(0.95f, 0.70f, 0.62f);
        v[1, 7, 0] = blush; v[4, 7, 0] = blush;
        v[1, 7, 1] = blush; v[4, 7, 1] = blush;

        // --- Eyes: 2x2 each, pupils at z=0 (always in front), whites at z=1 ---
        // Left eye: x=1..2, y=8..9
        v[1, 8, 0] = eyePupil;  v[2, 8, 0] = eyePupil;   // pupils on front face
        v[1, 9, 0] = eyePupil;  v[2, 9, 0] = eyeWhite;   // pupil bottom-left, white top-right
        v[1, 8, 1] = eyeWhite;  v[2, 8, 1] = eyeWhite;   // whites behind pupils
        v[1, 9, 1] = eyeWhite;  v[2, 9, 1] = eyeWhite;

        // Right eye: x=3..4, y=8..9 (mirrored)
        v[3, 8, 0] = eyePupil;  v[4, 8, 0] = eyePupil;
        v[3, 9, 0] = eyeWhite;  v[4, 9, 0] = eyePupil;
        v[3, 8, 1] = eyeWhite;  v[4, 8, 1] = eyeWhite;
        v[3, 9, 1] = eyeWhite;  v[4, 9, 1] = eyeWhite;

        // Mouth: row 7, z=0 (front face)
        v[2, 7, 0] = new Color(0.75f, 0.50f, 0.40f);
        v[3, 7, 0] = new Color(0.75f, 0.50f, 0.40f);

        // Nose: small bump between eyes at z=0, between the two eye blocks.
        // Eyes occupy x=1..2 and x=3..4, so the nose gap is implicit (no voxel needed).
        // Do NOT overwrite eye pupils — the gap between the eye groups IS the nose.

        // Chin definition (darker skin at bottom of face, z=0..2)
        Color chinShadow = skinShadow;
        for (int z = 0; z < 3; z++)
        {
            v[1, 6, z] = chinShadow;
            v[2, 6, z] = chinShadow;
            v[3, 6, z] = chinShadow;
            v[4, 6, z] = chinShadow;
        }

        // =============================================
        // HAIR (rows 8-10): full coverage on back and sides under helmet
        // =============================================
        Color hair = new Color(0.18f, 0.12f, 0.08f); // dark brown
        // Hair fully covers back of head (z=4..5) from y=7 up to helmet
        for (int y = 7; y < 10; y++)
        {
            for (int x = 0; x < 6; x++)
            {
                v[x, y, 4] = hair;
                v[x, y, 5] = hair;
            }
        }
        // Hair on sides (x=0, x=5) for z=3..5
        for (int y = 7; y < 10; y++)
        {
            v[0, y, 3] = hair;
            v[5, y, 3] = hair;
        }
        // Hair at top of head (row 9, under helmet) — full coverage
        for (int x = 1; x < 5; x++)
        {
            v[x, 9, 3] = hair;
        }

        // =============================================
        // HELMET (rows 10-11): military helmet with badge, full width
        // =============================================
        // Helmet base (row 10) — full width, full depth
        FillBlock(v, 0, 10, 0, 6, 11, 6, helmet);

        // Helmet dome (row 11) — full width, full depth
        FillBlock(v, 0, 11, 0, 6, 12, 6, helmet);

        // Helmet rim - darker band around bottom edge
        for (int x = 0; x < 6; x++)
        {
            v[x, 10, 5] = helmetRim; // back rim
        }
        for (int z = 0; z < 6; z++)
        {
            v[0, 10, z] = helmetRim; // left rim
            v[5, 10, z] = helmetRim; // right rim
        }

        // Front visor brim — only 1 row deep at z=0, does NOT cover face below
        for (int x = 0; x < 6; x++)
        {
            v[x, 10, 0] = helmetRim;
        }

        // Center badge/emblem on front of helmet
        v[2, 11, 0] = helmetBadge;
        v[3, 11, 0] = helmetBadge;

        // Helmet top highlight (slightly lighter center)
        FillBlock(v, 1, 11, 1, 5, 12, 5, teamColor.Lightened(0.05f));
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
