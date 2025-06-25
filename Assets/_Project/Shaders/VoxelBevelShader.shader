Shader "Custom/VoxelVisualBlendShader"
{
    Properties
    {
        [Header(Visual Style)]
        _MainTex ("Texture Atlas", 2D) = "white" {}
        _AtlasSize("Atlas Size (In Tiles)", Vector2) = (2,2)
        _Smoothness ("Surface Smoothness", Range(0.0, 1.0)) = 0.1
        _AmbientStrength("Ambient Strength", Range(0.0, 1.0)) = 0.3

        [Header(Blending)]
        _BlendNoiseScale ("Blend Noise Scale", Float) = 0.3
        _BlendNoiseIntensity ("Blend Noise Intensity", Range(0, 1.0)) = 0.5

        [Header(Virtual Voxel Settings)]
        _VoxelScale ("Voxel Scale", Float) = 2.0
        _GapWidth ("Gap Width", Range(0.0, 0.1)) = 0.03
        
        [Header(Bevel Effect)]
        _BevelWidth ("Bevel Width", Range(0.0, 0.5)) = 0.15
        _BevelIntensity ("Bevel Darkening", Range(0.0, 1.0)) = 0.7

        [Header(Shadow Correction)]
        _CustomShadowBias ("Shadow Bias", Range(0, 0.1)) = 0.05
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" "RenderType"="Opaque" "LightMode"="UniversalForward" }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float2 uv           : TEXCOORD0;
                float4 color        : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS  : SV_POSITION;
                float3 positionWS   : TEXCOORD0;
                float3 normalWS     : TEXCOORD1;
                float2 uv           : TEXCOORD2;
                float4 shadowCoord  : TEXCOORD3;
                float4 color        : COLOR;
            };

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            StructuredBuffer<float2> _VoxelUvCoordinates;

            CBUFFER_START(UnityPerMaterial)
                float2 _AtlasSize;
                float _VoxelScale, _BevelWidth, _Smoothness, _GapWidth, _AmbientStrength, _BevelIntensity, _CustomShadowBias, _BlendNoiseScale, _BlendNoiseIntensity;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.color = input.color;
                
                float3 biasedPositionWS = output.positionWS + output.normalWS * _CustomShadowBias;
                output.shadowCoord = TransformWorldToShadowCoord(biasedPositionWS);
                
                return output;
            }

            // Простая функция 2D шума для смешивания
            float simple_noise(float2 p) {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }

            // Функция затемнения для фаски
            half get_bevel_darkening_factor(float2 grid_uv) {
                float2 localPos = frac(grid_uv * _VoxelScale);
                float2 dist_to_edge = min(localPos, 1.0 - localPos);
                float min_dist = min(dist_to_edge.x, dist_to_edge.y);
                return 1.0 - smoothstep(0.0, _BevelWidth, min_dist);
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 1. Определение ориентации грани
                float3 blend_weights = abs(input.normalWS);
                blend_weights /= (blend_weights.x + blend_weights.y + blend_weights.z);
                float2 grid_uv;
                if (blend_weights.x > 0.5) grid_uv = input.positionWS.yz;
                else if (blend_weights.y > 0.5) grid_uv = input.positionWS.xz;
                else grid_uv = input.positionWS.xy;

                // 2. Заполнение зазоров
                float2 grid_pos = frac(grid_uv * _VoxelScale);
                if (any(grid_pos < _GapWidth) || any(grid_pos > 1.0 - _GapWidth)) {
                    return half4(0,0,0,1);
                }

                // 3. Распаковка данных о биомах из цвета вершины
                uint biomeA_surfaceID = round(input.color.r * 255.0);
                uint biomeB_surfaceID = round(input.color.g * 255.0);
                float blendStrength = input.color.b;

                // 4. Расчет UV для атласа (из буфера)
                float2 atlasOffsetA = _VoxelUvCoordinates[biomeA_surfaceID];
                float2 uvA = atlasOffsetA + input.uv / _AtlasSize;

                // Изначально цвет финальный равен текстуре основного биома
                half4 finalTexColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvA);

                // 5. Смешивание текстур, если мы на границе
                if (blendStrength > 0)
                {
                    float2 atlasOffsetB = _VoxelUvCoordinates[biomeB_surfaceID];
                    float2 uvB = atlasOffsetB + input.uv / _AtlasSize;
                    
                    half4 texB = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvB);
                    
                    float noise = (simple_noise(input.positionWS.xz * _BlendNoiseScale) - 0.5) * _BlendNoiseIntensity;
                    float finalBlend = saturate(smoothstep(0.4, 0.6, blendStrength + noise));
                    
                    finalTexColor = lerp(finalTexColor, texB, finalBlend);
                }

                // 6. Расчет освещения
                Light mainLight = GetMainLight(input.shadowCoord);
                half3 ambient = SampleSH(input.normalWS) * _AmbientStrength;
                half lambert = saturate(dot(input.normalWS, mainLight.direction));
                half3 diffuse = lambert * mainLight.color * mainLight.shadowAttenuation;
                half3 finalLitColor = (ambient + diffuse) * finalTexColor.rgb;
                
                // 7. Применение эффекта затемнения фаски
                half bevel_darkening = get_bevel_darkening_factor(grid_uv);
                finalLitColor *= (1.0 - bevel_darkening * _BevelIntensity);

                return half4(finalLitColor, 1.0);
            }
            ENDHLSL
        }
        
        // Pass для отбрасывания теней
        Pass
        {
            Name "ShadowCaster"
            Tags{"LightMode" = "ShadowCaster"}
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes input) {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }
            half4 frag(Varyings input) : SV_TARGET { return 0; }
            ENDHLSL
        }
    }
}