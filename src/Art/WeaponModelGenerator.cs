using Godot;

namespace VoxelSiege.Art;

/// <summary>
/// Result of generating a weapon model.
/// </summary>
public struct WeaponModelResult
{
    public ArrayMesh Mesh;
    public Godot.ImageTexture? PaletteTexture; // palette texture for uniform rendering
    public Vector3 ForwardDirection; // direction the weapon fires toward
    public Vector3 MuzzleOffset;     // local-space position of the muzzle/fire point
}

/// <summary>
/// Generates all weapon models as chunky toy-style voxel art.
/// Each weapon has a distinct silhouette, signature accent color,
/// and unique visual details so players can identify weapon types at a glance.
/// Weapons use different voxel sizes to communicate power tiers:
///   Drill (0.12m) → Cannon (0.14m) → Mortar (0.15m) → Missile (0.17m) → Railgun (0.18m)
/// </summary>
public static class WeaponModelGenerator
{
    private const float DefaultVoxelSize = 0.15f;

    /// <summary>
    /// Returns the voxel size (in meters) used by a given weapon type.
    /// Weapon debris should use this scale instead of the world MicrovoxelMeters.
    /// </summary>
    public static float GetVoxelSize(string weaponId)
    {
        return weaponId switch
        {
            "cannon" => 0.14f,
            "mortar" => 0.15f,
            "railgun" => 0.18f,
            "missile" => 0.17f,
            "drill" => 0.12f,
            _ => DefaultVoxelSize,
        };
    }

    public static WeaponModelResult Generate(string weaponId, Color teamColor)
    {
        return weaponId switch
        {
            "cannon" => GenerateCannon(teamColor),
            "mortar" => GenerateMortar(teamColor),
            "railgun" => GenerateRailgun(teamColor),
            "missile" => GenerateMissileLauncher(teamColor),
            "drill" => GenerateDrill(teamColor),
            _ => GenerateCannon(teamColor),
        };
    }

