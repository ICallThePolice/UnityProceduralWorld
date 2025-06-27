// GoldenVoxelShader.shader
Shader "Voxel/GoldenVoxelShader"
{
    // Свойства остаются без изменений
    Properties
    {
        [Header(Visuals)]
        [MainTexture] _MainTex ("Texture Atlas", 2D) = "white" {}
        _Color("Base Color Tint", Color) = (1,1,1,1)
        _Smoothness ("Surface Smoothness", Range(0.0, 1.0)) = 0.5
        _Metallic ("Metallic", Range(0.0, 1.0)) = 0.1
        _AmbientStrength("Ambient Strength", Range(0.0, 1.0)) = 0.2
        [Header(Procedural Effects)]
        _VoxelScale ("Voxel Scale", Float) = 1.0
        _GapWidth ("Gap Width", Range(0.0, 0.1)) = 0.0
        _BevelWidth ("Bevel Width", Range(0.01, 0.49)) = 0.1
        _BevelIntensity ("Bevel Darkening", Range(0.0, 1.0)) = 0.8
        [Header(Technical)]
        _CustomShadowBias ("Shadow Bias", Range(-0.1, 0.1)) = 0.02
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float4 tangentOS    : TANGENT;
                float4 color        : COLOR;
                float2 texcoord0    : TEXCOORD0;
                float2 texcoord1    : TEXCOORD1;
                float  texcoord2    : TEXCOORD2;
            };

            // Структура для передачи данных в frag-шейдер. Использует прямую передачу.
            struct Varyings
            {
                float4 positionHCS  : SV_POSITION;
                float3 positionWS   : TEXCOORD0;
                float3 normalWS     : TEXCOORD1;
                float4 tangentWS    : TEXCOORD2;
                float4 shadowCoord  : TEXCOORD3;
                float2 uv0          : TEXCOORD4;
                float2 uv1          : TEXCOORD5;
                float  texBlend     : TEXCOORD6;
                float4 color        : COLOR;
            };
            
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half _VoxelScale, _BevelWidth, _Smoothness, _GapWidth, _AmbientStrength, _BevelIntensity, _Metallic;
                float _CustomShadowBias;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);
                OUT.normalWS = normalInputs.normalWS;
                OUT.tangentWS = float4(normalInputs.tangentWS.xyz, IN.tangentOS.w);
                OUT.color = IN.color;
                OUT.uv0 = IN.texcoord0;
                OUT.uv1 = IN.texcoord1;
                OUT.texBlend = IN.texcoord2;
                Light mainLight = GetMainLight();
                float3 biasedPositionWS = OUT.positionWS + OUT.normalWS * _CustomShadowBias;
                OUT.shadowCoord = TransformWorldToShadowCoord(biasedPositionWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 blend_weights = abs(IN.normalWS);
                blend_weights /= (blend_weights.x + blend_weights.y + blend_weights.z);
                float2 grid_uv;
                if (blend_weights.x > 0.5) grid_uv = IN.positionWS.yz;
                else if (blend_weights.y > 0.5) grid_uv = IN.positionWS.xz;
                else grid_uv = IN.positionWS.xy;
                float2 grid_pos = frac(grid_uv * _VoxelScale);
                float2 gaps = step(_GapWidth, grid_pos) * (1.0 - step(1.0 - _GapWidth, grid_pos));
                clip(gaps.x * gaps.y - 0.5);

                half4 primaryTexture = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv0);
                half4 secondaryTexture = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv1);
                half4 blendedTexture = lerp(primaryTexture, secondaryTexture, IN.texBlend);
                half4 finalAlbedo = blendedTexture * IN.color;

                Light mainLight = GetMainLight(IN.shadowCoord);
                half3 ambient = SampleSH(IN.normalWS) * _AmbientStrength;
                half lambert = saturate(dot(IN.normalWS, mainLight.direction));
                half3 diffuse = lambert * mainLight.color * mainLight.shadowAttenuation;
                half3 finalLitColor = (ambient + diffuse) * finalAlbedo.rgb;

                float2 localPos = frac(grid_uv * _VoxelScale);
                float2 dist_to_edge = min(localPos, 1.0 - localPos);
                float min_dist = min(dist_to_edge.x, dist_to_edge.y);
                float darkening = 1.0 - smoothstep(0.0, _BevelWidth, min_dist);
                finalLitColor = lerp(finalLitColor, half3(0,0,0), darkening * _BevelIntensity);

                return half4(finalLitColor, 1.0);
            }
            ENDHLSL
        }

        // =========================================================================
        // Пасс 2: DepthOnly - с логикой скругления глубины
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
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct DepthVaryings { float4 positionHCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };
            
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST; half4 _Color; half _VoxelScale, _BevelWidth, _Smoothness, _GapWidth, _AmbientStrength, _BevelIntensity, _Metallic; float _CustomShadowBias;
            CBUFFER_END

            DepthVaryings vert(Attributes IN) {
                DepthVaryings OUT = (DepthVaryings)0;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            float frag(DepthVaryings IN) : SV_Depth {
                float3 blend_weights = abs(IN.normalWS);
                blend_weights /= (blend_weights.x + blend_weights.y + blend_weights.z);
                float2 grid_uv;
                if (blend_weights.x > 0.5) grid_uv = IN.positionWS.yz; else if (blend_weights.y > 0.5) grid_uv = IN.positionWS.xz; else grid_uv = IN.positionWS.xy;
                float2 grid_pos = frac(grid_uv * _VoxelScale);
                float2 gaps = step(_GapWidth, grid_pos) * (1.0 - step(1.0 - _GapWidth, grid_pos));
                clip(gaps.x * gaps.y - 0.5);

                float2 localPos = frac(grid_uv * _VoxelScale);
                float2 dist_to_edge = min(localPos, 1.0 - localPos);
                float min_dist_to_edge = min(dist_to_edge.x, dist_to_edge.y);
                float bevel_dist = _BevelWidth - min_dist_to_edge;
                float depth_offset_ws = 0;
                if (bevel_dist > 0) {
                    depth_offset_ws = sqrt(max(0, pow(_BevelWidth, 2) - pow(bevel_dist, 2)));
                }
                float3 new_pos_ws = IN.positionWS - IN.normalWS * (depth_offset_ws / _VoxelScale);
                float4 new_pos_hcs = TransformWorldToHClip(new_pos_ws);
                return new_pos_hcs.z / new_pos_hcs.w;
            }
            ENDHLSL
        }

        // =========================================================================
        // Пасс 3: DepthNormals - с той же логикой скругления глубины
        // =========================================================================
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; };
            struct DepthVaryings { float4 positionHCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST; half4 _Color; half _VoxelScale, _BevelWidth, _Smoothness, _GapWidth, _AmbientStrength, _BevelIntensity, _Metallic; float _CustomShadowBias;
            CBUFFER_END

            DepthVaryings vert(Attributes IN) {
                DepthVaryings OUT = (DepthVaryings)0;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            void frag(DepthVaryings IN, out half4 outNormalWS : SV_Target, out float outDepth : SV_Depth) {
                float3 blend_weights = abs(IN.normalWS);
                blend_weights /= (blend_weights.x + blend_weights.y + blend_weights.z);
                float2 grid_uv;
                if (blend_weights.x > 0.5) grid_uv = IN.positionWS.yz; else if (blend_weights.y > 0.5) grid_uv = IN.positionWS.xz; else grid_uv = IN.positionWS.xy;
                float2 grid_pos = frac(grid_uv * _VoxelScale);
                float2 gaps = step(_GapWidth, grid_pos) * (1.0 - step(1.0 - _GapWidth, grid_pos));
                clip(gaps.x * gaps.y - 0.5);

                outNormalWS = half4(normalize(IN.normalWS) * 0.5h + 0.5h, 1.0h);

                float2 localPos = frac(grid_uv * _VoxelScale);
                float2 dist_to_edge = min(localPos, 1.0 - localPos);
                float min_dist_to_edge = min(dist_to_edge.x, dist_to_edge.y);
                float bevel_dist = _BevelWidth - min_dist_to_edge;
                float depth_offset_ws = 0;
                if (bevel_dist > 0) {
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