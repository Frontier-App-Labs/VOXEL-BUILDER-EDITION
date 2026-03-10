using Godot;

namespace VoxelSiege.Art;

/// <summary>
/// Generates a chunky toy fighter jet voxel model for airstrike flyovers.
/// Grid: 12 wide (wingspan) x 4 tall x 8 deep at 0.20m per voxel.
/// Team-colored fuselage with grey wings, cockpit window, tail fin, engine exhausts.
/// </summary>
public static class PlaneModelGenerator
{
    private const float VoxelSize = 0.20f;

    /// <summary>
    /// Generate a fighter jet mesh with team-colored fuselage.
    /// The plane faces toward -Z (forward in Godot).
    /// </summary>
    public static ArrayMesh Generate(Color teamColor)
    {
        int w = 12, h = 4, d = 8;
        Color?[,,] v = new Color?[w, h, d];

        Color team = teamColor.Darkened(0.10f);
        Color teamDark = teamColor.Darkened(0.35f);
        Color teamLight = teamColor.Lightened(0.15f);
        Color grey = new(0.55f, 0.55f, 0.58f);
        Color greyDark = new(0.38f, 0.38f, 0.42f);
        Color greyLight = new(0.68f, 0.68f, 0.72f);
        Color cockpit = new(0.20f, 0.70f, 0.90f);    // cyan glass
        Color cockpitDark = new(0.10f, 0.50f, 0.70f);
        Color exhaust = new(0.25f, 0.22f, 0.20f);
        Color exhaustGlow = new(0.95f, 0.55f, 0.15f); // orange engine glow
        Color white = new(0.92f, 0.92f, 0.92f);

        // ── FUSELAGE (center body) ──
        // Core fuselage: 2 wide (x=5..7), 2 tall (y=1..3), runs full depth
        FillBlock(v, 5, 1, 0, 7, 3, 8, team);

        // Fuselage belly (darker underside)
        FillBlock(v, 5, 0, 1, 7, 1, 7, teamDark);

        // Nose cone (tapers at front, z=0)
        v[5, 1, 0] = teamLight;
        v[6, 1, 0] = teamLight;
        v[5, 2, 0] = team;
        v[6, 2, 0] = team;

        // Fuselage top stripe (lighter accent along spine)
        for (int z = 1; z < 7; z++)
        {
            v[5, 3, z] = teamLight;
            v[6, 3, z] = teamLight;
        }

        // ── COCKPIT ──
        // Windshield at z=1..3 on top of fuselage
        v[5, 3, 1] = cockpit;
        v[6, 3, 1] = cockpit;
        v[5, 3, 2] = cockpitDark;
        v[6, 3, 2] = cockpitDark;
        v[5, 3, 3] = cockpit;
        v[6, 3, 3] = cockpit;

        // Canopy frame
        v[5, 3, 0] = greyDark;
        v[6, 3, 0] = greyDark;

        // ── WINGS ──
        // Left wing: extends from x=0..5, at y=1, z=2..5
        FillBlock(v, 0, 1, 2, 5, 2, 5, grey);
        // Wing leading edge highlight
        for (int x = 0; x < 5; x++)
        {
            v[x, 1, 2] = greyLight;
        }
        // Wing trailing edge darker
        for (int x = 0; x < 5; x++)
        {
            v[x, 1, 4] = greyDark;
        }
        // Wing tip markings (team colored)
        v[0, 1, 2] = teamLight;
        v[0, 1, 3] = team;
        v[0, 1, 4] = teamDark;

        // Right wing: extends from x=7..12, at y=1, z=2..5
        FillBlock(v, 7, 1, 2, 12, 2, 5, grey);
        // Wing leading edge highlight
        for (int x = 7; x < 12; x++)
        {
            v[x, 1, 2] = greyLight;
        }
        // Wing trailing edge darker
        for (int x = 7; x < 12; x++)
        {
            v[x, 1, 4] = greyDark;
        }
        // Wing tip markings (team colored)
        v[11, 1, 2] = teamLight;
        v[11, 1, 3] = team;
        v[11, 1, 4] = teamDark;

        // ── TAIL FIN (vertical stabilizer) ──
        // Vertical fin at rear: x=5..7, y=2..4, z=6..8
        FillBlock(v, 5, 2, 6, 7, 4, 8, greyDark);
        // Fin tip
        v[5, 3, 7] = grey;
        v[6, 3, 7] = grey;

        // ── HORIZONTAL STABILIZERS (small rear wings) ──
        // Left stabilizer
        FillBlock(v, 3, 1, 6, 5, 2, 8, grey);
        v[3, 1, 6] = greyLight;
        v[3, 1, 7] = greyDark;

        // Right stabilizer
        FillBlock(v, 7, 1, 6, 9, 2, 8, grey);
        v[8, 1, 6] = greyLight;
        v[8, 1, 7] = greyDark;

        // ── ENGINE EXHAUSTS ──
        // Two engine nozzles at rear (z=7)
        v[5, 1, 7] = exhaust;
        v[6, 1, 7] = exhaust;
        v[5, 2, 7] = exhaust;
        v[6, 2, 7] = exhaust;

        // Engine glow at very back
        v[5, 1, 7] = exhaustGlow;
        v[6, 1, 7] = exhaustGlow;

        // ── ORDNANCE PYLONS (underwing bombs) ──
        // Small bomb shapes under each wing
        v[2, 0, 3] = greyDark;
        v[3, 0, 3] = greyDark;
        v[9, 0, 3] = greyDark;
        v[8, 0, 3] = greyDark;

        // ── UNIT MARKINGS ──
        // White star/dot on fuselage sides
        v[4, 2, 3] = white;
        v[7, 2, 3] = white;

        VoxelPalette palette = new();
        palette.AddColors(v);
        palette.Build();

        VoxelModelBuilder builder = new()
        {
            VoxelSize = VoxelSize,
            JitterAmount = 0.0f,
            OriginOffset = new Vector3(-w * 0.5f * VoxelSize, -h * 0.5f * VoxelSize, -d * 0.5f * VoxelSize),
        };

        PaletteTexture = palette.Texture;
        return builder.BuildMesh(v, palette);
    }

    /// <summary>
    /// The palette texture from the most recent Generate() call.
    /// </summary>
    public static ImageTexture? PaletteTexture { get; private set; }

    /// <summary>
    /// Create a StandardMaterial3D for the plane mesh using the palette texture.
    /// Slightly metallic for that toy-plane sheen.
    /// </summary>
    public static StandardMaterial3D CreatePlaneMaterial()
    {
        StandardMaterial3D mat = new();
        mat.AlbedoTexture = PaletteTexture;
        mat.TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest;
        mat.Metallic = 0.15f;
        mat.Roughness = 0.65f;
        mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel;
        return mat;
    }

    private static void FillBlock(Color?[,,] v, int x0, int y0, int z0, int x1, int y1, int z1, Color color)
    {
        int maxX = v.GetLength(0);
        int maxY = v.GetLength(1);
        int maxZ = v.GetLength(2);
        for (int x = x0; x < x1; x++)
        {
            for (int y = y0; y < y1; y++)
            {
                for (int z = z0; z < z1; z++)
                {
                    if (x >= 0 && x < maxX && y >= 0 && y < maxY && z >= 0 && z < maxZ)
                    {
                        v[x, y, z] = color;
                    }
                }
            }
        }
    }
}
