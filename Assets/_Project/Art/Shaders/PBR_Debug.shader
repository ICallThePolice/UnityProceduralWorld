// --- PBR_Debug.shader (ФИНАЛЬНАЯ ПОЛНАЯ ВЕРСИЯ) ---
Shader "Voxel/PBR_Debug"
{
    Properties
    {
        [MainTexture] _MainTex ("Texture Atlas", 2D) = "white" {}
        _AmbientStrength("Ambient Strength", Range(0.0, 1.0)) = 0.2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        // =========================================================================
        // Пасс 1: Основной рендер (цвет, свет, PBR)
        // =========================================================================
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "ShaderPasses.hlsl"
            ENDHLSL
        }

        // =========================================================================
        // Пасс 2: Рендер глубины (для теней, DoF, SSAO)
        // =========================================================================
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag_depth
            #include "ShaderPasses.hlsl"
            ENDHLSL
        }
        
        // =========================================================================
        // Пасс 3: Рендер нормалей и глубины (для DoF, SSAO)
        // =========================================================================
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag_depthnormals
            #include "ShaderPasses.hlsl"
            ENDHLSL
        }

        // =========================================================================
        // Пасс 4: Рендер теней (чтобы объект отбрасывал тени)
        // =========================================================================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert_shadow
            #pragma fragment frag_shadow
            #include "ShaderPasses.hlsl"
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}