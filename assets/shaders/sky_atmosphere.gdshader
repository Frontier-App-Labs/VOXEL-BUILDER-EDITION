shader_type sky;

// -- Uniforms --
uniform vec3 sky_top_color : source_color = vec3(0.3, 0.55, 0.95);
uniform vec3 sky_horizon_color : source_color = vec3(0.85, 0.75, 0.6);
uniform vec3 sky_bottom_color : source_color = vec3(0.55, 0.65, 0.45);
uniform vec3 sun_color : source_color = vec3(1.0, 0.95, 0.8);
uniform float sun_size : hint_range(0.001, 0.1) = 0.02;
uniform float sun_glow_size : hint_range(0.01, 0.5) = 0.15;
uniform float sun_glow_intensity : hint_range(0.0, 3.0) = 1.2;
uniform float cloud_speed : hint_range(0.0, 0.1) = 0.008;
uniform float cloud_density : hint_range(0.0, 1.0) = 0.45;
uniform float cloud_softness : hint_range(0.0, 0.5) = 0.2;
uniform float cloud_height : hint_range(0.0, 1.0) = 0.3;
uniform vec3 cloud_color : source_color = vec3(1.0, 0.98, 0.95);
uniform vec3 cloud_shadow_color : source_color = vec3(0.7, 0.65, 0.75);
uniform float horizon_fog : hint_range(0.0, 1.0) = 0.3;
uniform float warmth : hint_range(0.0, 1.0) = 0.5;

// ============================================================
// Noise functions
// ============================================================
float hash21(vec2 p) {
    p = fract(p * vec2(0.1031, 0.1030));
    p += dot(p, p.yx + 33.33);
    return fract((p.x + p.y) * p.x);
}

float value_noise_2d(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);

    float a = hash21(i);
    float b = hash21(i + vec2(1.0, 0.0));
    float c = hash21(i + vec2(0.0, 1.0));
    float d = hash21(i + vec2(1.0, 1.0));

    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

float fbm_2d(vec2 p) {
    float v = 0.0;
    float amp = 0.5;
    float freq = 1.0;
    for (int i = 0; i < 5; i++) {
        v += value_noise_2d(p * freq) * amp;
        freq *= 2.03;
        amp *= 0.49;
        p += vec2(1.7, 3.1); // domain shift per octave
    }
    return v;
}

// ============================================================
// Sky function
// ============================================================
void sky() {
    vec3 dir = EYEDIR;

    // -- Sky gradient --
    float y = dir.y;

    // Upper sky gradient (horizon to zenith)
    float upper_blend = smoothstep(0.0, 0.7, y);
    vec3 sky = mix(sky_horizon_color, sky_top_color, upper_blend);

    // Lower sky (below horizon)
    float lower_blend = smoothstep(0.0, -0.3, y);
    sky = mix(sky, sky_bottom_color, lower_blend);

    // Warm gradient near horizon
    float horizon_band = 1.0 - smoothstep(0.0, 0.15, abs(y));
    vec3 warm_tint = vec3(1.0, 0.85, 0.65);
    sky = mix(sky, sky * warm_tint, horizon_band * warmth);

    // -- Sun disk and glow --
    // Use LIGHT0_DIRECTION for sun position
    float sun_dot = dot(dir, LIGHT0_DIRECTION);

    // Sharp sun disk
    float sun_disk = smoothstep(1.0 - sun_size * 0.01, 1.0, sun_dot);
    // Soft sun glow
    float sun_glow = pow(max(sun_dot, 0.0), 8.0 / max(sun_glow_size, 0.01));

    sky += sun_color * sun_disk * 3.0;
    sky += sun_color * sun_glow * sun_glow_intensity * 0.3;

    // Sun-horizon warmth: when sun is near horizon, warm the sky
    float sun_height = LIGHT0_DIRECTION.y;
    float sunset_factor = smoothstep(0.3, -0.05, sun_height);
    vec3 sunset_color = vec3(1.0, 0.6, 0.3);
    sky = mix(sky, sky * sunset_color, sunset_factor * horizon_band * 0.5);

    // -- Procedural clouds --
    if (y > 0.0) {
        // Project direction onto a cloud plane
        vec2 cloud_uv = dir.xz / max(dir.y + cloud_height, 0.001);
        cloud_uv *= 2.0;

        // Scrolling
        float t = TIME * cloud_speed;
        cloud_uv += vec2(t, t * 0.3);

        // Cloud noise
        float cloud_noise = fbm_2d(cloud_uv * 3.0);
        float cloud_detail = fbm_2d(cloud_uv * 8.0 + vec2(50.0, 30.0));
        float cloud_shape = cloud_noise * 0.7 + cloud_detail * 0.3;

        // Threshold to density
        float cloud_mask = smoothstep(1.0 - cloud_density, 1.0 - cloud_density + cloud_softness, cloud_shape);

        // Fade clouds near horizon (avoid ugly stretching)
        cloud_mask *= smoothstep(0.02, 0.15, y);

        // Cloud lighting: brighter on sun-facing side
        float cloud_lit = smoothstep(-0.2, 0.6, sun_dot) * 0.4 + 0.6;
        vec3 lit_cloud = mix(cloud_shadow_color, cloud_color, cloud_lit);

        // Sun glow through clouds
        lit_cloud += sun_color * pow(max(sun_dot, 0.0), 4.0) * 0.15;

        sky = mix(sky, lit_cloud, cloud_mask);
    }

    // -- Horizon fog --
    float fog = smoothstep(0.1, 0.0, abs(y));
    vec3 fog_color = mix(sky_horizon_color, sun_color, smoothstep(-0.5, 0.5, sun_dot) * 0.3);
    sky = mix(sky, fog_color, fog * horizon_fog);

    COLOR = sky;
}
