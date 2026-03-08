shader_type spatial;
render_mode unshaded;

uniform float dissolve_amount : hint_range(0.0, 1.0) = 0.0;

float hash(vec3 p)
{
    return fract(sin(dot(p, vec3(12.9898, 78.233, 37.719))) * 43758.5453);
}

void fragment()
{
    float noise = hash(WORLD_POSITION.xyz * 8.0);
    if (noise < dissolve_amount)
    {
        discard;
    }

    ALBEDO = vec3(1.0, 0.45, 0.1);
    EMISSION = vec3(0.8, 0.25, 0.0) * (1.0 - dissolve_amount);
}
