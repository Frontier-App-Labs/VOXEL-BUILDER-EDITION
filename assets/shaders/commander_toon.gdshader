shader_type spatial;
render_mode diffuse_toon, specular_toon, cull_back;

// -- Uniforms --
uniform vec4 team_color : source_color = vec4(0.2, 0.5, 1.0, 1.0);
uniform float panic_level : hint_range(0.0, 1.0) = 0.0;
uniform float rim_intensity : hint_range(0.0, 2.0) = 0.8;
uniform float rim_power : hint_range(0.5, 8.0) = 3.0;
uniform vec4 rim_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float shadow_threshold : hint_range(0.0, 1.0) = 0.35;
uniform float highlight_threshold : hint_range(0.0, 1.0) = 0.75;
uniform vec4 shadow_color : source_color = vec4(0.15, 0.1, 0.2, 1.0);
uniform vec4 highlight_color : source_color = vec4(1.0, 0.97, 0.9, 1.0);
uniform float specular_size : hint_range(0.0, 1.0) = 0.12;
uniform float wobble_amplitude : hint_range(0.0, 0.15) = 0.06;
uniform float wobble_speed : hint_range(1.0, 20.0) = 8.0;

// -- Varying --
varying vec3 world_pos;
varying vec3 world_normal;
varying vec4 vtx_color;

// Simple hash for vertex wobble
float hash11(float p) {
    p = fract(p * 0.1031);
    p *= p + 33.33;
    p *= p + p;
    return fract(p);
}

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
    vtx_color = COLOR;

    // -- Panic wobble --
    if (panic_level > 0.01) {
        float wobble_amount = panic_level * wobble_amplitude;
        float t = TIME * wobble_speed;

        // Per-vertex variation using position hash
        float vert_hash = hash11(VERTEX.x * 13.7 + VERTEX.y * 7.3 + VERTEX.z * 23.1);

        // Shaking wobble on X and Z, slight Y bounce
        float wx = sin(t + vert_hash * 6.28) * wobble_amount;
        float wz = cos(t * 1.3 + vert_hash * 4.17) * wobble_amount;
        float wy = abs(sin(t * 2.0 + vert_hash * 3.14)) * wobble_amount * 0.3;

        // Scale wobble by height (feet stay grounded, head wobbles most)
        float height_factor = smoothstep(-0.5, 1.5, VERTEX.y);

        VERTEX.x += wx * height_factor;
        VERTEX.y += wy * height_factor;
        VERTEX.z += wz * height_factor;
    }
}

void fragment() {
    // -- Base color from vertex color with team tint --
    vec3 base_color = vtx_color.rgb;

    // Apply team color as a tint: blend toward team color for saturated areas
    float lum = dot(base_color, vec3(0.299, 0.587, 0.114));
    // Team color replaces hue in mid-brightness areas (uniforms, not skin)
    float team_blend = smoothstep(0.15, 0.4, lum) * smoothstep(0.85, 0.6, lum);
    // Only tint areas that are already somewhat saturated (uniforms, not skin)
    float max_c = max(base_color.r, max(base_color.g, base_color.b));
    float min_c = min(base_color.r, min(base_color.g, base_color.b));
    float sat = (max_c > 0.001) ? (max_c - min_c) / max_c : 0.0;
    team_blend *= smoothstep(0.1, 0.3, sat);
    base_color = mix(base_color, base_color * team_color.rgb * 1.8, team_blend * 0.6);

    // -- Panic red tint --
    if (panic_level > 0.0) {
        // Increasingly red flush
        vec3 panic_tint = mix(vec3(1.0), vec3(1.0, 0.3, 0.3), panic_level);
        base_color *= panic_tint;
        // Slight brightness pulse when panicking
        float panic_pulse = sin(TIME * 6.0) * 0.5 + 0.5;
        base_color += vec3(0.15, 0.0, 0.0) * panic_level * panic_pulse;
    }

    // -- 3-band cel shading via light --
    // We use DIFFUSE_LIGHT in the light() function, but for the albedo pass
    // we set up the base color. The cel shading bands are applied in light().
    ALBEDO = base_color;
    ROUGHNESS = 0.85;
    METALLIC = 0.0;

    // -- Rim light (fresnel-based) --
    vec3 view_dir = normalize(CAMERA_POSITION_WORLD - world_pos);
    float fresnel = 1.0 - max(dot(view_dir, normalize(world_normal)), 0.0);
    fresnel = pow(fresnel, rim_power);

    // Rim is brighter on lit side (top hemisphere)
    float top_bias = smoothstep(-0.2, 0.5, world_normal.y) * 0.5 + 0.5;
    vec3 rim = rim_color.rgb * fresnel * rim_intensity * top_bias;

    EMISSION = rim;

    // -- Specular setup for toon specular --
    SPECULAR = specular_size;
}

void light() {
    // -- 3-band cel shading --
    float NdotL = dot(NORMAL, LIGHT);

    // Hard-step shadow bands
    float shadow_band = step(shadow_threshold, NdotL);
    float highlight_band = step(highlight_threshold, NdotL);

    // Three bands: shadow, midtone, highlight
    vec3 shaded_color;
    if (NdotL < shadow_threshold) {
        // Shadow band
        shaded_color = ALBEDO * shadow_color.rgb;
    } else if (NdotL < highlight_threshold) {
        // Midtone band
        shaded_color = ALBEDO;
    } else {
        // Highlight band
        shaded_color = ALBEDO * highlight_color.rgb;
    }

    // -- Toon specular (sharp dot) --
    vec3 half_dir = normalize(LIGHT + VIEW);
    float NdotH = max(dot(NORMAL, half_dir), 0.0);
    float spec = step(1.0 - specular_size * 0.15, NdotH);
    vec3 spec_color = highlight_color.rgb * spec * 0.5;

    // -- Combine --
    DIFFUSE_LIGHT += (shaded_color + spec_color) * ATTENUATION * LIGHT_COLOR;
}
