Shader "Unlit/MinimapVisualizer"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float2 uv : TEXCOORD0; float4 vertex : SV_POSITION; };

            sampler2D _MainTex;
            
            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Читаем данные из нашей карты
                float4 biomeData = tex2D(_MainTex, i.uv);
                
                uint primaryID = (uint)biomeData.r;

                // Превращаем ID в цвет для отладки
                if (primaryID == 1) return fixed4(0, 0.5, 0, 1); // Neutral = Темно-зеленый
                if (primaryID == 2) return fixed4(0.8, 0.2, 0.2, 1); // Ereb = Красный
                if (primaryID == 3) return fixed4(0.2, 0.8, 0.2, 1); // Vital = Ярко-зеленый
                if (primaryID == 4) return fixed4(0.5, 0.2, 0.8, 1); // Psychic = Фиолетовый
                
                // Все остальное (включая нейтральную зону 0) - черное
                return fixed4(0, 0, 0, 1);
            }
            ENDHLSL
        }
    }
}