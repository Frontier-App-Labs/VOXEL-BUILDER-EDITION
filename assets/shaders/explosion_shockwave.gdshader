shader_type canvas_item;

// -- Uniforms --
uniform vec2 shockwave_center = vec2(0.5, 0.5);
uniform float shockwave_progress : hint_range(0.0, 1.0) = 0.0;
uniform float shockwave_intensity : hint_range(0.0, 0.15) = 0.06;
uniform float ring_width : hint_range(0.01, 0.2) = 0.08;
uniform float flash_intensity : hint_range(0.0, 1.0) = 0.4;
uniform float chromatic_strength : hint_range(0.0, 0.03) = 0.012;
uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;

void fragment() {
    vec2 uv = SCREEN_UV;

    // -- Aspect ratio correction for circular shockwave --
    vec2 screen_size = 1.0 / SCREEN_PIXEL_SIZE;
    float aspect = screen_size.x / screen_size.y;
    vec2 centered = uv - shockwave_center;
    centered.x *= aspect;

    float dist = length(centered);
    vec2 dir = normalize(centered + vec2(0.00001)); // avoid div by zero

    // -- Shockwave ring --
    // Ring expands from 0 to ~1.5 (covers full screen diagonal)
    float max_radius = 1.5;
    float current_radius = shockwave_progress * max_radius;

    // Distance from ring center
    float ring_dist = abs(dist - current_radius);

    // Ring falloff: sharp inner, soft outer
    float ring = smoothstep(ring_width, ring_width * 0.15, ring_dist);

    // Fade ring as it expands (weaker at edges of screen)
    float ring_fade = 1.0 - smoothstep(0.3, 1.0, shockwave_progress);
    ring *= ring_fade;

    // -- Radial distortion (push outward at the ring front) --
    float distortion_amount = ring * shockwave_intensity;
    // Behind the wavefront: slight inward pull. At wavefront: outward push.
    float sign_factor = (dist < current_radius) ? -0.3 : 1.0;
    vec2 distorted_uv = uv + dir * distortion_amount * sign_factor / vec2(aspect, 1.0);

    // -- Chromatic aberration at wavefront --
    float chroma = ring * chromatic_strength * ring_fade;
    vec2 chroma_offset = dir * chroma / vec2(aspect, 1.0);

    vec3 color;
    color.r = texture(screen_texture, distorted_uv + chroma_offset).r;
    color.g = texture(screen_texture, distorted_uv).g;
    color.b = texture(screen_texture, distorted_uv - chroma_offset).b;

    // -- Screen flash (bright white that fades quickly) --
    // Flash peaks at progress ~0.05 and fades out by ~0.3
    float flash = flash_intensity * smoothstep(0.0, 0.03, shockwave_progress)
                                  * smoothstep(0.35, 0.02, shockwave_progress);
    // Flash is stronger near the center
    float center_dist = length(uv - shockwave_center);
    flash *= smoothstep(0.8, 0.0, center_dist);
    color += vec3(flash);

    // -- Subtle ring brightness (the shockwave ring itself is slightly bright) --
    float ring_brightness = ring * 0.08 * ring_fade;
    color += vec3(ring_brightness);

    // -- Slight desaturation at shockwave front --
    float gray = dot(color, vec3(0.299, 0.587, 0.114));
    color = mix(color, vec3(gray), ring * 0.2 * ring_fade);

    COLOR = vec4(color, 1.0);
}
