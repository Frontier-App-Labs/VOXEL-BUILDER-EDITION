shader_type spatial;
render_mode unshaded, cull_back, depth_draw_opaque;

// -- Uniforms --
uniform vec4 highlight_color : source_color = vec4(1.0, 0.85, 0.2, 0.85);
uniform float pulse_speed : hint_range(0.5, 6.0) = 2.5;
uniform float fresnel_power : hint_range(0.5, 6.0) = 2.0;
uniform float glow_width : hint_range(0.0, 1.0) = 0.6;
uniform float glow_intensity : hint_range(0.5, 4.0) = 2.0;
uniform float outline_width : hint_range(0.0, 0.1) = 0.03;

// -- Varying --
varying vec3 world_pos;
varying vec3 world_normal;

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);

    // Slight vertex expansion for outline visibility
    VERTEX += NORMAL * outline_width;
}

void fragment() {
    // -- Fresnel edge glow --
    // View direction in world space
    vec3 view_dir = normalize(CAMERA_POSITION_WORLD - world_pos);
    float fresnel = 1.0 - abs(dot(view_dir, normalize(world_normal)));
    fresnel = pow(fresnel, fresnel_power);

    // Expand fresnel to cover more of the surface based on glow_width
    fresnel = smoothstep(1.0 - glow_width, 1.0, fresnel + glow_width * 0.5);

    // -- Pulse animation --
    float pulse = sin(TIME * pulse_speed * PI * 2.0) * 0.5 + 0.5; // 0 to 1
    // Remap pulse so it never fully disappears
    pulse = 0.5 + pulse * 0.5; // 0.5 to 1.0

    // -- Voxel edge highlight (fract-based) --
    vec3 voxel_frac = fract(world_pos);
    vec3 edge_dist = min(voxel_frac, 1.0 - voxel_frac);
    float min_edge = min(edge_dist.x, min(edge_dist.y, edge_dist.z));
    float edge_line = smoothstep(0.04, 0.0, min_edge);

    // -- Combine fresnel glow and edge lines --
    float glow_factor = max(fresnel, edge_line * 0.6);
    glow_factor *= pulse;

    // -- Final color --
    vec3 color = highlight_color.rgb * glow_intensity;
    float alpha = glow_factor * highlight_color.a;

    // Ensure minimum visibility so the highlight is always somewhat visible
    alpha = max(alpha, 0.08 * pulse * highlight_color.a);

    ALBEDO = color;
    ALPHA = clamp(alpha, 0.0, 1.0);
}
