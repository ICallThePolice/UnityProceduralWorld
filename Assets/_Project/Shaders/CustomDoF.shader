// CustomDoF.shader
Shader "Hidden/CustomDoF"
{
    Properties
    {
        // Эти параметры мы будем устанавливать из C# скрипта
        _MainTex ("Source Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 100
        ZWrite Off
        Cull Off

        Pass
        {
            Name "CustomDoF"
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // --- Структуры ---
            struct Attributes
            {
                float4 positionOS   : POSITION;
                float2 uv           : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;
                float2 screenPos    : TEXCOORD1;
            };

            // --- Переменные, которые мы будем передавать из C# ---
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            TEXTURE2D(_CameraDepthTexture);
            SAMPLER(sampler_CameraDepthTexture);
            
            float4x4 _InverseViewProjectionMatrix; // Матрица для восстановления мировой позиции
            
            float _FocusDistance;   // Дистанция от камеры до персонажа (16)
            float _SharpRange;      // Размер резкой зоны вокруг персонажа (+-16)
            float _BlurFalloff; // Размер переходной зоны от резкости к размытию (например, 8)
            float4 _MainTex_TexelSize; // Размер текстуры, чтобы правильно вычислять смещение для размытия

            // --- Вершинный шейдер (просто передает данные дальше) ---
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                // Получаем экранные координаты для доступа к карте глубины
                output.screenPos = output.positionCS.xy / output.positionCS.w;
                return output;
            }
            
            // Функция для восстановления мировой позиции пикселя по его глубине
            float3 ComputeWorldPos(float2 uv, float depth)
            {
                float4 ndcPos = float4(uv * 2.0 - 1.0, depth, 1.0);
                float4 worldPos = mul(_InverseViewProjectionMatrix, ndcPos);
                return worldPos.xyz / worldPos.w;
            }

            // --- Фрагментный шейдер (вся магия здесь) ---
            half4 frag(Varyings input) : SV_Target
            {
                // 1. Получаем оригинальный цвет пикселя
                half4 originalColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);

                // 2. Получаем глубину и восстанавливаем мировую позицию пикселя
                float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, input.uv);
                float3 worldPos = ComputeWorldPos(input.uv, rawDepth);
                float distanceToPixel = length(worldPos - _WorldSpaceCameraPos.xyz);
                
                // 3. Вычисляем фактор размытия на основе наших правил
                float distFromFocus = abs(distanceToPixel - _FocusDistance);
                // smoothstep(A, B, x) -> вернет 0, если x < A; 1, если x > B; и плавный переход между A и B.
                // Это идеально подходит для создания переходной зоны.
                float blurFactor = smoothstep(_SharpRange, _SharpRange + _BlurFalloff, distFromFocus);

                // Если фактор размытия почти ноль, нет смысла тратить ресурсы на размытие
                if (blurFactor < 0.01)
                {
                    return originalColor;
                }

                // 4. Простое и быстрое размытие (Box Blur на 4 сэмпла)
                // Для мобильных устройств это хороший компромисс между качеством и производительностью.
                half4 blurredColor = 0;
                float2 texelSize = _MainTex_TexelSize.xy * 2.0 * blurFactor; // Сила размытия зависит от blurFactor
                blurredColor += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + float2(texelSize.x, texelSize.y));
                blurredColor += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + float2(-texelSize.x, texelSize.y));
                blurredColor += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + float2(texelSize.x, -texelSize.y));
                blurredColor += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + float2(-texelSize.x, -texelSize.y));
                blurredColor /= 4.0;

                // 5. Смешиваем оригинальный цвет и размытый на основе фактора размытия
                return lerp(originalColor, blurredColor, blurFactor);
            }
            ENDHLSL
        }
    }
}