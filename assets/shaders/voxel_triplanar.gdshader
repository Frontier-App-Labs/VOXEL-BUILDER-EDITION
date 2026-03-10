shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// -- Uniforms --
uniform vec4 player_tint : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float damage_blend : hint_range(0.0, 1.0) = 0.0;
uniform float triplanar_scale : hint_range(0.1, 4.0) = 1.0;
uniform float ao_strength : hint_range(0.0, 1.0) = 0.65;
uniform float edge_highlight_strength : hint_range(0.0, 1.0) = 0.3;
uniform float noise_strength : hint_range(0.0, 0.15) = 0.04;

// -- Texture atlas (AI-generated material textures) --
uniform sampler2D material_atlas : source_color, filter_nearest, repeat_enable;
uniform bool use_material_atlas = false;
uniform float texture_blend : hint_range(0.0, 1.0) = 0.85; // how much texture vs vertex color
uniform vec2 atlas_tile_size = vec2(0.125, 0.5); // normalized tile size in atlas (e.g. 32/256, 32/64)

// -- Wind globals (set by WindAnimator.cs) --
global uniform float wind_time;
global uniform float wind_strength;
global uniform vec2 wind_direction;

// -- Grid visibility (set by GameManager: 1.0 in build mode, 0.0 otherwise) --
global uniform float edge_grid_enabled;

// -- Kill cam: dither-discard to see through blocks (stays in opaque pipeline) --
uniform float kill_cam_dissolve : hint_range(0.0, 1.0) = 0.0;
uniform float kill_cam_ground_y = 3.0;

// -- Varying --
varying vec3 world_pos;
varying vec3 world_normal;
varying vec4 vtx_color;
varying vec2 vtx_uv;

// ============================================================
// Hash-based noise (no texture dependency)
// ============================================================
float hash31(vec3 p) {
    p = fract(p * vec3(0.1031, 0.1030, 0.0973));
    p += dot(p, p.yxz + 33.33);
    return fract((p.x + p.y) * p.z);
}

float value_noise(vec3 p) {
    vec3 i = floor(p);
    vec3 f = fract(p);
    f = f * f * (3.0 - 2.0 * f); // smoothstep interp

    float a = hash31(i);
    float b = hash31(i + vec3(1.0, 0.0, 0.0));
    float c = hash31(i + vec3(0.0, 1.0, 0.0));
    float d = hash31(i + vec3(1.0, 1.0, 0.0));
    float e = hash31(i + vec3(0.0, 0.0, 1.0));
    float g = hash31(i + vec3(1.0, 0.0, 1.0));
    float h = hash31(i + vec3(0.0, 1.0, 1.0));
    float k = hash31(i + vec3(1.0, 1.0, 1.0));

    return mix(
        mix(mix(a, b, f.x), mix(c, d, f.x), f.y),
        mix(mix(e, g, f.x), mix(h, k, f.x), f.y),
        f.z
    );
}

// Two-octave noise for subtle surface variation
float surface_noise(vec3 p) {
    return value_noise(p) * 0.65 + value_noise(p * 2.13) * 0.35;
}

// ============================================================
// Damage crack pattern (2D Voronoi, projected per triplanar face)
// ============================================================
float crack_pattern_2d(vec2 p) {
    vec2 ip = floor(p);
    vec2 fp = fract(p);

    float min_dist1 = 1.0;
    float min_dist2 = 1.0;

    for (int x = -1; x <= 1; x++) {
        for (int y = -1; y <= 1; y++) {
            vec2 neighbor = vec2(float(x), float(y));
            vec3 hash_input = vec3(ip + neighbor, 0.0);
            vec2 point = vec2(
                hash31(hash_input),
                hash31(hash_input + vec3(31.7, 47.3, 0.0))
            );
            float d = length(neighbor + point - fp);
            if (d < min_dist1) {
                min_dist2 = min_dist1;
                min_dist1 = d;
            } else if (d < min_dist2) {
                min_dist2 = d;
            }
        }
    }

    // Edge distance gives crack lines
    float edge = min_dist2 - min_dist1;
    return 1.0 - smoothstep(0.0, 0.08, edge);
}