    /// <summary>
    /// Cannon: Classic wheeled artillery piece. Rounded barrel, brass details, wheel carriages.
    /// Tier 1 weapon at 0.14m voxels. Fires toward -Z.
    /// Signature color: Brass/gold accents.
    /// </summary>
    private static WeaponModelResult GenerateCannon(Color teamColor)
    {
        const float vs = 0.14f;
        int w = 10, h = 8, d = 12;
        Color?[,,] v = new Color?[w, h, d];

        Color metal = new(0.45f, 0.45f, 0.48f);
        Color metalDark = new(0.28f, 0.28f, 0.32f);
        Color metalLight = new(0.58f, 0.58f, 0.62f);
        Color brass = new(0.78f, 0.65f, 0.22f);
        Color brassLight = new(0.88f, 0.78f, 0.38f);
        Color wood = new(0.45f, 0.30f, 0.15f);
        Color woodDark = new(0.32f, 0.22f, 0.10f);
        Color team = teamColor.Darkened(0.15f);
        Color teamDark = teamColor.Darkened(0.35f);
        Color fuseGlow = new(0.95f, 0.45f, 0.10f);

        // Base platform: team colored
        FillBlock(v, 2, 0, 2, 8, 1, 10, team);
        // Platform rim darker
        for (int x = 2; x < 8; x++) { v[x, 0, 2] = teamDark; v[x, 0, 9] = teamDark; }
        for (int z = 2; z < 10; z++) { v[2, 0, z] = teamDark; v[7, 0, z] = teamDark; }

        // === WHEELS (signature detail - 4 voxels diameter on each side) ===
        // Left wheel (x=0..1)
        v[0, 1, 5] = woodDark; v[0, 2, 4] = wood; v[0, 2, 5] = woodDark; v[0, 2, 6] = wood;
        v[0, 3, 5] = woodDark; v[0, 1, 6] = wood; v[0, 3, 4] = wood; v[0, 3, 6] = wood;
        v[1, 1, 5] = woodDark; v[1, 2, 4] = wood; v[1, 2, 5] = brass; v[1, 2, 6] = wood;
        v[1, 3, 5] = woodDark; v[1, 1, 6] = wood; v[1, 3, 4] = wood; v[1, 3, 6] = wood;
        // Axle hub
        v[1, 2, 5] = brass;

        // Right wheel (x=8..9)
        v[9, 1, 5] = woodDark; v[9, 2, 4] = wood; v[9, 2, 5] = woodDark; v[9, 2, 6] = wood;
        v[9, 3, 5] = woodDark; v[9, 1, 6] = wood; v[9, 3, 4] = wood; v[9, 3, 6] = wood;
        v[8, 1, 5] = woodDark; v[8, 2, 4] = wood; v[8, 2, 5] = brass; v[8, 2, 6] = wood;
        v[8, 3, 5] = woodDark; v[8, 1, 6] = wood; v[8, 3, 4] = wood; v[8, 3, 6] = wood;
        v[8, 2, 5] = brass;

        // === CARRIAGE (wood frame connecting wheels to barrel) ===
        FillBlock(v, 2, 1, 4, 4, 3, 9, wood);   // left carriage
        FillBlock(v, 6, 1, 4, 8, 3, 9, wood);    // right carriage

        // === BARREL (+ cross section for roundness) ===
        // Core barrel (3x3 cross section running z=0..8)
        FillBlock(v, 4, 3, 0, 6, 5, 9, metal);       // 2x2 core
        // Horizontal expansion for + shape
        FillBlock(v, 3, 3, 1, 7, 4, 8, metal);        // wider band
        // Vertical expansion for + shape
        FillBlock(v, 4, 2, 1, 6, 6, 8, metal);        // taller band

        // Barrel tilted up slightly (front voxels one row higher)
        v[4, 5, 0] = metal; v[5, 5, 0] = metal;
        v[4, 6, 0] = metalLight; v[5, 6, 0] = metalLight;

        // === MUZZLE FLARE (front z=0) ===
        FillBlock(v, 3, 2, 0, 7, 6, 1, metalDark);
        v[4, 6, 0] = brassLight; v[5, 6, 0] = brassLight;

        // === BRASS DECORATIVE BANDS ===
        for (int x = 3; x < 7; x++)
        {
            v[x, 5, 2] = brass; v[x, 5, 5] = brass; v[x, 5, 7] = brass;
            v[x, 2, 2] = brass; v[x, 2, 5] = brass;
        }

        // === BREECH (rear end of barrel) ===
        FillBlock(v, 3, 2, 8, 7, 6, 10, brass);
        FillBlock(v, 4, 3, 9, 6, 5, 10, metalDark);

        // === FUSE/IGNITION GLOW at rear ===
        v[4, 5, 9] = fuseGlow;
        v[5, 5, 9] = fuseGlow;
        v[4, 6, 9] = fuseGlow;

        // Trunnion pins on sides
        v[3, 3, 5] = brass; v[6, 3, 5] = brass;
        v[3, 3, 6] = brass; v[6, 3, 6] = brass;

        // Weathered spots on barrel (lighter grey patches)
        v[3, 3, 3] = metalLight; v[6, 4, 6] = metalLight;
        v[5, 5, 4] = metalLight;

        return BuildResult(v, w, h, d, Vector3.Back, vs);
    }

