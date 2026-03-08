shader_type spatial;
render_mode diffuse_toon, specular_toon;

uniform vec4 tint : source_color = vec4(1.0);

void fragment()
{
    ALBEDO = tint.rgb;
    ROUGHNESS = 0.7;
}