// ============================================================
// Vertex shader
// ============================================================
void vertex() {
    vec3 ws = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;

    // Wind displacement for ground-level vegetation only (grass/leaves, NOT structures)
    // Tight color check: Leaves material has R<0.25, G>0.35 — excludes Dirt (R=0.29) and all player materials
    // Height limit: only near ground (y 2-5) to prevent structures from swaying
    bool is_vegetation = COLOR.g > 0.35 && COLOR.r < 0.26 && ws.y > 1.0 && ws.y < 20.0;
    if (is_vegetation) {
        // Grass sways gently — decreases with height so only short plants move
        float height_factor = clamp(1.0 - (ws.y - 2.0) * 0.35, 0.0, 1.0);
        // Spatial variation so each plant sways differently
        float phase = ws.x * 0.7 + ws.z * 0.5;
        float sway = sin(wind_time * 2.0 + phase) * 0.5 + sin(wind_time * 3.1 + phase * 1.3) * 0.3;
        ws.x += sway * wind_strength * height_factor * wind_direction.x;
        ws.z += sway * wind_strength * height_factor * wind_direction.y;
        // Slight vertical bob
        ws.y += sin(wind_time * 1.7 + phase * 0.9) * wind_strength * height_factor * 0.1;
        // Write back displaced position via inverse (only for vegetation)
        VERTEX = (inverse(MODEL_MATRIX) * vec4(ws, 1.0)).xyz;
    }
    // For non-vegetation, VERTEX is unchanged (no inverse needed)

    world_pos = ws;
    world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
    vtx_color = COLOR;
    vtx_uv = UV;
}

