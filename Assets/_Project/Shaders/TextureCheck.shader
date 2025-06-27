// FinalVoxelShader.shader
Shader "Voxel/FinalVoxelShader"
{
    // Полный набор свойств, как мы и хотели
    Properties
    {
        [Header(Visuals)]
        [MainTexture] _MainTex ("Texture Atlas", 2D) = "white" {}
        _Color("Base Color Tint", Color) = (1,1,1,1)
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

        // --- Пасс 1: UniversalForward - для цвета, смешивания и простого освещения ---
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

            struct Varyings
            {
                float4 positionHCS  : SV_POSITION;
                float3 positionWS   : TEXCOORD0;
                float3 normalWS     : TEXCOORD1;
                float4 shadowCoord  : TEXCOORD3;
                float2 uv0          : TEXCOORD4;
                float2 uv1          : TEXCOORD5;
                float  texBlend     : TEXCOORD6;
                float4 color        : COLOR;
            };
            
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half _VoxelScale, _BevelWidth, _GapWidth, _AmbientStrength, _BevelIntensity;
                float _CustomShadowBias;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
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

        // --- Пасс 2: DepthOnly - простой и надежный ---
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            struct Attributes { float4 p:POSITION; float3 n:NORMAL; };
            struct Varyings { float4 p:SV_POSITION; float3 posWS:TEXCOORD0; float3 normalWS:TEXCOORD1; };
            
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half _VoxelScale, _BevelWidth, _GapWidth, _AmbientStrength, _BevelIntensity;
                float _CustomShadowBias;
            CBUFFER_END
            
            Varyings vert(Attributes i){Varyings o=(Varyings)0;o.posWS=TransformObjectToWorld(i.p.xyz);o.p=TransformObjectToHClip(i.p.xyz);o.normalWS=TransformObjectToWorldNormal(i.n);return o;}
            void frag(Varyings i){float3 bw=abs(i.normalWS);bw/=(bw.x+bw.y+bw.z);float2 guv;if(bw.x>0.5)guv=i.posWS.yz;else if(bw.y>0.5)guv=i.posWS.xz;else guv=i.posWS.xy;float2 gpos=frac(guv*_VoxelScale);float2 gaps=step(_GapWidth,gpos)*(1.0-step(1.0-_GapWidth,gpos));clip(gaps.x*gaps.y-0.5);}
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}