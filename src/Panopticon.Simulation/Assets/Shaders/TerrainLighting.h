#ifndef TERRAIN_LIGHTING_H
#define TERRAIN_LIGHTING_H

vec3 GetTerrainDiffuseLighting(vec3 position, vec3 normal, float viewDepth, ivec2 pixel)
{
    float nDotL = saturate(dot(normal, -_GlobalLightingConstantsPS.Terrain.Lights[0].Direction));
    vec3 visibility = ShadowVisibility(position, viewDepth, nDotL, normal, pixel);
    vec3 diffuseColor;
    vec3 specularColor;
    DoLighting(_GlobalLightingConstantsPS.Terrain, position, normal,
        vec3(1), vec3(1), vec3(0), 0, false, visibility, diffuseColor, specularColor);
    return diffuseColor;
}

#endif
