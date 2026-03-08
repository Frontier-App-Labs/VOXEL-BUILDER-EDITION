shader_type spatial;
render_mode cull_front, unshaded;

uniform vec4 outline_color : source_color = vec4(1.0, 0.85, 0.2, 1.0);
uniform float outline_width = 0.04;

void vertex()
{
    VERTEX += NORMAL * outline_width;
}

void fragment()
{
    ALBEDO = outline_color.rgb;
    ALPHA = outline_color.a;
}
