// A world-space hexagonal grid makes every terrain LOD share the same cells.
// R/G/B of the control map select grass/soil/rock; A covers snow. Rivers use
// their own mask. All surface colours are generated here, with no tile maps.
float4x4 World;
float4x4 View;
float4x4 Projection;
float2 TerrainWorldOrigin;
float2 TerrainWorldSize;
float HexRadius;
float HexOutlineWidth;
float VoronoiPatchSize;
float VoronoiShadeStrength;

// One fixed base colour per material. The masks only select a material;
// Voronoi is the sole source of colour variation within that material.
static const float3 GrassBaseColor = float3(0.23, 0.45, 0.21);
static const float3 SoilBaseColor = float3(0.48, 0.34, 0.21);
static const float3 RockBaseColor = float3(0.48, 0.51, 0.54);
static const float3 SnowBaseColor = float3(0.88, 0.91, 0.93);
static const float3 RiverBaseColor = float3(0.10, 0.32, 0.48);

texture ControlTexture;
texture RiverTexture;

sampler2D ControlSampler = sampler_state
{
    Texture = <ControlTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;
    AddressU = Clamp;
    AddressV = Clamp;
};

sampler2D RiverSampler = sampler_state
{
    Texture = <RiverTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;
    AddressU = Clamp;
    AddressV = Clamp;
};

struct VertexInput
{
    float4 Position : POSITION0;
    float3 Normal : NORMAL0;
    float2 MacroUV : TEXCOORD0;
};

struct PixelInput
{
    float4 Position : POSITION0;
    float3 WorldPosition : TEXCOORD0;
    float3 WorldNormal : TEXCOORD1;
};

PixelInput MainVS(VertexInput input)
{
    PixelInput output;
    float4 worldPosition = mul(input.Position, World);
    output.Position = mul(mul(worldPosition, View), Projection);
    output.WorldPosition = worldPosition.xyz;
    output.WorldNormal = normalize(mul(input.Normal, (float3x3)World));
    return output;
}

// Pointy-top axial coordinates. Cube rounding chooses one cell even near the
// sloping sides, including for negative world coordinates.
float2 FindHexCenter(float2 position, out float2 cellId)
{
    float2 scaled = position / HexRadius;
    float q = 0.57735026919 * scaled.x - 0.33333333333 * scaled.y;
    float r = 0.66666666667 * scaled.y;
    float3 cube = float3(q, -q - r, r);
    float3 rounded = floor(cube + 0.5);
    float3 error = abs(rounded - cube);
    if (error.x > error.y && error.x > error.z)
        rounded.x = -rounded.y - rounded.z;
    else if (error.y > error.z)
        rounded.y = -rounded.x - rounded.z;
    else
        rounded.z = -rounded.x - rounded.y;

    cellId = float2(rounded.x, rounded.z);
    return HexRadius * float2(1.73205080757 * (cellId.x + 0.5 * cellId.y),
        1.5 * cellId.y);
}

// Small, deterministic hashes avoid trigonometric work in the pixel shader.
float Hash12(float2 value)
{
    float3 p = frac(float3(value.x, value.y, value.x) * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return frac((p.x + p.y) * p.z);
}

float2 Hash22(float2 value)
{
    float3 p = frac(float3(value.x, value.y, value.x) *
        float3(0.1031, 0.1030, 0.0973));
    p += dot(p, p.yzx + 33.33);
    return frac((p.xx + p.yz) * p.zy);
}

// Jittered Voronoi sites create broad patches. Evaluate at the hex centre,
// so one hex always has one stable shade and a Voronoi edge cannot split it.
float VoronoiShade(float2 hexCenter)
{
    float2 position = hexCenter / VoronoiPatchSize;
    float2 baseCell = floor(position);
    float nearestDistance = 100.0;
    float2 nearestSite = baseCell;
    [unroll] for (int y = -1; y <= 1; y++)
    {
        [unroll] for (int x = -1; x <= 1; x++)
        {
            float2 site = baseCell + float2(x, y);
            float2 offset = site + Hash22(site) - position;
            float distanceSquared = dot(offset, offset);
            if (distanceSquared < nearestDistance)
            {
                nearestDistance = distanceSquared;
                nearestSite = site;
            }
        }
    }
    return Hash12(nearestSite + float2(31.0, -17.0));
}

float4 MainPS(PixelInput input) : COLOR0
{
    float2 worldXZ = input.WorldPosition.xz;
    float2 cellId;
    float2 center = FindHexCenter(worldXZ, cellId);
    float2 maskUV = (center - TerrainWorldOrigin) / TerrainWorldSize;
    // Centre UVs are constant within a cell. Supply world derivatives so the
    // mask still chooses a sensible mip level on distant terrain.
    float2 uvDx = ddx(worldXZ) / TerrainWorldSize;
    float2 uvDy = ddy(worldXZ) / TerrainWorldSize;
    float4 control = tex2Dgrad(ControlSampler, maskUV, uvDx, uvDy);
    float river = tex2Dgrad(RiverSampler, maskUV, uvDx, uvDy).r;
    float3 ground = control.rgb / max(dot(control.rgb, float3(1.0, 1.0, 1.0)), 0.0001);

    // Pick one unchanged base colour for the whole hexagon. Snow and river
    // masks cover the ground in that order, with the river on top.
    float3 color = GrassBaseColor;
    if (ground.g > ground.r && ground.g >= ground.b)
        color = SoilBaseColor;
    if (ground.b > ground.r && ground.b > ground.g)
        color = RockBaseColor;
    if (control.a >= 0.5)
        color = SnowBaseColor;
    if (river >= 0.5)
        color = RiverBaseColor;

    color *= 1.0 + (VoronoiShade(center) - 0.5) * VoronoiShadeStrength;

    // Distance to the six sides of a regular pointy-top hexagon. The outline
    // is antialiased in screen space and fades when a cell becomes subpixel.
    float2 local = abs(worldXZ - center);
    float edgeDistance = 0.86602540378 * HexRadius -
        max(local.x, 0.5 * local.x + 0.86602540378 * local.y);
    float antialiasWidth = max(fwidth(edgeDistance), 0.015);
    float outline = 1.0 - smoothstep(HexOutlineWidth - antialiasWidth,
        HexOutlineWidth + antialiasWidth, edgeDistance);
    float cellPixels = HexRadius /
        max(max(fwidth(worldXZ.x), fwidth(worldXZ.y)), 0.001);
    float outlineVisibility = saturate((cellPixels - 1.5) / 3.0);
    color *= 1.0 - 0.32 * outline * outlineVisibility;

    float3 normal = normalize(input.WorldNormal);
    float3 lightDirection = normalize(float3(0.35, 0.85, -0.4));
    float light = 0.55 + 0.45 * saturate(dot(normal, lightDirection));
    return float4(saturate(color * light), 1.0);
}

technique TerrainHoneycomb
{
    pass Main
    {
        VertexShader = compile vs_3_0 MainVS();
        PixelShader = compile ps_3_0 MainPS();
    }
}
