shader_type sky;

// -- Uniforms --
uniform float pixel_density : hint_range(16.0, 128.0) = 48.0;
uniform int color_bands : hint_range(3, 12) = 6;

// Sky gradient colors (zenith to horizon)
uniform vec3 zenith_color : source_color = vec3(0.10, 0.10, 0.24);    // deep indigo
uniform vec3 upper_sky_color : source_color = vec3(0.18, 0.30, 0.55); // dark blue
uniform vec3 mid_sky_color : source_color = vec3(0.36, 0.55, 0.75);   // medium blue
uniform vec3 low_sky_color : source_color = vec3(0.58, 0.72, 0.82);   // light blue
uniform vec3 horizon_color : source_color = vec3(0.91, 0.66, 0.28);   // warm amber
uniform vec3 below_horizon_color : source_color = vec3(0.83, 0.47, 0.23); // orange
uniform vec3 ground_color : source_color = vec3(0.30, 0.25, 0.20);    // dark brown

// Sun
uniform vec3 sun_tint : source_color = vec3(1.0, 0.96, 0.83);
uniform float sun_pixel_radius : hint_range(1.0, 6.0) = 3.0;
uniform float sun_glow_radius : hint_range(2.0, 12.0) = 7.0;
uniform vec3 sun_glow_color : source_color = vec3(1.0, 0.85, 0.55);

// Clouds
uniform float cloud_speed : hint_range(0.0, 0.05) = 0.006;
uniform float cloud_coverage : hint_range(0.0, 1.0) = 0.40;
uniform float cloud_altitude : hint_range(0.1, 0.7) = 0.30;
uniform vec3 cloud_bright_color : source_color = vec3(1.0, 0.97, 0.92);
uniform vec3 cloud_shadow_color : source_color = vec3(0.65, 0.62, 0.72);

// Stars
uniform float star_density : hint_range(0.0, 0.05) = 0.015;
uniform float star_brightness : hint_range(0.0, 2.0) = 0.8;

// ============================================================
// Hash functions (for clouds and stars)
// ============================================================
float hash21(vec2 p) {
    p = fract(p * vec2(443.897, 441.423));
    p += dot(p, p.yx + 19.19);
    return fract((p.x + p.y) * p.x);
}

float hash11(float p) {
    p = fract(p * 0.1031);
    p *= p + 33.33;
    p *= p + p;
    return fract(p);
}

// Block noise: returns a constant value per integer cell
float block_noise(vec2 p) {
    return hash21(floor(p));
}

// Two-layer block noise for cloud shapes
float cloud_noise(vec2 p) {
    float n1 = block_noise(p);
    float n2 = block_noise(p * 2.17 + vec2(37.0, 13.0));
    return n1 * 0.6 + n2 * 0.4;
}

// ============================================================
// Quantize direction to pixel grid
// ============================================================
vec2 pixelate_direction(vec3 dir, float density) {
    // Convert to equirectangular coordinates
    float azimuth = atan(dir.x, dir.z);                  // -PI to PI
    float altitude = asin(clamp(dir.y, -1.0, 1.0));      // -PI/2 to PI/2

    // Quantize to pixel grid
    float pixel_step = PI / density;
    azimuth  = floor(azimuth  / pixel_step) * pixel_step + pixel_step * 0.5;
    altitude = floor(altitude / pixel_step) * pixel_step + pixel_step * 0.5;

    return vec2(azimuth, altitude);
}

