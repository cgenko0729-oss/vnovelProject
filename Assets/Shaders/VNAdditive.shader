// ============================================================================
// VN/Additive
// 加法混合发光 Shader，两个用途共用：
//   1. 悬浮粒子（ParticleSystem，顶点色 = 粒子颜色）
//   2. 图片背后的柔光光环（RawImage）
// _TintColor 支持 HDR（>1 的颜色分量会触发 URP Bloom 泛光）。
// ============================================================================
Shader "VN/Additive"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        [HDR] _TintColor ("Tint Color (HDR)", Color) = (1,1,1,1)
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
        Lighting Off
        ZWrite Off
        Blend SrcAlpha One   // 加法混合：只增亮，不遮挡

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            half4 _TintColor;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color;
                return OUT;
            }

            half4 frag(v2f IN) : SV_Target
            {
                half4 tex = tex2D(_MainTex, IN.texcoord);
                half4 c;
                c.rgb = tex.rgb * IN.color.rgb * _TintColor.rgb;
                c.a = tex.a * IN.color.a * _TintColor.a;
                return c;
            }
            ENDCG
        }
    }
}
