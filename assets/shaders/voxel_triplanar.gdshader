shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

uniform sampler2D atlas_texture : source_color;
uniform vec4 player_tint : source_color = vec4(1.0);
uniform float damage_blend = 0.0;

void fragment()
{
    vec3 blend = abs(NORMAL);
    blend = max(blend, vec3(0.0001));
    blend /= blend.x + blend.y + blend.z;

    vec2 uv_x = UV.yz;
    vec2 uv_y = UV.xz;
    vec2 uv_z = UV.xy;

    vec3 x_color = texture(atlas_texture, fract(uv_x)).rgb;
    vec3 y_color = texture(atlas_texture, fract(uv_y)).rgb;
    vec3 z_color = texture(atlas_texture, fract(uv_z)).rgb;
    vec3 albedo = (x_color * blend.x) + (y_color * blend.y) + (z_color * blend.z);
    albedo = mix(albedo, albedo * 0.6 + vec3(0.3, 0.05, 0.05), damage_blend);
    ALBEDO = albedo * player_tint.rgb;
    ROUGHNESS = 0.85;
}