// ============================================================
// Fragment shader
// ============================================================
void fragment() {
    // -- Triplanar blend weights from world normal --
    vec3 blend = abs(world_normal);
    blend = pow(blend, vec3(4.0)); // sharpen blend for voxels
    blend = max(blend, vec3(0.00001));
    blend /= (blend.x + blend.y + blend.z);

    // -- World-space UVs (sign-corrected to avoid mirroring on negative-facing surfaces) --
    vec3 scaled_pos = world_pos * triplanar_scale;
    vec2 uv_x = scaled_pos.yz * vec2(sign(world_normal.x), 1.0);
    vec2 uv_y = scaled_pos.xz * vec2(1.0, sign(world_normal.y));
    vec2 uv_z = scaled_pos.xy * vec2(-sign(world_normal.z), 1.0);

    // -- Base color from vertex color (greedy mesher provides material color) --
    vec3 base_color = vtx_color.rgb;
    float vtx_ao = vtx_color.a; // AO stored in vertex alpha

    // -- Material texture sampling (when atlas is available) --
    if (use_material_atlas) {
        // vtx_uv encodes the atlas tile origin (all 4 quad vertices share the same value).
        // We use world_pos to compute triplanar UVs that tile correctly per-voxel,
        // then map each into the tile's sub-rect within the atlas.
        vec2 tile_origin = vtx_uv;

        // fract(world_pos.AB * triplanar_scale) gives 0-1 tiling position per voxel.
        // Clamp slightly inward to prevent bleeding into adjacent atlas tiles at boundaries.
        // Map that into the atlas sub-rect: origin + fract_pos * tile_size.
        vec2 uv_yz = tile_origin + clamp(fract(scaled_pos.yz), 0.002, 0.998) * atlas_tile_size;
        vec2 uv_xz = tile_origin + clamp(fract(scaled_pos.xz), 0.002, 0.998) * atlas_tile_size;
        vec2 uv_xy = tile_origin + clamp(fract(scaled_pos.xy), 0.002, 0.998) * atlas_tile_size;

        // Triplanar blend: sample atlas at each projection plane and blend by normal weight.
        vec3 tex_color_tri = texture(material_atlas, uv_yz).rgb * blend.x
                           + texture(material_atlas, uv_xz).rgb * blend.y
                           + texture(material_atlas, uv_xy).rgb * blend.z;

        // Blend: texture provides detail, vertex color provides base tone.
        // overlay blend: multiply where dark, screen where light
        vec3 blended = mix(base_color, base_color * tex_color_tri * 2.0, texture_blend);
        base_color = clamp(blended, vec3(0.0), vec3(1.5)); // allow slight over-bright, clamped later
    }

    // -- Procedural noise to break up flat faces (per-plane) --
    float noise_x = surface_noise(scaled_pos * vec3(0.0, 3.7, 3.7));
    float noise_y = surface_noise(scaled_pos * vec3(3.7, 0.0, 3.7));
    float noise_z = surface_noise(scaled_pos * vec3(3.7, 3.7, 0.0));
    float blended_noise = noise_x * blend.x + noise_y * blend.y + noise_z * blend.z;
    // Remap noise from [0,1] to [-1,1] and apply subtly
    vec3 albedo = base_color * (1.0 + (blended_noise - 0.5) * 2.0 * noise_strength);

    // -- Edge / bevel highlight --
    // Use fract of world position to detect proximity to voxel edges
    vec3 voxel_frac = fract(world_pos);
    // Distance from nearest edge in each axis
    vec3 edge_dist = min(voxel_frac, 1.0 - voxel_frac);
    // How close to an edge (closer = brighter). Check the two smallest axes.
    // Sort: we want the two axes NOT aligned with the face normal
    vec3 weighted_edge = edge_dist + abs(world_normal) * 10.0; // push normal-axis dist high
    // Find the two smallest edge distances
    float e1 = min(weighted_edge.x, min(weighted_edge.y, weighted_edge.z));
    float e_temp_x = weighted_edge.x == e1 ? 999.0 : weighted_edge.x;
    float e_temp_y = weighted_edge.y == e1 ? 999.0 : weighted_edge.y;
    float e_temp_z = weighted_edge.z == e1 ? 999.0 : weighted_edge.z;
    float e2 = min(e_temp_x, min(e_temp_y, e_temp_z));

    // Edge glow: strongest at edges, strongest at corners (where both e1 and e2 are small)
    // Only visible during build mode (edge_grid_enabled = 1.0)
    float edge_factor = smoothstep(0.06, 0.0, e1) * 0.7 + smoothstep(0.06, 0.0, e2) * 0.3;
    // Corner boost
    float corner_factor = smoothstep(0.08, 0.0, e1) * smoothstep(0.08, 0.0, e2);
    float highlight = (edge_factor * 0.7 + corner_factor * 0.5) * edge_highlight_strength * edge_grid_enabled;
    albedo += vec3(highlight);

    // -- Ambient Occlusion --
    // Darken concave edges/corners. AO from vertex alpha (1.0 = no occlusion, 0.0 = full).
    // Also add edge-based AO for additional depth
    float edge_ao = 1.0 - corner_factor * 0.3;
    float ao = mix(1.0, vtx_ao * edge_ao, ao_strength);
    albedo *= ao;

    // -- Damage overlay --
    if (damage_blend > 0.0) {
        // Crack pattern using 2D Voronoi on each triplanar projection plane, blended by normal
        vec3 crack_pos = world_pos * 5.0;
        float cracks = crack_pattern_2d(crack_pos.yz) * blend.x
                      + crack_pattern_2d(crack_pos.xz) * blend.y
                      + crack_pattern_2d(crack_pos.xy) * blend.z;
        // Darken and add crack lines
        float crack_mask = cracks * damage_blend;
        // Darken overall with damage
        vec3 damaged_tint = mix(albedo, albedo * 0.55, damage_blend * 0.6);
        // Add dark crack lines
        albedo = mix(damaged_tint, vec3(0.08, 0.04, 0.02), crack_mask * 0.85);
        // Slight reddish-brown tint as damage increases
        albedo = mix(albedo, albedo * vec3(0.9, 0.7, 0.6), damage_blend * 0.3);
    }

    // -- Player tint --
    // Subtle color shift: lerp toward player color
    albedo = mix(albedo, albedo * player_tint.rgb, player_tint.a);

    // -- Roughness varies by material brightness --
    float luminance = dot(base_color, vec3(0.299, 0.587, 0.114));
    // Dark = rough (dirt, stone), bright = slightly smoother (metal, plastic)
    float roughness = mix(0.95, 0.55, smoothstep(0.2, 0.85, luminance));
    // Add noise variation to roughness
    roughness += (blended_noise - 0.5) * 0.08;

    // -- Metallic hint for very bright/saturated colors --
    float max_chan = max(base_color.r, max(base_color.g, base_color.b));
    float min_chan = min(base_color.r, min(base_color.g, base_color.b));
    float saturation = (max_chan > 0.001) ? (max_chan - min_chan) / max_chan : 0.0;
    float metallic = smoothstep(0.7, 0.95, luminance) * smoothstep(0.0, 0.15, 1.0 - saturation) * 0.4;

    // -- Output --
    ALBEDO = clamp(albedo, vec3(0.0), vec3(1.0));
    ROUGHNESS = clamp(roughness, 0.1, 1.0);
    METALLIC = metallic;
    AO = ao;
    AO_LIGHT_AFFECT = 0.7;

    // -- Kill cam: discard pixels in a clean checkerboard pattern so blocks become see-through.
    //    Uses discard (not ALPHA) so the shader stays in the opaque render pipeline.
    //    Checkerboard gives a uniform, non-noisy transparency vs random hash dithering.
    if (kill_cam_dissolve > 0.01) {
        if (world_pos.y > kill_cam_ground_y) {
            // Smooth height-based fade: blocks near ground dissolve less, higher blocks more
            float height_factor = clamp((world_pos.y - kill_cam_ground_y) / 8.0, 0.0, 1.0);
            float effective_dissolve = kill_cam_dissolve * height_factor;

            // 4x4 Bayer matrix for ordered dithering (clean uniform pattern)
            int px = int(FRAGCOORD.x) % 4;
            int py = int(FRAGCOORD.y) % 4;
            float bayer4x4[16] = float[16](
                 0.0/16.0,  8.0/16.0,  2.0/16.0, 10.0/16.0,
                12.0/16.0,  4.0/16.0, 14.0/16.0,  6.0/16.0,
                 3.0/16.0, 11.0/16.0,  1.0/16.0,  9.0/16.0,
                15.0/16.0,  7.0/16.0, 13.0/16.0,  5.0/16.0
            );
            float threshold = bayer4x4[py * 4 + px];
            if (threshold < effective_dissolve) {
                discard;
            }
        }
    }
}
