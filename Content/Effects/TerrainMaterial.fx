// Authored control masks place ground materials and snow across the world.
// The separate river mask temporarily paints blue water on the terrain.
float4x4 World;
float4x4 View;
float4x4 Projection;
float GrassTileSize;
float DirtTileSize;
float RockTileSize;
float SnowTileSize;
float2 GrassHeightRange;
float2 DirtHeightRange;
float2 RockHeightRange;
float2 SnowHeightRange;
float BoundaryHeightInfluence;
float BoundaryNoiseInfluence;
float BoundarySoftness;
float DirtColorSoftness;
float WorldColorVariationStrength;
float DetailBlendStrength;
float DetailFadeStart;
float DetailFadeEnd;
float3 CameraPosition;
float DebugMode;

texture ControlTexture;
texture RiverTexture;
texture GrassTexture;
texture DirtTexture;
texture RockTexture;
texture SnowTexture;
// RGB = tangent-space OpenGL normal, A = roughness (both linear data).
texture GrassNormalRoughTexture;
texture DirtNormalRoughTexture;
texture RockNormalRoughTexture;
texture SnowNormalRoughTexture;
texture GrassHeightTexture;
texture DirtHeightTexture;
texture RockHeightTexture;
texture SnowHeightTexture;

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

sampler2D DirtSampler = sampler_state
{
    Texture = <DirtTexture>;
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

sampler2D DirtNormalRoughSampler = sampler_state
{
    Texture = <DirtNormalRoughTexture>;
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

sampler2D GrassHeightSampler = sampler_state
{
    Texture = <GrassHeightTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;
    AddressU = Wrap;
    AddressV = Wrap;
};

sampler2D DirtHeightSampler = sampler_state
{
    Texture = <DirtHeightTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;
    AddressU = Wrap;
    AddressV = Wrap;
};

sampler2D RockHeightSampler = sampler_state
{
    Texture = <RockHeightTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;
    AddressU = Wrap;
    AddressV = Wrap;
};

sampler2D SnowHeightSampler = sampler_state
{
    Texture = <SnowHeightTexture>;
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

float4 MainPS(PixelInput input) : COLOR0
{
    // RGB ground weights and the independent alpha snow layer are editable
    // grayscale masks packed only for the GPU.
    float4 packedControl = tex2D(ControlSampler, input.MacroUV);
    float3 control = packedControl.rgb;
    control /= max(control.r + control.g + control.b, 0.0001);
    float3 normal = normalize(input.WorldNormal);
    float rockPreference = control.b;
    float dirtPreference = control.g / max(control.r + control.g, 0.0001);
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

    bool dirtBoundary = dirtPreference > 0.15 && dirtPreference < 0.85;
    bool rockBoundary = rockPreference > 0.15 && rockPreference < 0.85;
    bool snowBoundary = snowPreference > 0.15 && snowPreference < 0.85;
    float grassHeight = 0.5;
    float dirtHeight = 0.5;
    float breakup = 0.0;
    if (dirtBoundary || rockBoundary || snowBoundary)
    {
        grassHeight = RemapMaterialHeight(tex2Dgrad(GrassHeightSampler,
            groundUV / GrassTileSize,
            worldDx.xz / GrassTileSize,
            worldDy.xz / GrassTileSize).r, GrassHeightRange);
        dirtHeight = RemapMaterialHeight(tex2Dgrad(DirtHeightSampler,
            groundUV / DirtTileSize,
            worldDx.xz / DirtTileSize,
            worldDy.xz / DirtTileSize).r, DirtHeightRange);
        breakup = 0.7 * SmoothWorldNoise(groundUV / 14.0) +
            0.3 * SmoothWorldNoise(groundUV / 3.5);
    }

    float dirtFeather = max(BoundarySoftness, 0.5 * fwidth(dirtPreference));
    float rockFeather = max(BoundarySoftness, 0.5 * fwidth(rockPreference));
    float dirtWeight = step(0.5, dirtPreference);
    float dirtColorWeight = dirtWeight;
    if (dirtBoundary)
    {
        float edge = dirtPreference - 0.5 +
            BoundaryHeightInfluence * (dirtHeight - grassHeight) +
            BoundaryNoiseInfluence * breakup;
        dirtWeight = smoothstep(-dirtFeather, dirtFeather, edge);
        // Colour can settle more gradually while the surface material keeps
        // its narrow, height-shaped edge.
        dirtColorWeight = smoothstep(-DirtColorSoftness,
            DirtColorSoftness, edge);
    }

    float rockWeight = step(0.5, rockPreference);
    if (rockBoundary)
    {
        float rockHeight = 0.0;
        if (rockWeights.x > 0.001)
        {
            rockHeight += rockWeights.x * RemapMaterialHeight(
                tex2Dgrad(RockHeightSampler,
                    input.WorldPosition.zy / RockTileSize,
                    worldDx.zy / RockTileSize,
                    worldDy.zy / RockTileSize).r, RockHeightRange);
        }
        if (rockWeights.y > 0.001)
        {
            rockHeight += rockWeights.y * RemapMaterialHeight(
                tex2Dgrad(RockHeightSampler,
                    groundUV / RockTileSize,
                    worldDx.xz / RockTileSize,
                    worldDy.xz / RockTileSize).r, RockHeightRange);
        }
        if (rockWeights.z > 0.001)
        {
            rockHeight += rockWeights.z * RemapMaterialHeight(
                tex2Dgrad(RockHeightSampler,
                    input.WorldPosition.xy / RockTileSize,
                    worldDx.xy / RockTileSize,
                    worldDy.xy / RockTileSize).r, RockHeightRange);
        }
        float groundHeight = lerp(grassHeight, dirtHeight, dirtWeight);
        float edge = rockPreference - 0.5 +
            BoundaryHeightInfluence * (rockHeight - groundHeight) -
            BoundaryNoiseInfluence * breakup;
        rockWeight = smoothstep(-rockFeather, rockFeather, edge);
    }

    float snowWeight = step(0.5, snowPreference);
    if (snowBoundary)
    {
        float snowHeight = RemapMaterialHeight(tex2Dgrad(SnowHeightSampler,
            groundUV / SnowTileSize,
            worldDx.xz / SnowTileSize,
            worldDy.xz / SnowTileSize).r, SnowHeightRange);
        float groundHeight = lerp(grassHeight, dirtHeight, dirtWeight);
        float edge = snowPreference - 0.5 +
            BoundaryHeightInfluence * (snowHeight - groundHeight) +
            BoundaryNoiseInfluence * breakup;
        float feather = max(BoundarySoftness, 0.5 * fwidth(snowPreference));
        snowWeight = smoothstep(-feather, feather, edge);
    }

    if (DebugMode > 0.5)
    {
        if (DebugMode < 1.5)
        {
            float grassWeight = (1.0 - rockWeight) * (1.0 - dirtWeight);
            float soilWeight = (1.0 - rockWeight) * dirtWeight;
            float3 materialColor =
                grassWeight * float3(0.15, 0.85, 0.22) +
                soilWeight * float3(0.95, 0.55, 0.12) +
                rockWeight * float3(0.95, 0.12, 0.12);
            materialColor = lerp(materialColor, float3(1.0, 1.0, 1.0), snowWeight);
            materialColor = lerp(materialColor, float3(0.15, 0.35, 1.0), river);
            return float4(materialColor, 1.0);
        }

        // White = full rock, black = no rock, even under snow or rivers.
        return float4(rockWeight, rockWeight, rockWeight, 1.0);
    }

    // Approximate averages of the albedo tiles, used beyond detail range.
    float3 groundAverage = lerp(
        float3(0.311, 0.241, 0.083),
        float3(0.475, 0.394, 0.237), dirtColorWeight);
    float3 colour = lerp(groundAverage,
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
            float3 ground = float3(0.0, 0.0, 0.0);
            float3 groundNormal = float3(0.0, 0.0, 0.0);
            float groundRoughness = 0.0;
            // Project the map's red/green axes onto world X/Z while keeping
            // its blue axis aligned with the terrain's geometric normal.
            float3 tangentX = normalize(float3(1.0, 0.0, 0.0) -
                normal * normal.x);
            float3 tangentZ = normalize(cross(tangentX, normal));
            if (dirtColorWeight < 0.999)
            {
                ground += (1.0 - dirtColorWeight) * tex2Dgrad(GrassSampler,
                    groundUV / GrassTileSize,
                    worldDx.xz / GrassTileSize,
                    worldDy.xz / GrassTileSize).rgb;
            }
            if (dirtColorWeight > 0.001)
            {
                ground += dirtColorWeight * tex2Dgrad(DirtSampler,
                    groundUV / DirtTileSize,
                    worldDx.xz / DirtTileSize,
                    worldDy.xz / DirtTileSize).rgb;
            }

            if (dirtWeight < 0.999)
            {
                float grassWeight = 1.0 - dirtWeight;
                float4 grassSurface = tex2Dgrad(GrassNormalRoughSampler,
                    groundUV / GrassTileSize,
                    worldDx.xz / GrassTileSize,
                    worldDy.xz / GrassTileSize);
                float3 grassLocalNormal = grassSurface.rgb * 2.0 - 1.0;
                groundNormal += grassWeight * normalize(
                    tangentX * grassLocalNormal.x +
                    tangentZ * grassLocalNormal.y +
                    normal * grassLocalNormal.z);
                groundRoughness += grassWeight * grassSurface.a;
            }

            if (dirtWeight > 0.001)
            {
                float4 dirtSurface = tex2Dgrad(DirtNormalRoughSampler,
                    groundUV / DirtTileSize,
                    worldDx.xz / DirtTileSize,
                    worldDy.xz / DirtTileSize);
                float3 dirtLocalNormal = dirtSurface.rgb * 2.0 - 1.0;
                groundNormal += dirtWeight * normalize(
                    tangentX * dirtLocalNormal.x +
                    tangentZ * dirtLocalNormal.y +
                    normal * dirtLocalNormal.z);
                groundRoughness += dirtWeight * dirtSurface.a;
            }

            detail += groundWeight * ground;
            detailNormal += groundWeight * normalize(groundNormal);
            detailRoughness += groundWeight * groundRoughness;
        }

        if (rockWeight > 0.001)
        {
            // World-XZ UVs stretch badly on cliffs, so project rock from all
            // three axes.
            float3 rock = float3(0.0, 0.0, 0.0);
            float3 rockPerturbation = float3(0.0, 0.0, 0.0);
            float rockRoughness = 0.0;
            if (rockWeights.x > 0.001)
            {
                rock += rockWeights.x * tex2Dgrad(RockSampler,
                    input.WorldPosition.zy / RockTileSize,
                    worldDx.zy / RockTileSize,
                    worldDy.zy / RockTileSize).rgb;
                float4 surfaceX = tex2Dgrad(RockNormalRoughSampler,
                    input.WorldPosition.zy / RockTileSize,
                    worldDx.zy / RockTileSize,
                    worldDy.zy / RockTileSize);
                float3 normalX = surfaceX.rgb * 2.0 - 1.0;
                rockPerturbation += rockWeights.x *
                    float3(0.0, normalX.y, normalX.x) /
                    max(normalX.z, 0.2);
                rockRoughness += rockWeights.x * surfaceX.a;
            }
            if (rockWeights.y > 0.001)
            {
                rock += rockWeights.y * tex2Dgrad(RockSampler,
                    groundUV / RockTileSize,
                    worldDx.xz / RockTileSize,
                    worldDy.xz / RockTileSize).rgb;
                float4 surfaceY = tex2Dgrad(RockNormalRoughSampler,
                    groundUV / RockTileSize,
                    worldDx.xz / RockTileSize,
                    worldDy.xz / RockTileSize);
                float3 normalY = surfaceY.rgb * 2.0 - 1.0;
                rockPerturbation += rockWeights.y *
                    float3(normalY.x, 0.0, normalY.y) /
                    max(normalY.z, 0.2);
                rockRoughness += rockWeights.y * surfaceY.a;
            }
            if (rockWeights.z > 0.001)
            {
                rock += rockWeights.z * tex2Dgrad(RockSampler,
                    input.WorldPosition.xy / RockTileSize,
                    worldDx.xy / RockTileSize,
                    worldDy.xy / RockTileSize).rgb;
                float4 surfaceZ = tex2Dgrad(RockNormalRoughSampler,
                    input.WorldPosition.xy / RockTileSize,
                    worldDx.xy / RockTileSize,
                    worldDy.xy / RockTileSize);
                float3 normalZ = surfaceZ.rgb * 2.0 - 1.0;
                rockPerturbation += rockWeights.z *
                    float3(normalZ.x, normalZ.y, 0.0) /
                    max(normalZ.z, 0.2);
                rockRoughness += rockWeights.z * surfaceZ.a;
            }
            detail += rockWeight * rock;
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
            snowColour = lerp(snowColour, tex2Dgrad(SnowSampler,
                groundUV / SnowTileSize,
                worldDx.xz / SnowTileSize,
                worldDy.xz / SnowTileSize).rgb, detailStrength);
            float4 snowSurface = tex2Dgrad(SnowNormalRoughSampler,
                groundUV / SnowTileSize,
                worldDx.xz / SnowTileSize,
                worldDy.xz / SnowTileSize);
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
