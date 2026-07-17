// ============================================================================
// VN/Shockwave
// 全屏情绪水波 overlay：受击/震惊时从中心荡开一圈水波纹。
// 不遮挡画面（透明叠加）：HDR 主波峰环 + 尾随衰减涟漪 + 波峰内侧微暗波谷，
// 配合 Bloom 波峰有辉光；真正的画面扭曲由 VNScreenShockwave 联动
// VNImageEffect 的波浪 UV 扭曲（_WaveAmount 脉冲）完成。
// _Progress 0 → 1 = 波纹从中心扩散到覆盖全屏并淡出。
// ============================================================================
Shader "VN/Shockwave"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        [HDR] _RingColor ("Ring Color", Color) = (1.7, 2.1, 2.8, 1)
        _Progress ("Progress", Range(0,1)) = 0
        _Strength ("Strength", Range(0,2)) = 1
        _Center ("Center (viewport)", Vector) = (0.5,0.5,0,0)
        _Aspect ("Aspect Ratio", Float) = 1.7778
        _RingWidth ("Ring Width", Range(0.005,0.2)) = 0.05
        _WaveFreq ("Wake Wave Frequency", Float) = 48
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Overlay"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

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
            half4 _RingColor;
            float _Progress;
            float _Strength;
            float4 _Center;
            float _Aspect;
            float _RingWidth;
            float _WaveFreq;

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
                float2 q = (IN.texcoord - _Center.xy) * float2(_Aspect, 1.0);
                float d = length(q);
                float radius = _Progress * 1.55; // 足以扫过最远角

                // 快进快出：开头 7% 迅速点亮，扩散过半后逐渐淡出
                float fade = saturate(_Progress * 14.0) * saturate((1.0 - _Progress) * 2.1);

                // 主波峰：radius 处的亮环（平方锐化让中心更亮、边缘柔）
                float crest = 1.0 - smoothstep(0.0, _RingWidth, abs(d - radius));
                crest *= crest;

                // 尾随涟漪：波峰后方一段衰减的余波环
                float rings = pow(saturate(0.5 + 0.5 * cos((d - radius) * _WaveFreq)), 6.0);
                float wake = smoothstep(radius - 0.4, radius - 0.05, d)
                           * (1.0 - smoothstep(radius - 0.05, radius + 0.02, d));

                // 波谷：波峰内侧一圈微暗，做出"水面下压"的体积感
                float trough = (1.0 - smoothstep(0.0, _RingWidth * 2.4,
                    abs(d - (radius - _RingWidth * 2.2)))) * 0.35;

                float bright = saturate(crest + rings * wake * 0.55);
                float total = saturate(bright + trough);

                half4 col;
                // 亮部显示 HDR 环色，暗部趋近黑：按占比混合
                col.rgb = _RingColor.rgb * (bright / max(total, 0.001));
                col.a = total * _Strength * fade * IN.color.a;
                return col;
            }
            ENDCG
        }
    }
}