    /// <summary>
    /// Mortar: Angled tube on wide base with sandbag detail, ammo shells, and camo pattern.
    /// Tier 2 weapon at 0.15m voxels. Fires up-and-forward.
    /// Signature color: Olive/camo green with bright sight lens.
    /// </summary>
    private static WeaponModelResult GenerateMortar(Color teamColor)
    {
        const float vs = 0.15f;
        int w = 8, h = 9, d = 8;
        Color?[,,] v = new Color?[w, h, d];

        Color olive = new(0.38f, 0.42f, 0.25f);
        Color oliveDark = new(0.26f, 0.30f, 0.16f);
        Color oliveLight = new(0.48f, 0.55f, 0.32f);
        Color metalDark = new(0.22f, 0.22f, 0.25f);
        Color tan = new(0.60f, 0.52f, 0.35f);
        Color tanDark = new(0.45f, 0.38f, 0.25f);
        Color sightLens = new(0.20f, 0.90f, 0.30f);
        Color shellBrass = new(0.82f, 0.68f, 0.28f);
        Color shellTip = new(0.70f, 0.15f, 0.10f);
        Color team = teamColor.Darkened(0.15f);
        Color teamDark = teamColor.Darkened(0.35f);

        // === WIDE BASE PLATE (wider than before) ===
        FillBlock(v, 0, 0, 0, 8, 1, 8, metalDark);
        // Team color accent ring
        for (int x = 0; x < 8; x++) { v[x, 0, 0] = teamDark; v[x, 0, 7] = teamDark; }
        for (int z = 0; z < 8; z++) { v[0, 0, z] = teamDark; v[7, 0, z] = teamDark; }

        // === SANDBAG-LIKE BUMPS around base ===
        // Front sandbags
        v[1, 1, 0] = tan; v[2, 1, 0] = tanDark; v[3, 1, 0] = tan;
        v[5, 1, 0] = tan; v[6, 1, 0] = tanDark;
        // Side sandbags
        v[0, 1, 2] = tan; v[0, 1, 3] = tanDark; v[0, 1, 5] = tan;
        v[7, 1, 2] = tanDark; v[7, 1, 4] = tan; v[7, 1, 5] = tanDark;
        // Rear sandbags
        v[2, 1, 7] = tan; v[3, 1, 7] = tanDark; v[5, 1, 7] = tan;

        // === BIPOD LEGS with camo pattern ===
        FillBlock(v, 1, 1, 1, 3, 3, 3, olive);
        FillBlock(v, 5, 1, 1, 7, 3, 3, oliveLight);  // camo: lighter patch
        FillBlock(v, 1, 1, 5, 3, 3, 7, oliveLight);   // camo: lighter patch
        FillBlock(v, 5, 1, 5, 7, 3, 7, olive);
        // Camo spots on legs
        v[2, 2, 2] = oliveDark; v[6, 1, 6] = oliveDark;
        v[1, 2, 6] = oliveDark; v[5, 2, 2] = oliveDark;

        // === MORTAR TUBE (angled more clearly) ===
        // Cradle base
        FillBlock(v, 3, 1, 3, 5, 3, 5, metalDark);

        // Tube rising at angle — using diagonal stepping
        // Step 1: y=2-3, z=3-4
        FillBlock(v, 3, 2, 2, 5, 4, 4, olive);
        // Step 2: y=3-5, z=1-3
        FillBlock(v, 3, 3, 1, 5, 5, 3, olive);
        // Step 3: y=4-6, z=0-2
        FillBlock(v, 3, 4, 0, 5, 6, 2, olive);
        // Step 4: top muzzle y=5-8, z=0-1
        FillBlock(v, 3, 5, 0, 5, 8, 1, olive);

        // Muzzle opening
        v[3, 8, 0] = oliveDark; v[4, 8, 0] = oliveDark;
        // Muzzle flare
        v[2, 7, 0] = olive; v[5, 7, 0] = olive;
        v[2, 6, 0] = olive; v[5, 6, 0] = olive;

        // === SIGHT SCOPE (prominent, with bright lens) ===
        v[2, 4, 1] = metalDark;
        v[2, 5, 1] = metalDark;
        v[2, 6, 1] = metalDark;
        v[2, 6, 0] = sightLens;  // bright green lens (visible from any angle)
        v[1, 5, 1] = metalDark;  // sight mount bracket

        // === AMMO SHELLS next to base ===
        // Left shell (small brass cylinder)
        v[0, 1, 4] = shellBrass;
        v[0, 2, 4] = shellBrass;
        v[0, 3, 4] = shellTip;  // red tip

        // Right shell
        v[7, 1, 3] = shellBrass;
        v[7, 2, 3] = shellBrass;
        v[7, 3, 3] = shellTip;

        // Lying shell
        v[6, 1, 0] = shellBrass; v[7, 1, 0] = shellBrass;

        return BuildResult(v, w, h, d, (Vector3.Up + Vector3.Back).Normalized(), vs);
    }

