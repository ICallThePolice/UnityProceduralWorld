// --- ФИНАЛЬНАЯ ИСПРАВЛЕННАЯ ВЕРСИЯ: ShaderPasses.hlsl ---
#ifndef SHADER_PASSES_INCLUDED
#define SHADER_PASSES_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

struct Attributes
{
    float4 positionOS     : POSITION;
    float3 normalOS       : NORMAL; 
    float4 color          : COLOR;
    float2 uv0            : TEXCOORD0;
    float2 uv1            : TEXCOORD1;
    float  texBlend       : TEXCOORD2;
    float4 emissionData   : TEXCOORD3;
    float4 gapColor       : TEXCOORD4;
    float2 materialProps  : TEXCOORD5;
    float  gapWidth       : TEXCOORD6;
    float3 bevelData      : TEXCOORD7;
};

struct Varyings
{
    float4 positionHCS    : SV_POSITION;
    float3 positionWS     : TEXCOORD0;
    float3 normalWS       : TEXCOORD1;
    float4 shadowCoord    : TEXCOORD2;
    float4 color          : COLOR;
    float2 uv0            : TEXCOORD3;
    float2 uv1            : TEXCOORD4;
    float  texBlend       : TEXCOORD5;
    half4  emissionData   : TEXCOORD6;
    half4  gapColor       : TEXCOORD7;
    half2  materialProps  : TEXCOORD8;
    half   gapWidth       : TEXCOORD9;
    half3  bevelData      : TEXCOORD10;
};

CBUFFER_START(UnityPerMaterial)
    half _AmbientStrength;
CBUFFER_END

TEXTURE2D(_MainTex);
SAMPLER(sampler_MainTex);

// ===================================================================================
// Вспомогательная функция для отсечения пикселей в швах
// ===================================================================================
void ClipGaps(float3 positionWS, float3 normalWS, half gapWidth)
{
    // Если ширина шва нулевая, ничего не делаем для производительности
    if (gapWidth <= 0.0)
    {
        return;
    }

    float3 blend_weights = abs(normalWS);
    blend_weights /= (blend_weights.x + blend_weights.y + blend_weights.z);
    
    float2 grid_uv = 0;
    if(blend_weights.x > 0.5) grid_uv = positionWS.yz;
    else if(blend_weights.y > 0.5) grid_uv = positionWS.xz;
    else grid_uv = positionWS.xy;
    
    float2 grid_pos = frac(grid_uv);

    // --- ИСПРАВЛЕНА ЛОГИКА ---
    // Теперь мы проверяем, находится ли пиксель ВНУТРИ шва
    if (any(grid_pos < gapWidth) || any(grid_pos > (1.0 - gapWidth)))
    {
        // Если да, отбрасываем его
        clip(-1);
    }
}

// Vert-пасс для ForwardLit, Depth, Normals
Varyings vert(Attributes IN)
{
    Varyings OUT = (Varyings)0;
    OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
    OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
    OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
    OUT.shadowCoord = TransformWorldToShadowCoord(OUT.positionWS);

    OUT.color = IN.color;
    OUT.uv0 = IN.uv0;
    OUT.uv1 = IN.uv1;
    OUT.texBlend = IN.texBlend;
    OUT.emissionData = half4(IN.emissionData);
    OUT.gapColor = half4(IN.gapColor);
    OUT.materialProps = half2(IN.materialProps);
    OUT.gapWidth = IN.gapWidth;
    OUT.bevelData = half3(IN.bevelData);

    return OUT;
}

// Vert-пасс СПЕЦИАЛЬНО для ShadowCaster
Varyings vert_shadow(Attributes IN)
{
    Varyings OUT = (Varyings)0;
    OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
    
    float3 lightDirection = GetMainLight().direction;
    float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);
    OUT.positionHCS = TransformWorldToHClip(ApplyShadowBias(OUT.positionWS, normalWS, lightDirection));
    
    OUT.normalWS = normalWS;
    OUT.gapWidth = IN.gapWidth;
    
    return OUT;
}

// Фрагментный шейдер для основного пасса
half4 frag(Varyings IN) : SV_Target
{
    // --- ВОЗВРАЩАЕМ ПОЛНУЮ ЛОГИКУ ---
    
    // 1. Вычисляем процедурную нормаль для "граненого" вида
    float3 normalWS = normalize(cross(ddy(IN.positionWS), ddx(IN.positionWS)));

    // 2. Вырезаем швы
    ClipGaps(IN.positionWS, normalWS, IN.gapWidth);

    // 3. Смешиваем текстуры
    half4 primaryTexture = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv0);
    half4 secondaryTexture = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv1);
    half4 blendedTexture = lerp(primaryTexture, secondaryTexture, IN.texBlend);
    half4 surfaceAlbedo = blendedTexture * IN.color;
    
    // 4. Применяем Bevel (фаску/блик)
    float2 grid_uv;
    float3 blend_weights = abs(normalWS);
    blend_weights /= (blend_weights.x + blend_weights.y + blend_weights.z);
    if(blend_weights.x > 0.5) grid_uv = IN.positionWS.yz;
    else if(blend_weights.y > 0.5) grid_uv = IN.positionWS.xz;
    else grid_uv = IN.positionWS.xy;

    float2 localPos = frac(grid_uv);
    float2 dist_to_edge = min(localPos, 1.0 - localPos);
    float min_dist = min(dist_to_edge.x, dist_to_edge.y);
    float bevel_raw = 1.0 - smoothstep(0.0, IN.bevelData.x, min_dist);
    
    half3 bevelColor = (IN.bevelData.z < 0) ? half3(0,0,0) : half3(1,1,1);
    half3 finalAlbedo = lerp(surfaceAlbedo.rgb, bevelColor, bevel_raw * IN.bevelData.y * abs(IN.bevelData.z));
    
    // 5. Рассчитываем освещение
    Light mainLight = GetMainLight(IN.shadowCoord);
    half3 ambient = SampleSH(normalWS) * _AmbientStrength;
    half lambert = saturate(dot(normalWS, mainLight.direction));
    half3 diffuse = lambert * mainLight.color * mainLight.shadowAttenuation;
    half3 finalColor = (ambient + diffuse) * finalAlbedo;

    // 6. Добавляем свечение
    #if _EMISSION_ON
        finalColor += IN.emissionData.rgb * IN.emissionData.a;
    #endif
    
    return half4(finalColor, 1.0);
}

// Фрагментный шейдер для пасса глубины
float frag_depth(Varyings IN) : SV_Depth
{
    ClipGaps(IN.positionWS, IN.normalWS, IN.gapWidth);
    return IN.positionHCS.z / IN.positionHCS.w;
}

// Фрагментный шейдер для пасса глубины и нормалей
void frag_depthnormals(Varyings IN, out half4 outNormalWS : SV_Target0, out float outDepth : SV_Depth)
{
    ClipGaps(IN.positionWS, IN.normalWS, IN.gapWidth);
    outNormalWS = half4(normalize(IN.normalWS) * 0.5h + 0.5h, 1.0h);
    outDepth = IN.positionHCS.z / IN.positionHCS.w;
}

// Фрагментный шейдер для пасса теней
half4 frag_shadow(Varyings IN) : SV_Target
{
    ClipGaps(IN.positionWS, IN.normalWS, IN.gapWidth);
    return 0;
}

#endif