// 이미 그려진 픽셀을 지워 창을 그 자리만 투명하게 만드는 UI 셰이더.
// 창이 카드보다 길어 DWM은 창의 위 모서리만 둥글게 깎는다. 카드의 아래 두 모서리는 이것으로 깎는다.
// 알파가 1인 곳은 색과 알파를 모두 0으로 만들고, 0인 곳은 건드리지 않는다.
Shader "SAIUN/UI/EraseAlpha"
{
    Properties
    {
        [PerRendererData] _MainTex ("Mask", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
        }

        Cull Off
        ZWrite Off
        ZTest Always
        // 결과 = 원래 값 × (1 − 마스크 알파). 색과 알파 모두 같은 비율로 지운다.
        Blend Zero OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;

            struct Attributes
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.color = input.color;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            fixed4 Fragment(Varyings input) : SV_Target
            {
                fixed alpha = tex2D(_MainTex, input.uv).a * input.color.a;
                return fixed4(0, 0, 0, alpha);
            }
            ENDCG
        }
    }

    Fallback Off
}
