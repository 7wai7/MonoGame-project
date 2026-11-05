// Active experimental material with optional hex sampling for comparison.
// Authored control masks place ground materials and snow across the world.
// The separate river mask temporarily paints blue water on the terrain.
float4x4 World;
float4x4 View;
float4x4 Projection;
float GrassTileSize;
float GrassHexEnabled;
float OtherHexEnabled;
float HexGridWorldSize;
float RockTileSize;
float SnowTileSize;
float2 GrassHeightRange;
float2 RockHeightRange;
float2 SnowHeightRange;
float BoundaryHeightInfluence;
float BoundaryNoiseInfluence;
float BoundarySoftness;
float WorldColorVariationStrength;
float DetailBlendStrength;
float DetailFadeStart;
float DetailFadeEnd;
float3 CameraPosition;
float DebugMode;

texture ControlTexture;
texture RiverTexture;
texture GrassTexture;
texture RockTexture;
texture SnowTexture;
// RGB = tangent-space OpenGL normal, A = roughness (both linear data).
texture GrassNormalRoughTexture;
texture RockNormalRoughTexture;
texture SnowNormalRoughTexture;

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

sampler2D GrassSampler = sampler_state
{
    Texture = <GrassTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;
    AddressU = Wrap;
    AddressV = Wrap;
};

sampler2D RockSampler = sampler_state
{
    Texture = <RockTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;
    AddressU = Wrap;
    AddressV = Wrap;
};

sampler2D SnowSampler = sampler_state
{
    Texture = <SnowTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;
    AddressU = Wrap;
    AddressV = Wrap;
};

sampler2D GrassNormalRoughSampler = sampler_state
{
    Texture = <GrassNormalRoughTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;
    AddressU = Wrap;
    AddressV = Wrap;
};

sampler2D RockNormalRoughSampler = sampler_state
{
    Texture = <RockNormalRoughTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;
    AddressU = Wrap;
    AddressV = Wrap;
};

sampler2D SnowNormalRoughSampler = sampler_state
{
    Texture = <SnowNormalRoughTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;
    AddressU = Wrap;
    AddressV = Wrap;
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
    float2 MacroUV : TEXCOORD0;
    float3 WorldPosition : TEXCOORD1;
    float3 WorldNormal : TEXCOORD2;
};

PixelInput MainVS(VertexInput input)
{
    PixelInput output;
    float4 worldPosition = mul(input.Position, World);
    output.Position = mul(mul(worldPosition, View), Projection);
    output.WorldPosition = worldPosition.xyz;
    output.WorldNormal = normalize(mul(input.Normal, (float3x3)World));
    output.MacroUV = input.MacroUV;
    return output;
}

float RemapMaterialHeight(float height, float2 range)
{
    return saturate((height - range.x) / (range.y - range.x));
}

float HashPosition(float2 cell)
{
    return frac(sin(dot(cell, float2(127.1, 311.7))) * 43758.5453);
}

float SmoothWorldNoise(float2 position)
{
    float2 cell = floor(position);
    float2 local = frac(position);
    local = local * local * (3.0 - 2.0 * local);
    float bottom = lerp(HashPosition(cell),
        HashPosition(cell + float2(1.0, 0.0)), local.x);
    float top = lerp(HashPosition(cell + float2(0.0, 1.0)),
        HashPosition(cell + float2(1.0, 1.0)), local.x);
    return lerp(bottom, top, local.y) * 2.0 - 1.0;
}

#include "TerrainHexSampling.fxh"