    /// <summary>
    /// Railgun: Long sleek twin-rail design with pulsing cyan energy core and capacitor coils.
    /// Tier 3 weapon at 0.18m voxels (largest, most imposing). Fires toward -Z.
    /// Signature color: Cyan energy glow.
    /// </summary>
    private static WeaponModelResult GenerateRailgun(Color teamColor)
    {
        const float vs = 0.18f;
        // Narrow profile (w=6) with long barrel (d=14) — sleek twin-rail silhouette.
        // Previous w=10 made the weapon as wide as it was long, looking like a
        // flat platform rather than a directional gun.
        int w = 6, h = 6, d = 14;
        Color?[,,] v = new Color?[w, h, d];

        Color steel = new(0.35f, 0.37f, 0.40f);
        Color steelDark = new(0.18f, 0.20f, 0.24f);
        Color steelLight = new(0.50f, 0.52f, 0.55f);
        Color cyan = new(0.20f, 0.85f, 0.95f);
        Color cyanGlow = new(0.50f, 1.0f, 1.0f);
        Color cyanBright = new(0.80f, 1.0f, 1.0f);
        Color coilDark = new(0.12f, 0.12f, 0.16f);
        Color team = teamColor.Darkened(0.15f);
        Color teamDark = teamColor.Darkened(0.35f);

        // Base platform (narrower)
        FillBlock(v, 1, 0, 3, 5, 1, 12, team);
        for (int x = 1; x < 5; x++) { v[x, 0, 3] = teamDark; v[x, 0, 11] = teamDark; }

        // === LEFT RAIL (x=0..1, close to center) ===
        FillBlock(v, 0, 1, 0, 2, 3, 13, steel);
        // Tapered front
        FillBlock(v, 0, 1, 0, 2, 3, 1, steelDark);
        v[0, 2, 0] = steelLight; v[1, 2, 0] = steelLight; // front edge highlight
        // Top highlight strip
        for (int z = 1; z < 12; z++) { v[0, 2, z] = steelLight; v[1, 2, z] = steelLight; }

        // === RIGHT RAIL (x=4..5, close to center) ===
        FillBlock(v, 4, 1, 0, 6, 3, 13, steel);
        FillBlock(v, 4, 1, 0, 6, 3, 1, steelDark);
        v[4, 2, 0] = steelLight; v[5, 2, 0] = steelLight;
        for (int z = 1; z < 12; z++) { v[4, 2, z] = steelLight; v[5, 2, z] = steelLight; }

        // === ENERGY CHANNEL between rails (x=2..3) ===
        FillBlock(v, 2, 1, 2, 4, 2, 11, cyan);
        // Bright energy pulses along the channel
        for (int z = 2; z < 11; z += 2)
        {
            v[2, 1, z] = cyanGlow;
            v[3, 1, z] = cyanGlow;
        }

        // === SPARKS AT MUZZLE (bright white voxels at front) ===
        v[1, 1, 0] = cyanBright; v[4, 1, 0] = cyanBright;
        v[2, 1, 0] = cyanBright; v[3, 1, 0] = cyanBright;
        v[1, 2, 0] = cyanGlow; v[4, 2, 0] = cyanGlow;
        v[2, 2, 1] = cyanGlow; v[3, 2, 1] = cyanGlow;

        // === CAPACITOR COILS on top (rings of dark metal) ===
        // Coil 1
        FillBlock(v, 1, 3, 4, 5, 4, 5, coilDark);
        v[2, 3, 4] = cyan; v[3, 3, 4] = cyan; // glow between coils
        // Coil 2
        FillBlock(v, 1, 3, 6, 5, 4, 7, coilDark);
        v[2, 3, 6] = cyan; v[3, 3, 6] = cyan;
        // Coil 3
        FillBlock(v, 1, 3, 8, 5, 4, 9, coilDark);
        v[2, 3, 8] = cyan; v[3, 3, 8] = cyan;

        // === TOP HOUSING connecting coils ===
        FillBlock(v, 2, 3, 4, 4, 4, 9, steelDark);
        // Energy indicators on housing
        v[2, 3, 5] = cyan; v[3, 3, 5] = cyan;
        v[2, 3, 7] = cyanGlow; v[3, 3, 7] = cyanGlow;

        // === REAR POWER UNIT (bulkier) ===
        FillBlock(v, 1, 1, 11, 5, 5, 14, steelDark);
        // Power core glow visible from back
        v[2, 2, 12] = cyan; v[3, 2, 12] = cyan;
        v[2, 3, 12] = cyanGlow; v[3, 3, 12] = cyanGlow;
        v[2, 2, 13] = cyanBright; v[3, 2, 13] = cyanBright;
        v[2, 3, 13] = cyanBright; v[3, 3, 13] = cyanBright;
        // Side vents
        v[1, 3, 13] = steelLight; v[4, 3, 13] = steelLight;
        v[1, 4, 13] = steelLight; v[4, 4, 13] = steelLight;
        // Top vent
        v[2, 4, 12] = steelLight; v[3, 4, 12] = steelLight;

        return BuildResult(v, w, h, d, Vector3.Back, vs);
    }

