#version 450

layout(set = 0, binding = 0) uniform sampler SourceSampler;

layout(set = 0, binding = 1) uniform texture2D SourceTexture;

layout(location = 0) in vec2 in_Uv;

layout(location = 0) out vec4 out_Color;

// Zero-mean ordered dither, at most one 8-bit code step, indexed by screen coordinates
// so the pattern is stable across frames.
const float Bayer4x4[16] = float[16](
    0.0, 8.0, 2.0, 10.0,
    12.0, 4.0, 14.0, 6.0,
    3.0, 11.0, 1.0, 9.0,
    15.0, 7.0, 13.0, 5.0);

float LinearToSrgb(float channel)
{
    if (channel <= 0.0031308)
    {
        return channel * 12.92;
    }

    return 1.055 * pow(channel, 1.0 / 2.4) - 0.055;
}

void main()
{
    vec3 linearColor = clamp(texture(sampler2D(SourceTexture, SourceSampler), in_Uv).rgb, 0.0, 1.0);

    vec3 encoded = vec3(
        LinearToSrgb(linearColor.r),
        LinearToSrgb(linearColor.g),
        LinearToSrgb(linearColor.b));

    int bayerIndex = (int(gl_FragCoord.x) & 3) + ((int(gl_FragCoord.y) & 3) << 2);
    float dither = (Bayer4x4[bayerIndex] - 7.5) / (16.0 * 255.0);

    out_Color = vec4(encoded + dither, 1.0);
}
