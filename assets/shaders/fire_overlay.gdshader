shader_type spatial;
render_mode unshaded, blend_add, cull_disabled;

uniform float intensity : hint_range(0.0, 1.0) = 0.5;
uniform float time_offset = 0.0;

void fragment() {
    float flicker = sin(TIME * 8.0 + time_offset) * 0.3 + 0.7;
    vec3 fire_color = mix(vec3(1.0, 0.3, 0.0), vec3(1.0, 0.8, 0.2), flicker);
    ALBEDO = fire_color;
    ALPHA = intensity * flicker * 0.6;
}