    /// <summary>
    /// Missile Launcher: Wide angled launcher rack with visible missile nosecones,
    /// exhaust vents, and control panel. Wider than tall for distinct profile.
    /// Tier 3 weapon at 0.17m voxels. Fires toward -Z.
    /// Signature color: Red/yellow warning markings.
    /// </summary>
    private static WeaponModelResult GenerateMissileLauncher(Color teamColor)
    {
        const float vs = 0.17f;
        int w = 10, h = 6, d = 8;
        Color?[,,] v = new Color?[w, h, d];

        Color armyGreen = new(0.28f, 0.36f, 0.22f);
        Color armyDark = new(0.18f, 0.24f, 0.14f);
        Color camoGreen = new(0.32f, 0.40f, 0.20f);
        Color camoTan = new(0.45f, 0.40f, 0.28f);
        Color metalDark = new(0.22f, 0.22f, 0.25f);
        Color red = new(0.85f, 0.18f, 0.12f);
        Color white = new(0.90f, 0.90f, 0.90f);
        Color yellow = new(0.95f, 0.80f, 0.15f);
        Color screenGreen = new(0.10f, 0.80f, 0.20f);
        Color team = teamColor.Darkened(0.15f);
        Color teamDark = teamColor.Darkened(0.35f);

        // Base platform (wider)
        FillBlock(v, 1, 0, 1, 9, 1, 7, team);
        for (int x = 1; x < 9; x++) { v[x, 0, 1] = teamDark; v[x, 0, 6] = teamDark; }

        // === LAUNCH RACK (tilted back — rear is taller than front) ===
        // Front face lower
        FillBlock(v, 1, 1, 1, 9, 4, 3, armyGreen);
        // Middle section
        FillBlock(v, 1, 1, 3, 9, 5, 5, armyGreen);
        // Rear section taller (tilted back effect)
        FillBlock(v, 1, 1, 5, 9, 6, 7, armyGreen);

        // Darker edges
        for (int y = 1; y < 6; y++)
        {
            v[1, y, 1] = armyDark; v[8, y, 1] = armyDark;
            v[1, y, 6] = armyDark; v[8, y, 6] = armyDark;
        }

        // === CAMO NETTING on top (irregular dark green voxels) ===
        v[2, 5, 5] = camoGreen; v[4, 5, 5] = camoTan; v[6, 5, 5] = camoGreen;
        v[3, 5, 6] = camoTan; v[5, 5, 6] = camoGreen; v[7, 5, 6] = camoTan;
        v[2, 5, 6] = camoGreen; v[8, 5, 5] = camoTan;

        // === 4 LAUNCH TUBES with VISIBLE MISSILE NOSECONES ===
        // Tubes are dark holes; missile tips poke out as red/white
        // Top-left tube
        v[2, 3, 1] = metalDark; v[3, 3, 1] = metalDark;
        v[2, 3, 2] = metalDark; v[3, 3, 2] = metalDark;
        v[2, 3, 0] = red;   // missile nosecone poking out!
        v[3, 3, 0] = white;

        // Top-right tube
        v[6, 3, 1] = metalDark; v[7, 3, 1] = metalDark;
        v[6, 3, 2] = metalDark; v[7, 3, 2] = metalDark;
        v[6, 3, 0] = white;
        v[7, 3, 0] = red;

        // Bottom-left tube
        v[2, 2, 1] = metalDark; v[3, 2, 1] = metalDark;
        v[2, 2, 2] = metalDark; v[3, 2, 2] = metalDark;
        v[2, 2, 0] = white;
        v[3, 2, 0] = red;

        // Bottom-right tube
        v[6, 2, 1] = metalDark; v[7, 2, 1] = metalDark;
        v[6, 2, 2] = metalDark; v[7, 2, 2] = metalDark;
        v[6, 2, 0] = red;
        v[7, 2, 0] = white;

        // Tube divider cross
        v[4, 2, 1] = armyDark; v[5, 2, 1] = armyDark;
        v[4, 3, 1] = armyDark; v[5, 3, 1] = armyDark;
        v[4, 2, 0] = armyDark; v[5, 2, 0] = armyDark;
        v[4, 3, 0] = armyDark; v[5, 3, 0] = armyDark;

        // === EXHAUST VENTS on back (dark slots) ===
        v[2, 2, 7] = metalDark; v[3, 2, 7] = metalDark;
        v[6, 2, 7] = metalDark; v[7, 2, 7] = metalDark;
        v[2, 3, 7] = metalDark; v[3, 3, 7] = metalDark;
        v[6, 3, 7] = metalDark; v[7, 3, 7] = metalDark;

        // === CONTROL PANEL on right side ===
        v[9, 2, 3] = metalDark; v[9, 2, 4] = metalDark;
        v[9, 3, 3] = metalDark; v[9, 3, 4] = screenGreen; // green screen dot

        // === WARNING STRIPES (more prominent) ===
        // Left side chevrons
        for (int z = 2; z < 6; z++)
        {
            v[1, 3, z] = (z % 2 == 0) ? yellow : armyDark;
            v[1, 2, z] = (z % 2 == 0) ? armyDark : yellow;
        }
        // Right side chevrons
        for (int z = 2; z < 6; z++)
        {
            v[8, 3, z] = (z % 2 == 0) ? yellow : armyDark;
            v[8, 2, z] = (z % 2 == 0) ? armyDark : yellow;
        }

        // Red danger marking on top
        v[4, 4, 3] = red; v[5, 4, 3] = red;
        v[4, 4, 4] = red; v[5, 4, 4] = red;

        return BuildResult(v, w, h, d, Vector3.Back, vs);
    }

