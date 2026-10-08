#version 450

layout(location = 0) out vec2 out_Uv;

const vec2 Positions[3] = vec2[3](
    vec2(-1.0, -1.0),
    vec2(3.0, -1.0),
    vec2(-1.0, 3.0));

void main()
{
    vec2 position = Positions[gl_VertexIndex];

    out_Uv = position * 0.5 + 0.5;

    gl_Position = vec4(position, 0.0, 1.0);
}