// ============================================================
// Sky function
// ============================================================
void sky() {
    // Pixelate the view direction
    vec2 pix = pixelate_direction(EYEDIR, pixel_density);
    float azimuth = pix.x;
    float altitude = pix.y;

    // Normalized altitude: 0 at horizon, 1 at zenith, negative below horizon
    float t = altitude / (PI * 0.5);

    // ---- Stepped sky gradient ----
    // Quantize t into discrete bands for that retro look
    float band_size = 1.0 / float(color_bands);
    float t_stepped = floor(t / band_size) * band_size;

    vec3 sky_col;
    if (t_stepped >= 0.80) {
        sky_col = zenith_color;
    } else if (t_stepped >= 0.60) {
        sky_col = mix(upper_sky_color, zenith_color, (t_stepped - 0.60) / 0.20);
    } else if (t_stepped >= 0.40) {
        sky_col = mix(mid_sky_color, upper_sky_color, (t_stepped - 0.40) / 0.20);
    } else if (t_stepped >= 0.20) {
        sky_col = mix(low_sky_color, mid_sky_color, (t_stepped - 0.20) / 0.20);
    } else if (t_stepped >= 0.0) {
        sky_col = mix(horizon_color, low_sky_color, t_stepped / 0.20);
    } else if (t_stepped >= -0.15) {
        sky_col = mix(below_horizon_color, horizon_color, (t_stepped + 0.15) / 0.15);
    } else {
        sky_col = mix(ground_color, below_horizon_color, clamp((t_stepped + 0.5) / 0.35, 0.0, 1.0));
    }

    // ---- Sun ----
    // Sun position from the directional light
    float sun_az = atan(LIGHT0_DIRECTION.x, LIGHT0_DIRECTION.z);
    float sun_alt = asin(clamp(LIGHT0_DIRECTION.y, -1.0, 1.0));

    // Distance in pixel units
    float pixel_step = PI / pixel_density;
    float dx = (azimuth - sun_az) / pixel_step;
    float dy = (altitude - sun_alt) / pixel_step;
    float pixel_dist = max(abs(dx), abs(dy)); // Chebyshev distance gives square shape

    // Hard square sun disc
    if (pixel_dist <= sun_pixel_radius) {
        // Inner core is brightest, outer ring slightly dimmer
        float core = pixel_dist <= sun_pixel_radius * 0.5 ? 1.0 : 0.85;
        sky_col = sun_tint * (2.5 * core);
    }
    // Blocky glow ring around sun
    else if (pixel_dist <= sun_glow_radius) {
        float glow_t = 1.0 - (pixel_dist - sun_pixel_radius) / (sun_glow_radius - sun_pixel_radius);
        // Step the glow into 3 bands
        glow_t = floor(glow_t * 3.0) / 3.0;
        sky_col = mix(sky_col, sun_glow_color, glow_t * 0.5);
    }

    // ---- Clouds ----
    if (altitude > 0.02) {
        // Project pixelated direction onto cloud plane
        float cloud_y = sin(altitude);
        vec2 cloud_uv = vec2(azimuth, 0.0) / max(cloud_y + cloud_altitude, 0.01);
        // Scale for cloud size
        cloud_uv *= 4.0;

        // Scroll clouds
        float scroll = TIME * cloud_speed;
        cloud_uv.x += scroll;

        // Quantize cloud UV to same pixel grid (clouds are blocky too)
        float cloud_pixel = 1.0;
        cloud_uv = floor(cloud_uv / cloud_pixel) * cloud_pixel;

        // Generate blocky cloud pattern
        float n = cloud_noise(cloud_uv);

        // Threshold for cloud presence
        float cloud_mask = step(1.0 - cloud_coverage, n);

        // Fade clouds near horizon to avoid stretching
        cloud_mask *= smoothstep(0.05, 0.20, altitude / (PI * 0.5));

        // Cloud lighting: sun-facing side is brighter
        float sun_face = dot(
            vec2(cos(azimuth), sin(azimuth)),
            vec2(cos(sun_az), sin(sun_az))
        );
        float lit = step(0.0, sun_face);
        vec3 cloud_col = mix(cloud_shadow_color, cloud_bright_color, lit * 0.6 + 0.4);

        sky_col = mix(sky_col, cloud_col, cloud_mask);
    }

    // ---- Stars (upper sky only) ----
    if (t_stepped > 0.45) {
        // Use the quantized pixel coordinates as a seed
        vec2 star_seed = vec2(azimuth * 100.0, altitude * 100.0);
        float star_val = hash21(floor(star_seed));
        if (star_val < star_density) {
            // Twinkle using time
            float twinkle = sin(TIME * 1.5 + star_val * 50.0) * 0.3 + 0.7;
            float star_intensity = star_brightness * twinkle;
            // Slightly warm or cool tinted stars
            vec3 star_col = mix(vec3(0.9, 0.9, 1.0), vec3(1.0, 0.95, 0.85), hash11(star_val * 7.0));
            sky_col += star_col * star_intensity;
        }
    }

    // ---- Horizon glow band ----
    // Warm glow right at the horizon, also stepped
    float horizon_glow = 1.0 - smoothstep(0.0, 0.10, abs(t));
    horizon_glow = floor(horizon_glow * 3.0) / 3.0;
    vec3 glow = mix(horizon_color, sun_glow_color, 0.3);
    sky_col = mix(sky_col, glow, horizon_glow * 0.35);

    COLOR = sky_col;
}