    /// <summary>
    /// Drill: Industrial mining drill with spiral bit, chunky orange motor, warning chevrons.
    /// Tier 1 weapon at 0.12m voxels (smallest, least threatening). Fires toward -Z.
    /// Signature color: Safety orange with black/yellow chevrons.
    /// </summary>
    private static WeaponModelResult GenerateDrill(Color teamColor)
    {
        const float vs = 0.12f;
        int w = 8, h = 9, d = 14;
        Color?[,,] v = new Color?[w, h, d];

        Color orange = new(0.92f, 0.58f, 0.12f);
        Color orangeDark = new(0.72f, 0.42f, 0.08f);
        Color yellow = new(0.95f, 0.82f, 0.18f);
        Color black = new(0.10f, 0.10f, 0.12f);
        Color metal = new(0.50f, 0.50f, 0.54f);
        Color metalDark = new(0.30f, 0.30f, 0.34f);
        Color metalLight = new(0.62f, 0.62f, 0.66f);
        Color sparkWhite = new(0.95f, 0.92f, 0.80f);
        Color team = teamColor.Darkened(0.15f);

        // Base housing platform
        FillBlock(v, 1, 0, 7, 7, 1, 14, team);

        // === MOTOR HOUSING (big chunky block, orange) ===
        FillBlock(v, 1, 1, 8, 7, 6, 13, orange);
        // Motor top cap
        FillBlock(v, 2, 6, 9, 6, 7, 12, orangeDark);

        // === WARNING CHEVRONS (prominent black/yellow) ===
        // Front face of motor
        for (int x = 1; x < 7; x++)
        {
            v[x, 5, 8] = (x % 2 == 0) ? yellow : black;
            v[x, 4, 8] = (x % 2 == 0) ? black : yellow;
        }
        // Left side chevrons
        for (int z = 8; z < 13; z++)
        {
            v[1, 5, z] = (z % 2 == 0) ? yellow : black;
            v[1, 4, z] = (z % 2 == 0) ? black : yellow;
        }
        // Right side chevrons
        for (int z = 8; z < 13; z++)
        {
            v[6, 5, z] = (z % 2 == 0) ? yellow : black;
            v[6, 4, z] = (z % 2 == 0) ? black : yellow;
        }

        // === EXHAUST PIPE on top ===
        FillBlock(v, 3, 7, 10, 5, 9, 12, metalDark);
        v[3, 8, 10] = metalDark; v[4, 8, 10] = metalDark;
        // Smoke opening
        v[3, 8, 11] = black; v[4, 8, 11] = black;

        // === EXHAUST VENTS on back ===
        v[2, 3, 13] = metalDark; v[3, 3, 13] = black; v[4, 3, 13] = metalDark;
        v[5, 3, 13] = black;
        v[2, 2, 13] = black; v[3, 2, 13] = metalDark; v[4, 2, 13] = black;
        v[5, 2, 13] = metalDark;

        // === DRIVE SHAFT ===
        FillBlock(v, 3, 2, 5, 5, 5, 8, metalDark);

        // === DRILL CHUCK (wider section) ===
        FillBlock(v, 2, 1, 4, 6, 6, 6, metal);

        // === DRILL BIT (conical with spiral grooves) ===
        // Wide base ring
        FillBlock(v, 1, 1, 3, 7, 6, 5, metal);

        // Medium ring
        FillBlock(v, 2, 2, 2, 6, 5, 3, metalLight);

        // Narrowing
        FillBlock(v, 2, 2, 1, 6, 5, 2, metalLight);

        // Narrow tip
        v[3, 3, 0] = metalLight; v[4, 3, 0] = metalLight;
        v[3, 4, 0] = metalLight; v[4, 4, 0] = metalLight;

        // Spiral fluting (darker grooves at different z depths + rotational positions)
        v[1, 4, 4] = metalDark; v[6, 2, 4] = metalDark;
        v[2, 1, 3] = metalDark; v[5, 5, 3] = metalDark;
        v[1, 3, 3] = metalDark; v[6, 4, 3] = metalDark;
        v[2, 5, 2] = metalDark; v[5, 2, 2] = metalDark;
        v[3, 5, 1] = metalDark; v[4, 2, 1] = metalDark;
        v[2, 4, 1] = metalDark; v[5, 3, 1] = metalDark;

        // === SPARKS at drill tip ===
        v[3, 4, 0] = sparkWhite;
        v[4, 3, 0] = yellow;

        return BuildResult(v, w, h, d, Vector3.Back, vs);
    }

    private static WeaponModelResult BuildResult(Color?[,,] voxels, int w, int h, int d, Vector3 forward, float voxelSize = DefaultVoxelSize)
    {
        VoxelPalette palette = new();
        palette.AddColors(voxels);
        palette.Build();

        VoxelModelBuilder builder = new()
        {
            VoxelSize = voxelSize,
            JitterAmount = 0.0f,
            OriginOffset = new Vector3(-w * 0.5f * voxelSize, 0, -d * 0.5f * voxelSize),
        };

        return new WeaponModelResult
        {
            Mesh = builder.BuildMesh(voxels, palette),
            PaletteTexture = palette.Texture,
            ForwardDirection = forward,
            MuzzleOffset = new Vector3(0, h * 0.5f * voxelSize, -d * 0.5f * voxelSize),
        };
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
