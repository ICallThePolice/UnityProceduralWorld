// GoldenVoxelShader.shader (ИСПРАВЛЕННАЯ ВЕРСИЯ)
Shader "Voxel/PBR_Debug"
{
    // Свойства остаются без изменений
    Properties
    {
        [MainTexture] _MainTex ("Texture Atlas", 2D) = "white" {}
        [Header(Global Settings)]
        _AmbientStrength("Ambient Strength", Range(0.0, 1.0)) = 0.2
        _VoxelScale ("Voxel Scale", Float) = 1.0
        _GapWidth ("Gap Width", Range(0.0, 0.1)) = 0.0
        _FadeStartDistance ("Fade Start Distance", Float) = 16.0
        _FadeEndDistance ("Fade End Distance", Float) = 32.0
        [Header(Bevel is now a global effect)]
        _BevelWidth ("Bevel Width", Range(0.01, 0.49)) = 0.1
        _BevelIntensity ("Bevel Darkening", Range(0.0, 1.0)) = 0.8

        [Header(Technical)]
        [Toggle(_EMISSION_ON)] _Emission("Enable Emission?", Float) = 0
        _CustomShadowBias("Shadow Bias", Float) = 0.01 // Добавил свойство для удобной настройки
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        // =========================================================================
        // Пасс 1: UniversalForward - для цвета и освещения
        // =========================================================================
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _EMISSION_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // ИСПРАВЛЕНИЕ: Структура атрибутов теперь точно соответствует VertexAttributeDescriptor в C#
            struct Attributes
            {
                float4 positionOS    : POSITION;
                float3 normalOS      : NORMAL;
                float4 tangentOS     : TANGENT;
                float4 color         : COLOR;      // VertexAttribute.Color
                float2 uv0           : TEXCOORD0;  // VertexAttribute.TexCoord0
                float2 uv1           : TEXCOORD1;  // VertexAttribute.TexCoord1
                float  texBlend      : TEXCOORD2;  // VertexAttribute.TexCoord2
                float4 emissionData  : TEXCOORD3;  // VertexAttribute.TexCoord3
                float4 gapColor      : TEXCOORD4;  // VertexAttribute.TexCoord4
                float2 materialProps : TEXCOORD5;  // VertexAttribute.TexCoord5
            };
            
            // Структура Varyings остается почти такой же, но для ясности переименуем поля
            struct Varyings
            {
                float4 positionHCS    : SV_POSITION;
                float3 positionWS     : TEXCOORD0;
                float3 normalWS       : TEXCOORD1;
                float4 tangentWS      : TEXCOORD2;
                float4 shadowCoord    : TEXCOORD3;
                float4 color          : COLOR;      // Передаем цвет вертекса
                float2 uv0            : TEXCOORD4;
                float2 uv1            : TEXCOORD5;
                float  texBlend       : TEXCOORD6;
                half4  emissionData   : TEXCOORD7;
                half4  gapColor       : TEXCOORD8;
                half2  materialProps  : TEXCOORD9;
                half   fadeFactor     : TEXCOORD10;
            };
            
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half _VoxelScale, _BevelWidth, _GapWidth, _AmbientStrength, _BevelIntensity;
                float _CustomShadowBias, _FadeStartDistance, _FadeEndDistance;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.tangentWS = float4(TransformObjectToWorldDir(IN.tangentOS.xyz), IN.tangentOS.w);
                
                // Передаем данные из атрибутов напрямую
                OUT.color = IN.color;
                OUT.uv0 = IN.uv0;
                OUT.uv1 = IN.uv1;
                OUT.texBlend = IN.texBlend;
                OUT.emissionData = half4(IN.emissionData);
                OUT.gapColor = half4(IN.gapColor);
                OUT.materialProps = half2(IN.materialProps);

                float distanceToCamera = distance(OUT.positionWS, _WorldSpaceCameraPos.xyz);
                OUT.fadeFactor = smoothstep(_FadeEndDistance, _FadeStartDistance, distanceToCamera);
                
                // Используем _CustomShadowBias из свойств материала
                OUT.shadowCoord = TransformWorldToShadowCoord(OUT.positionWS);

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // --- Эффекты затухания ---
                half fade = IN.fadeFactor;
                // Применяем затухание к эффектам, чтобы они плавно исчезали
                half currentGapWidth = _GapWidth * fade;
                half currentBevelIntensity = _BevelIntensity * fade;
                
                // --- Логика Gap (швов) ---
                // Вычисляем UV для процедурной сетки, чтобы определить, где рисовать шов
                float3 blend_weights = abs(IN.normalWS);
                blend_weights /= (blend_weights.x + blend_weights.y + blend_weights.z);
                float2 grid_uv;
                if(blend_weights.x > 0.5) grid_uv = IN.positionWS.yz;
                else if(blend_weights.y > 0.5) grid_uv = IN.positionWS.xz;
                else grid_uv = IN.positionWS.xy;
                
                float2 grid_pos = frac(grid_uv * _VoxelScale);
                float2 gaps = step(currentGapWidth, grid_pos) * (1.0 - step(1.0 - currentGapWidth, grid_pos));
                float gapFactor = 1.0 - (gaps.x * gaps.y);
                // Если мы в шве (gapFactor > 0), можно сразу вернуть цвет шва
                // clip(-1) отбросит пиксель, если захотим сделать швы прозрачными
                if (gapFactor > 0.99) {
                    return IN.gapColor;
                }

                // --- Смешивание текстур ---
                half4 primaryTexture = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv0);
                half4 secondaryTexture = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv1);
                half4 blendedTexture = lerp(primaryTexture, secondaryTexture, IN.texBlend);
                
                // Умножаем на цвет вертекса, который теперь является смешанным цветом двух вокселей
                half4 finalAlbedo = blendedTexture * IN.color;
                clip(finalAlbedo.a - 0.01);

                // --- Освещение ---
                Light mainLight = GetMainLight(IN.shadowCoord);
                half3 ambient = SampleSH(IN.normalWS) * _AmbientStrength;
                half lambert = saturate(dot(IN.normalWS, mainLight.direction));
                // Умножаем цвет света на его ослабление тенью
                half3 diffuseLight = mainLight.color * mainLight.shadowAttenuation;
                half3 diffuse = lambert * diffuseLight;
                
                half3 litSurfaceColor = (ambient + diffuse) * finalAlbedo.rgb;

                // --- Эффект Bevel (скашивания краев) ---
                float2 localPos = frac(grid_uv * _VoxelScale);
                float2 dist_to_edge = min(localPos, 1.0 - localPos);
                float min_dist = min(dist_to_edge.x, dist_to_edge.y);
                float darkening = 1.0 - smoothstep(0.0, _BevelWidth, min_dist);
                // Затемняем цвет в зависимости от близости к краю и интенсивности эффекта
                litSurfaceColor = lerp(litSurfaceColor, half3(0,0,0), darkening * currentBevelIntensity);

                // --- Свечение (Emission) ---
                #if _EMISSION_ON
                    // Добавляем цвет свечения, умноженный на его силу
                    litSurfaceColor += IN.emissionData.rgb * IN.emissionData.a; // Используем .a как силу (strength)
                #endif
                
                // Возвращаем итоговый цвет. gapFactor здесь больше не нужен, т.к. мы отсекли пиксели шва ранее
                return half4(litSurfaceColor, 1.0);
            }
            ENDHLSL
        }

        // =========================================================================
        // Пасс 2: DepthOnly - Пассы глубины и нормалей также нуждаются в исправлении
        // =========================================================================
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            // Атрибуты для пасса глубины минимальны
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                half   fadeFactor  : TEXCOORD2;
            };
            
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half _VoxelScale, _BevelWidth, _GapWidth;
                float _FadeStartDistance, _FadeEndDistance;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                float distanceToCamera = distance(OUT.positionWS, _WorldSpaceCameraPos.xyz);
                OUT.fadeFactor = smoothstep(_FadeEndDistance, _FadeStartDistance, distanceToCamera);
                return OUT;
            }

            // ВАЖНО: Пасс глубины должен отбрасывать те же пиксели, что и основной пасс
            float frag(Varyings IN) : SV_Depth
            {
                half currentGapWidth = _GapWidth * IN.fadeFactor;
                
                float3 blend_weights = abs(IN.normalWS);
                blend_weights /= (blend_weights.x + blend_weights.y + blend_weights.z);
                float2 grid_uv;
                if (blend_weights.x > 0.5) grid_uv = IN.positionWS.yz;
                else if (blend_weights.y > 0.5) grid_uv = IN.positionWS.xz;
                else grid_uv = IN.positionWS.xy;
                
                float2 grid_pos = frac(grid_uv * _VoxelScale);
                float2 gaps = step(currentGapWidth, grid_pos) * (1.0 - step(1.0 - currentGapWidth, grid_pos));
                // Отбрасываем пиксели в швах, чтобы в них не записывалась глубина
                clip((gaps.x * gaps.y) - 0.5);

                // Логика "скругления" глубины для эффекта bevel остается
                float2 localPos = frac(grid_uv * _VoxelScale);
                float2 dist_to_edge = min(localPos, 1.0 - localPos);
                float min_dist_to_edge = min(dist_to_edge.x, dist_to_edge.y);
                float bevel_dist = _BevelWidth - min_dist_to_edge;
                
                float depth_offset_ws = 0;
                if (bevel_dist > 0)
                {
                    depth_offset_ws = sqrt(max(0, pow(_BevelWidth, 2) - pow(bevel_dist, 2)));
                }

                float3 new_pos_ws = IN.positionWS - IN.normalWS * (depth_offset_ws / _VoxelScale);
                float4 new_pos_hcs = TransformWorldToHClip(new_pos_ws);
                return new_pos_hcs.z / new_pos_hcs.w;
            }
            ENDHLSL
        }
        
        // Пасс DepthNormals опционален, но если используется, его тоже надо поправить аналогично DepthOnly
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Структуры такие же, как в DepthOnly
             struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                half   fadeFactor  : TEXCOORD2;
            };
            
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half _VoxelScale, _BevelWidth, _GapWidth;
                float _FadeStartDistance, _FadeEndDistance;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                float distanceToCamera = distance(OUT.positionWS, _WorldSpaceCameraPos.xyz);
                OUT.fadeFactor = smoothstep(_FadeEndDistance, _FadeStartDistance, distanceToCamera);
                return OUT;
            }

            void frag(Varyings IN, out half4 outNormalWS : SV_Target, out float outDepth : SV_Depth)
            {
                half currentGapWidth = _GapWidth * IN.fadeFactor;
                
                float3 blend_weights = abs(IN.normalWS);
                blend_weights /= (blend_weights.x + blend_weights.y + blend_weights.z);
                float2 grid_uv;
                if (blend_weights.x > 0.5) grid_uv = IN.positionWS.yz;
                else if (blend_weights.y > 0.5) grid_uv = IN.positionWS.xz;
                else grid_uv = IN.positionWS.xy;

                float2 grid_pos = frac(grid_uv * _VoxelScale);
                float2 gaps = step(currentGapWidth, grid_pos) * (1.0 - step(1.0 - currentGapWidth, grid_pos));
                clip((gaps.x * gaps.y) - 0.5);

                // Записываем нормаль
                outNormalWS = half4(normalize(IN.normalWS) * 0.5h + 0.5h, 1.0h);
                
                // Записываем скорректированную глубину
                float2 localPos = frac(grid_uv * _VoxelScale);
                float2 dist_to_edge = min(localPos, 1.0 - localPos);
                float min_dist_to_edge = min(dist_to_edge.x, dist_to_edge.y);
                float bevel_dist = _BevelWidth - min_dist_to_edge;
                float depth_offset_ws = 0;
                if (bevel_dist > 0)
                {
                    depth_offset_ws = sqrt(max(0, pow(_BevelWidth, 2) - pow(bevel_dist, 2)));
                }
                float3 new_pos_ws = IN.positionWS - IN.normalWS * (depth_offset_ws / _VoxelScale);
                float4 new_pos_hcs = TransformWorldToHClip(new_pos_ws);
                outDepth = new_pos_hcs.z / new_pos_hcs.w;
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}