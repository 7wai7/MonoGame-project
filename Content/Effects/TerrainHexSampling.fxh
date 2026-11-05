// Three simplex vertices select neighboring hex regions. Each cell is
// sampled and accumulated before moving to the next, keeping ps_3_0 register
// use low. Albedo alpha holds height; both maps share UV rotation and offset.
// Adapted from Mikkelsen, Practical Real-Time Hex-Tiling (JCGT 2022):
// https://jcgt.org/published/0011/03/05/
struct HexMaterialSample
{
    float4 albedoHeight;
    float4 surface;
};

float2 RotateHexUV(float2 value, float2 rotation)
{
    return float2(rotation.x * value.x - rotation.y * value.y,
                  rotation.y * value.x + rotation.x * value.y);
}

float2 UnrotateHexNormal(float2 value, float2 rotation)
{
    return float2(rotation.x * value.x + rotation.y * value.y,
                 -rotation.y * value.x + rotation.x * value.y);
}

float2 HexCenter(float2 id, float gridScale)
{
    return float2(id.x + 0.5 * id.y, id.y / 1.15470054) *
        (gridScale / 3.46410162);
}

// Keep the user's inexpensive three-channel arithmetic hash.
float3 HexRandom(float2 id)
{
    float3 p = frac(float3(id.x, id.y, id.x + id.y) *
        float3(0.1031, 0.11369, 0.13787) + float3(0.13, 0.37, 0.71));
    p += dot(p, p.yzx + 33.33);
    return frac((p.xxy + p.yzz) * p.zyx);
}

HexMaterialSample SampleHexMaterial(sampler2D albedoSampler,
    sampler2D surfaceSampler, float2 uv, float2 dx, float2 dy,
    float gridScale, bool surfaceNeeded)
{
    float2 grid = uv * (3.46410162 / gridScale);
    float2 skew = float2(grid.x - 0.57735027 * grid.y,
                         1.15470054 * grid.y);
    float2 baseId = floor(skew);
    float2 local = frac(skew);
    float remaining = 1.0 - local.x - local.y;
    float side = step(0.0, -remaining);
    float direction = 2.0 * side - 1.0;
    float3 barycentric = float3(-remaining * direction,
        side - local.y * direction, side - local.x * direction);
    float2 id0 = baseId + float2(side, side);
    float2 id1 = baseId + float2(side, 1.0 - side);
    float2 id2 = baseId + float2(1.0 - side, side);

    float4 colourHeightSum = float4(0.0, 0.0, 0.0, 0.0);
    float2 slopeSum = float2(0.0, 0.0);
    float roughnessSum = 0.0;
    float weightSum = 0.0;
    [loop] for (int cell = 0; cell < 3; cell++)
    {
        float2 id = cell == 0 ? id0 : (cell == 1 ? id1 : id2);
        float bary = cell == 0 ? barycentric.x :
            (cell == 1 ? barycentric.y : barycentric.z);
        float3 random = HexRandom(id);
        float angle = 6.28318531 * random.x;
        float2 rotation = float2(cos(angle), sin(angle));
        float2 center = HexCenter(id, gridScale);
        float2 sampleUV = RotateHexUV(uv - center, rotation) +
            center + random.yz;
        float2 sampleDx = RotateHexUV(dx, rotation);
        float2 sampleDy = RotateHexUV(dy, rotation);
        float4 albedoHeight = tex2Dgrad(albedoSampler,
            sampleUV, sampleDx, sampleDy);
        // Seventh power favors one cell except near its edge. Multiplication
        // replaces pow, while luminance reduces contrast loss at seams.
        float b = max(bary, 0.0);
        float b2 = b * b;
        float b4 = b2 * b2;
        float b7 = b4 * b2 * b;
        float luminance = dot(albedoHeight.rgb,
            float3(0.299, 0.587, 0.114));
        float weight = b7 * lerp(1.0, luminance, 0.6);
        colourHeightSum += weight * albedoHeight;
        weightSum += weight;
        if (surfaceNeeded)
        {
            float4 surface = tex2Dgrad(surfaceSampler,
                sampleUV, sampleDx, sampleDy);
            float3 localNormal = surface.rgb * 2.0 - 1.0;
            float2 slope = UnrotateHexNormal(
                -localNormal.xy / max(localNormal.z, 0.05), rotation);
            slopeSum += weight * slope;
            roughnessSum += weight * surface.a;
        }
    }

    float inverseWeight = 1.0 / max(weightSum, 0.00001);
    HexMaterialSample result;
    result.albedoHeight = colourHeightSum * inverseWeight;
    float3 blendedNormal = normalize(float3(
        -slopeSum * inverseWeight, 1.0));
    result.surface = float4(blendedNormal * 0.5 + 0.5,
        roughnessSum * inverseWeight);
    return result;
}
