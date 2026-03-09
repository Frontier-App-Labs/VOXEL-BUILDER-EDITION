shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// -- Uniforms --
uniform float dissolve_progress : hint_range(0.0, 1.0) = 0.0;
uniform float edge_width : hint_range(0.01, 0.2) = 0.08;
uniform vec4 edge_color_hot : source_color = vec4(1.0, 0.95, 0.7, 1.0);   // white-hot center
uniform vec4 edge_color_warm : source_color = vec4(1.0, 0.45, 0.05, 1.0); // orange outer
uniform float emission_strength : hint_range(0.0, 12.0) = 6.0;

// -- Varying --
varying vec3 world_pos;
varying vec4 vtx_color;

// ============================================================
// Multi-octave noise (world-space, no texture needed)
// ============================================================
float hash31(vec3 p) {
    p = fract(p * vec3(0.1031, 0.1030, 0.0973));
    p += dot(p, p.yxz + 33.33);
    return fract((p.x + p.y) * p.z);
}

float value_noise(vec3 p) {
    vec3 i = floor(p);
    vec3 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);

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

float fbm_noise(vec3 p) {
    float v = 0.0;
    float amp = 0.5;
    float freq = 1.0;
    for (int i = 0; i < 4; i++) {
        v += value_noise(p * freq) * amp;
        freq *= 2.17;
        amp *= 0.48;
    }
    return v;
}

// ============================================================
// Vertex
// ============================================================
void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    vtx_color = COLOR;

    // Slight upward push for dissolving fragments (toy "crumbling" feel)
    if (dissolve_progress > 0.05) {
        float local_noise = hash31(world_pos * 3.0);
        // Only push vertices whose noise is near the dissolve front
        float proximity = smoothstep(0.0, 0.2, dissolve_progress - local_noise + 0.15);
        VERTEX.y += proximity * dissolve_progress * 0.15;
        // Slight random jitter
        VERTEX.x += (hash31(world_pos * 7.3) - 0.5) * proximity * 0.05;
        VERTEX.z += (hash31(world_pos * 11.1) - 0.5) * proximity * 0.05;
    }
}

// ============================================================
// Fragment
// ============================================================
void fragment() {
    // World-space noise for consistent dissolve pattern
    float noise = fbm_noise(world_pos * 4.0);

    // Dissolve threshold
    float dissolve_threshold = dissolve_progress;

    // Discard fully dissolved fragments
    if (noise < dissolve_threshold) {
        discard;
    }

    // -- Edge glow calculation --
    float dist_from_edge = noise - dissolve_threshold;
    float edge_outer = smoothstep(edge_width, 0.0, dist_from_edge);       // 1.0 at dissolve edge
    float edge_inner = smoothstep(edge_width * 0.4, 0.0, dist_from_edge); // tighter hot core

    // Color gradient: white-hot core -> orange -> base color
    vec3 glow_color = mix(edge_color_warm.rgb, edge_color_hot.rgb, edge_inner);
    float glow_intensity = edge_outer * emission_strength * (0.6 + 0.4 * dissolve_progress);

    // -- Base color from vertex color (same as voxel shader provides) --
    vec3 base_color = vtx_color.rgb;

    // Darken base near the dissolve front (charring effect)
    float char_factor = smoothstep(edge_width * 2.5, edge_width * 0.5, dist_from_edge);
    base_color = mix(base_color, base_color * vec3(0.2, 0.15, 0.1), char_factor * 0.8);

    // Blend glow over base
    vec3 final_color = mix(base_color, glow_color, edge_outer * 0.6);

    // -- Output --
    ALBEDO = final_color;
    EMISSION = glow_color * glow_intensity;
    ROUGHNESS = mix(0.8, 0.2, edge_outer); // glowing edges are smoother
    METALLIC = 0.0;

    // Slight alpha fade right at the dissolve boundary for softer edge
    ALPHA = 1.0;
    if (dist_from_edge < edge_width * 0.05) {
        ALPHA = smoothstep(0.0, edge_width * 0.05, dist_from_edge);
    }
}
