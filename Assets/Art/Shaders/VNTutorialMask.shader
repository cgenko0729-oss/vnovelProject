// ============================================================================
// VN/TutorialMask
// 教程暗幕：整屏压暗 + 在指定位置挖出最多 4 个洞（圆角矩形或椭圆），
// 洞口一圈 HDR 描边（配合 Bloom 会发光，起到「看这里」的指引作用）。
//
// 为什么不用四块 Image 围出矩形洞：那样只能做硬边矩形单洞，没有圆角、
// 没有羽化、没法多洞，视觉上一眼就是「工具」而不是演出。
//
// 洞的坐标全部是**本图 uv 空间的 0~1**（由 VNTutorialMask 每帧从目标
// RectTransform 的世界四角换算出来），所以换分辨率、换布局都不用改资产。
// _Holes[i] = (中心x, 中心y, 半宽, 半高)，_HoleCount 为 0 时就是纯暗幕。
// ============================================================================
Shader "VN/TutorialMask"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Dim Color", Color) = (0, 0, 0.01, 0.72)
        [HDR] _EdgeColor ("Edge Color", Color) = (1.6, 1.35, 0.75, 1)
        _EdgeWidth ("Edge Width (uv)", Float) = 0.004
        _Feather ("Feather (uv)", Float) = 0.012
        _Corner ("Corner Radius (uv)", Float) = 0.02
        _Aspect ("Aspect Ratio", Float) = 1.7778
        _Shape ("Shape (0=rect 1=ellipse)", Float) = 0
        _HoleCount ("Hole Count", Float) = 0
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
            half4 _Color;
            half4 _EdgeColor;
            float _EdgeWidth;
            float _Feather;
            float _Corner;
            float _Aspect;
            float _Shape;
            float _HoleCount;
            float4 _Holes[4];

            v2f vert(appdata_t v)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color;
                return OUT;
            }

            // 圆角矩形的有符号距离（< 0 = 洞内）。p / b 已做过宽高比校正
            float SdRoundedBox(float2 p, float2 b, float r)
            {
                r = min(r, min(b.x, b.y));
                float2 q = abs(p) - (b - r);
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
            }

            // 椭圆的近似有符号距离（够用：只拿它做羽化与描边的过渡）
            float SdEllipse(float2 p, float2 b)
            {
                float k = length(p / max(b, 1e-5));
                return (k - 1.0) * min(b.x, b.y);
            }

            half4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.texcoord;
                float2 aspect = float2(_Aspect, 1.0);

                // coverage：洞的覆盖度（1 = 完全在洞内，暗幕在这里被抠掉）
                // ring：描边强度，取所有洞里最亮的一圈
                float coverage = 0.0;
                float ring = 0.0;

                int count = (int)_HoleCount;
                [unroll]
                for (int i = 0; i < 4; i++)
                {
                    if (i >= count) continue;

                    float2 c = _Holes[i].xy;
                    float2 h = max(_Holes[i].zw, 1e-5);
                    float2 p = (uv - c) * aspect;
                    float2 b = h * aspect;

                    float d = _Shape > 0.5 ? SdEllipse(p, b) : SdRoundedBox(p, b, _Corner);

                    // 洞内 → 1，越过羽化带 → 0
                    coverage = max(coverage, 1.0 - smoothstep(-_Feather, 0.0, d));
                    // 描边：贴着 d = 0 的一圈
                    ring = max(ring, 1.0 - smoothstep(0.0, max(_EdgeWidth, 1e-5), abs(d)));
                }

                half3 rgb = _Color.rgb;
                half a = _Color.a * (1.0 - coverage);

                // 描边压在暗幕之上：颜色整体推向 HDR 边色，alpha 取两者较大
                // （洞内 alpha 已经是 0，不这样做描边会被抠掉一半）
                half edge = saturate(ring) * _EdgeColor.a;
                rgb = lerp(rgb, _EdgeColor.rgb, saturate(ring));
                a = max(a, edge);

                a *= IN.color.a;
                return half4(rgb, a);
            }
            ENDCG
        }
    }
}