float4 MainPS(PixelInput input) : COLOR0
{
    // The packed mask stores grass in red, rock in green, and snow in alpha.
    // Its blue channel is reserved; river coverage is a separate texture.
    float4 packedControl = tex2D(ControlSampler, input.MacroUV);
    float2 control = packedControl.rg;
    control /= max(control.r + control.g, 0.0001);
    float3 normal = normalize(input.WorldNormal);
    float rockPreference = control.g;
    float snowPreference = packedControl.a;
    float river = tex2D(RiverSampler, input.MacroUV).r;

    if (DebugMode > 2.5)
    {
        // Raw mask coverage: white = snow, blue = river, black = ground.
        return float4(lerp(float3(snowPreference, snowPreference, snowPreference),
            float3(0.15, 0.35, 1.0), river), 1.0);
    }

    // Distant ground uses average material colours; close ground uses the
    // repeating textures. Boundaries remain world-stable at every distance.
    float distanceToCamera = length(input.WorldPosition - CameraPosition);
    float detailFade = 1.0 - smoothstep(DetailFadeStart, DetailFadeEnd,
        distanceToCamera);
    float detailStrength = DetailBlendStrength * detailFade;
    // The painted mask places each material. Height and world-space noise
    // shape only the boundary, avoiding a broad translucent crossfade.
    float3 worldDx = ddx(input.WorldPosition);
    float3 worldDy = ddy(input.WorldPosition);
    float2 groundUV = input.WorldPosition.xz;
    float3 rockWeights = abs(normal);
    rockWeights *= rockWeights;
    rockWeights *= rockWeights;
    rockWeights /= rockWeights.x + rockWeights.y + rockWeights.z;

    bool rockBoundary = rockPreference > 0.15 && rockPreference < 0.85;
    bool snowBoundary = snowPreference > 0.15 && snowPreference < 0.85;
    bool anyBoundary = rockBoundary || snowBoundary;
    bool grassHeightNeeded = anyBoundary;
    bool grassHexActive = GrassHexEnabled > 0.5;
    bool otherHexActive = OtherHexEnabled > 0.5;
    bool grassHexNeeded = grassHeightNeeded ||
        (detailStrength > 0.001 && rockPreference < 0.85);
    float2 grassUV = groundUV / GrassTileSize;
    float2 grassDx = worldDx.xz / GrassTileSize;
    float2 grassDy = worldDy.xz / GrassTileSize;
    HexMaterialSample grassHex;
    if (grassHexActive && grassHexNeeded)
    {
        grassHex = SampleHexMaterial(GrassSampler,
            GrassNormalRoughSampler, grassUV, grassDx, grassDy,
            HexGridWorldSize / GrassTileSize, detailStrength > 0.001);
    }
    float2 snowUV = groundUV / SnowTileSize;
    float2 snowDx = worldDx.xz / SnowTileSize;
    float2 snowDy = worldDy.xz / SnowTileSize;
    HexMaterialSample snowHex;
    if (otherHexActive && (snowBoundary ||
        (detailStrength > 0.001 && snowPreference > 0.15)))
    {
        snowHex = SampleHexMaterial(SnowSampler,
            SnowNormalRoughSampler, snowUV, snowDx, snowDy,
            HexGridWorldSize / SnowTileSize, detailStrength > 0.001);
    }
    float4 grassTile = float4(0.0, 0.0, 0.0, 0.0);
    float grassHeight = 0.5;
    float breakup = 0.0;
    if (grassHeightNeeded)
    {
        float grassHeightSample;
        if (grassHexActive)
        {
            grassHeightSample = grassHex.albedoHeight.a;
        }
        else
        {
            grassTile = tex2Dgrad(GrassSampler, grassUV, grassDx, grassDy);
            grassHeightSample = grassTile.a;
        }
        grassHeight = RemapMaterialHeight(grassHeightSample, GrassHeightRange);
    }
    if (anyBoundary)
    {
        breakup = 0.7 * SmoothWorldNoise(groundUV / 14.0) +
            0.3 * SmoothWorldNoise(groundUV / 3.5);
    }

    // A loop keeps the three rock projections in one shader path. Each axis
    // uses the same albedo sample for both boundary height and surface colour.
    float rockHeight = 0.0;
    float3 rockColour = float3(0.0, 0.0, 0.0);
    float3 rockPerturbation = float3(0.0, 0.0, 0.0);
    float rockRoughness = 0.0;
    if (rockBoundary || (detailStrength > 0.001 && rockPreference > 0.15))
    {
        [loop] for (int axis = 0; axis < 3; axis++)
        {
            float axisWeight = axis == 0 ? rockWeights.x :
                (axis == 1 ? rockWeights.y : rockWeights.z);
            if (axisWeight <= 0.001)
            {
                continue;
            }
            float2 rockUV = (axis == 0 ? input.WorldPosition.zy :
                (axis == 1 ? groundUV : input.WorldPosition.xy)) /
                RockTileSize;
            float2 rockDx = (axis == 0 ? worldDx.zy :
                (axis == 1 ? worldDx.xz : worldDx.xy)) / RockTileSize;
            float2 rockDy = (axis == 0 ? worldDy.zy :
                (axis == 1 ? worldDy.xz : worldDy.xy)) / RockTileSize;
            float4 rockTile;
            float4 rockSurface = float4(0.5, 0.5, 1.0, 1.0);
            if (otherHexActive)
            {
                HexMaterialSample rockHex = SampleHexMaterial(RockSampler,
                    RockNormalRoughSampler, rockUV, rockDx, rockDy,
                    HexGridWorldSize / RockTileSize,
                    detailStrength > 0.001);
                rockTile = rockHex.albedoHeight;
                rockSurface = rockHex.surface;
            }
            else
            {
                rockTile = tex2Dgrad(RockSampler, rockUV, rockDx, rockDy);
                if (detailStrength > 0.001)
                {
                    rockSurface = tex2Dgrad(RockNormalRoughSampler,
                        rockUV, rockDx, rockDy);
                }
            }
            if (rockBoundary)
            {
                rockHeight += axisWeight * RemapMaterialHeight(rockTile.a,
                    RockHeightRange);
            }
            if (detailStrength > 0.001)
            {
                rockColour += axisWeight * rockTile.rgb;
                float3 localNormal = rockSurface.rgb * 2.0 - 1.0;
                float2 slope = localNormal.xy / max(localNormal.z, 0.2);
                rockPerturbation += axisWeight * (axis == 0 ?
                    float3(0.0, slope.y, slope.x) : (axis == 1 ?
                    float3(slope.x, 0.0, slope.y) :
                    float3(slope.x, slope.y, 0.0)));
                rockRoughness += axisWeight * rockSurface.a;
            }
        }
    }

    float rockFeather = max(BoundarySoftness, 0.5 * fwidth(rockPreference));
    float rockWeight = step(0.5, rockPreference);
    if (rockBoundary)
    {
        float edge = rockPreference - 0.5 +
            BoundaryHeightInfluence * (rockHeight - grassHeight) -
            BoundaryNoiseInfluence * breakup;
        rockWeight = smoothstep(-rockFeather, rockFeather, edge);
    }

    float snowWeight = step(0.5, snowPreference);
    if (snowBoundary)
    {
        float snowHeightSample = otherHexActive ? snowHex.albedoHeight.a :
            tex2Dgrad(SnowSampler, snowUV, snowDx, snowDy).a;
        float snowHeight = RemapMaterialHeight(snowHeightSample,
            SnowHeightRange);
        float edge = snowPreference - 0.5 +
            BoundaryHeightInfluence * (snowHeight - grassHeight) +
            BoundaryNoiseInfluence * breakup;
        float feather = max(BoundarySoftness, 0.5 * fwidth(snowPreference));
        snowWeight = smoothstep(-feather, feather, edge);
    }

    if (DebugMode > 0.5)
    {
        if (DebugMode < 1.5)
        {
            float3 materialColor =
                (1.0 - rockWeight) * float3(0.15, 0.85, 0.22) +
                rockWeight * float3(0.95, 0.12, 0.12);
            materialColor = lerp(materialColor, float3(1.0, 1.0, 1.0), snowWeight);
            materialColor = lerp(materialColor, float3(0.15, 0.35, 1.0), river);
            return float4(materialColor, 1.0);
        }

        // White = full rock, black = no rock, even under snow or rivers.
        return float4(rockWeight, rockWeight, rockWeight, 1.0);
    }

    // Approximate averages of the albedo tiles, used beyond detail range.
    float3 colour = lerp(float3(0.311, 0.241, 0.083),
        float3(0.472, 0.443, 0.393), rockWeight);
    float3 shadingNormal = normal;
    float roughness = 0.9;
    if (detailStrength > 0.001)
    {
        // Derivatives were computed before the boundary and material branches,
        // so each texture chooses the correct mip level across material edges.
        float3 detail = float3(0.0, 0.0, 0.0);
        float3 detailNormal = float3(0.0, 0.0, 0.0);
        float detailRoughness = 0.0;
        float groundWeight = 1.0 - rockWeight;
        if (groundWeight > 0.001)
        {
            // Project the map's red/green axes onto world X/Z while keeping
            // its blue axis aligned with the terrain's geometric normal.
            float3 tangentX = normalize(float3(1.0, 0.0, 0.0) -
                normal * normal.x);
            float3 tangentZ = normalize(cross(tangentX, normal));
            float3 grassColor;
            if (grassHexActive)
            {
                grassColor = grassHex.albedoHeight.rgb;
            }
            else if (grassHeightNeeded)
            {
                grassColor = grassTile.rgb;
            }
            else
            {
                grassColor = tex2Dgrad(GrassSampler,
                    grassUV, grassDx, grassDy).rgb;
            }
            float4 grassSurface = grassHexActive
                ? grassHex.surface
                : tex2Dgrad(GrassNormalRoughSampler,
                    grassUV, grassDx, grassDy);
            float3 grassLocalNormal = grassSurface.rgb * 2.0 - 1.0;
            detail += groundWeight * grassColor;
            detailNormal += groundWeight * normalize(
                tangentX * grassLocalNormal.x +
                tangentZ * grassLocalNormal.y +
                normal * grassLocalNormal.z);
            detailRoughness += groundWeight * grassSurface.a;
        }

        if (rockWeight > 0.001)
        {
            // World-XZ UVs stretch on cliffs, so the loop above projects rock
            // from all three axes before the final material weights are known.
            detail += rockWeight * rockColour;
            // Add only the projected tangent perturbations: a flat normal map
            // must preserve the original slope rather than axis-aligning it.
            detailNormal += rockWeight * normalize(normal + rockPerturbation);
            detailRoughness += rockWeight * rockRoughness;
        }

        colour = lerp(colour, detail, detailStrength);
        shadingNormal = normalize(lerp(normal, normalize(detailNormal),
            detailStrength));
        roughness = lerp(roughness, detailRoughness, detailStrength);
    }

    // Stable broad patches break up uniform single-material areas.
    float broadVariation = 0.7 * SmoothWorldNoise(groundUV / 110.0) +
        0.3 * SmoothWorldNoise((groundUV + float2(57.0, -93.0)) / 36.0);
    colour *= 1.0 + broadVariation * WorldColorVariationStrength;

    // Snow is its own tiled surface, with independent colour, normal,
    // roughness and displacement. Its height-shaped edge covers the ground.
    if (snowWeight > 0.001)
    {
        float3 snowColour = float3(0.783, 0.786, 0.787);
        if (detailStrength > 0.001)
        {
            float3 snowDetail = otherHexActive ? snowHex.albedoHeight.rgb :
                tex2Dgrad(SnowSampler, snowUV, snowDx, snowDy).rgb;
            snowColour = lerp(snowColour, snowDetail, detailStrength);
            float4 snowSurface = otherHexActive
                ? snowHex.surface
                : tex2Dgrad(SnowNormalRoughSampler, snowUV, snowDx, snowDy);
            float3 tangentX = normalize(float3(1.0, 0.0, 0.0) -
                normal * normal.x);
            float3 tangentZ = normalize(cross(tangentX, normal));
            float3 localNormal = snowSurface.rgb * 2.0 - 1.0;
            float3 snowNormal = normalize(
                tangentX * localNormal.x + tangentZ * localNormal.y +
                normal * localNormal.z);
            shadingNormal = normalize(lerp(shadingNormal, snowNormal,
                snowWeight * detailStrength));
            roughness = lerp(roughness, snowSurface.a,
                snowWeight * detailStrength);
        }
        colour = lerp(colour, snowColour, snowWeight);
    }

    // Temporary painted rivers; a separate water surface can replace this.
    colour = lerp(colour, float3(0.08, 0.27, 0.45), river);

    float3 lightDirection = normalize(float3(0.35, 0.85, -0.4));
    float NdotL = saturate(dot(shadingNormal, lightDirection));
    float light = 0.48 + 0.52 * NdotL;

    // Roughness controls the width of a small neutral dielectric highlight.
    // The scene still uses one directional light, not a full PBR light rig.
    float3 viewDirection = normalize(CameraPosition - input.WorldPosition);
    float3 halfDirection = normalize(lightDirection + viewDirection);
    float gloss = 1.0 - saturate(roughness);
    float specularPower = lerp(8.0, 96.0, gloss);
    float specular = pow(saturate(dot(shadingNormal, halfDirection)),
        specularPower) * (0.025 + 0.14 * gloss) * NdotL;
    return float4(saturate(colour * light + specular), 1.0);
}

technique TerrainMaterial
{
    pass Main
    {
        VertexShader = compile vs_3_0 MainVS();
        PixelShader = compile ps_3_0 MainPS();
    }
}
