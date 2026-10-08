#version 450

const vec2 Positions[3] = vec2[3](
    vec2(-1.0, -1.0),
    vec2(3.0, -1.0),
    vec2(-1.0, 3.0));

void main()
{
    vec2 position = Positions[gl_VertexIndex];

    gl_Position = vec4(position, 0.0, 1.0);
}
