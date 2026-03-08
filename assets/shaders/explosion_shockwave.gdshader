shader_type canvas_item;

uniform vec2 center = vec2(0.5, 0.5);
uniform float radius = 0.0;
uniform float strength = 0.03;

void fragment()
{
    vec2 uv = UV;
    vec2 delta = uv - center;
    float distance = length(delta);
    float wave = smoothstep(radius, radius - 0.1, distance);
    uv += normalize(delta + vec2(0.0001)) * wave * strength;
    COLOR = texture(SCREEN_TEXTURE, uv);
}
