// ============================================================================
// VN/ScreenTransition
// 全屏转场 Shader：一个 Pass 内通过 _Mode 切换多种转场图案。
// _Progress 0 → 1 = 覆盖率从无到全屏（转出），再 1 → 0 揭示新画面（转入）。
//   Mode 0: 噪声溶解（带 HDR 辉光边缘，复用 fbm 分形噪声）
//   Mode 1: 百叶窗（横向条带）
//   Mode 2: 瓦片翻转（随机顺序 + 对角线推进）
//   Mode 3: 圆形扩散（从 _Center 扩散，可对准说话角色）
//   Mode 4: 水墨晕染（圆形扩散 + 强噪声扰动边缘）
//   Mode 5: 纯色全覆盖（配 HDR 白色 = 爆闪转场；配低透明度 = 柔光罩）
//   Mode 7: 卷页（弯曲页缘、背光和投影）
//   Mode 8: 碎裂（放射碎片和裂缝高光）
//   Mode 9: 水波扩散（多重水波环）
//   Mode 10: 墨水晕染（多墨滴融合和飞墨颗粒）
// ============================================================================
Shader "VN/ScreenTransition"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        [HDR] _Color ("Cover Color", Color) = (0,0,0,1)
        [HDR] _EdgeColor ("Dissolve Edge Color", Color) = (2.5,1.6,0.6,1)
        _Progress ("Progress", Range(0,1)) = 0
        _Mode ("Mode", Float) = 0
        _NoiseScale ("Noise Scale", Float) = 7
        _Count ("Stripe/Tile Count", Float) = 9
        _Center ("Center (viewport)", Vector) = (0.5,0.5,0,0)
        _Aspect ("Aspect Ratio", Float) = 1.7778
        _EdgeWidth ("Dissolve Edge Width", Range(0.001,0.3)) = 0.07
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
            float _Progress;
            float _Mode;
            float _NoiseScale;
            float _Count;
            float4 _Center;
            float _Aspect;
            float _EdgeWidth;

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float fbm(float2 p)
            {
                float v = 0.0;
                float amp = 0.55;
                for (int k = 0; k < 3; k++)
                {
                    v += vnoise(p) * amp;
                    p = p * 2.03 + 17.13;
                    amp *= 0.5;
                }
                return v;
            }

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
                float2 uv = IN.texcoord;
                float p = _Progress;
                int mode = (int)(_Mode + 0.5);

                float mask = 0.0;
                float edge = 0.0;

                if (mode == 0)
                {
                    // 噪声溶解：d > 0 处被覆盖，边界处点亮辉光
                    float n = fbm(uv * _NoiseScale);
                    float d = (p * 1.06 - 0.03) - n;
                    mask = smoothstep(-0.008, 0.008, d);
                    edge = (1.0 - smoothstep(0.0, _EdgeWidth, abs(d)))
                           * saturate(p * 8.0) * saturate((1.0 - p) * 8.0);
                }
                else if (mode == 1)
                {
                    // 百叶窗：每条横带内部从下往上被覆盖
                    float s = frac(uv.y * _Count);
                    mask = smoothstep(s - 0.04, s + 0.04, p * 1.08 - 0.04);
                }
                else if (mode == 2)
                {
                    // 瓦片翻转：随机顺序 + 沿对角线推进，整块瓦片一起快速淡入
                    float2 grid = float2(_Count * _Aspect, _Count);
                    float2 id = floor(uv * grid);
                    float2 tileCenter = (id + 0.5) / grid;
                    float diag = (tileCenter.x + tileCenter.y) * 0.5;
                    float th = hash21(id) * 0.4 + diag * 0.6;
                    mask = saturate((p * 1.25 - 0.1 - th) / 0.1);
                }
                else if (mode == 3)
                {
                    // 圆形扩散：从 _Center 向外覆盖（宽高比校正保证是正圆）
                    float2 q = (uv - _Center.xy) * float2(_Aspect, 1.0);
                    float d = length(q);
                    float r = p * 2.1; // 足以覆盖最远角
                    mask = 1.0 - smoothstep(r - 0.05, r, d);
                }
                else if (mode == 4)
                {
                    // 水墨晕染：圆形扩散 + 强噪声扰动边界
                    float2 q = (uv - _Center.xy) * float2(_Aspect, 1.0);
                    float d = length(q) + (fbm(uv * _NoiseScale) - 0.5) * 0.5;
                    float r = p * 2.3 - 0.1;
                    mask = 1.0 - smoothstep(r - 0.12, r, d);
                }
                else if (mode == 6)
                {
                    // POV 眨眼：上下两片"眼睑"合拢再睁开，边缘带眼睑弧线
                    float curve = sin(uv.x * 3.14159) * 0.07; // 中间弧度更深
                    float coverage = p * (0.55 + curve);
                    float mTop = smoothstep(1.0 - coverage - 0.015, 1.0 - coverage + 0.015, uv.y);
                    float mBot = 1.0 - smoothstep(coverage - 0.015, coverage + 0.015, uv.y);
                    mask = max(mTop, mBot);
                }
                else if (mode == 7)
                {
                    // 卷页：右页缘向左卷起。正弦弯曲模拟纸张张力，宽带形成背面高光与投影。
                    float bend = sin(uv.y * 3.14159) * 0.075;
                    float fold = 1.055 - p * 1.13;
                    float signedDist = uv.x + bend - fold;
                    float paper = smoothstep(-0.012, 0.012, signedDist);
                    float curlBand = 1.0 - smoothstep(0.0, 0.115, abs(signedDist));
                    float shadow = (1.0 - smoothstep(0.0, 0.075, -signedDist)) * (1.0 - paper);
                    float active = saturate(p * 12.0) * saturate((1.0 - p) * 12.0);
                    mask = saturate(paper + shadow * 0.32 * active);
                    edge = curlBand * active * (0.45 + 0.55 * smoothstep(-0.04, 0.035, signedDist));
                }
                else if (mode == 8)
                {
                    // 碎裂：方格被对角线切成三角碎片，冲击波从中心按随机顺序点亮。
                    float2 q = (uv - _Center.xy) * float2(_Aspect, 1.0);
                    float radial = length(q) / 1.25;
                    float2 grid = float2(_Count * _Aspect, _Count);
                    float2 cell = floor(uv * grid);
                    float2 local = frac(uv * grid);
                    float triangle = step(1.0, local.x + local.y);
                    float shardNoise = hash21(cell + triangle * 19.37);
                    float threshold = radial * 0.62 + shardNoise * 0.38;
                    mask = smoothstep(threshold - 0.055, threshold + 0.025, p * 1.12);

                    float border = min(min(local.x, 1.0 - local.x), min(local.y, 1.0 - local.y));
                    float diagonal = abs(local.x + local.y - 1.0) * 0.707;
                    float crack = 1.0 - smoothstep(0.012, 0.055, min(border, diagonal));
                    float front = 1.0 - smoothstep(0.0, 0.12, abs(p * 1.12 - threshold));
                    edge = crack * front * saturate(p * 14.0) * saturate((1.0 - p) * 8.0);
                }
                else if (mode == 9)
                {
                    // 水波扩散：主圆形遮罩后跟随数道衰减波纹，边缘使用 HDR 高光。
                    float2 q = (uv - _Center.xy) * float2(_Aspect, 1.0);
                    float d = length(q);
                    float radius = p * 2.05;
                    float wave = sin((d - radius) * 42.0) * 0.018 * saturate(1.15 - d);
                    mask = 1.0 - smoothstep(radius - 0.055 + wave, radius + 0.02 + wave, d);
                    float rings = pow(saturate(0.5 + 0.5 * cos((d - radius) * 55.0)), 10.0);
                    float wake = smoothstep(radius - 0.34, radius - 0.03, d) * (1.0 - smoothstep(radius - 0.03, radius + 0.08, d));
                    edge = rings * wake * saturate(p * 9.0) * saturate((1.0 - p) * 7.0);
                }
                else if (mode == 10)
                {
                    // 墨水晕染：中心墨团与数个偏移墨滴融合，噪声塑造纤维边缘和飞墨颗粒。
                    float2 aspect = float2(_Aspect, 1.0);
                    float2 q0 = (uv - _Center.xy) * aspect;
                    float2 q1 = (uv - (_Center.xy + float2( 0.18,  0.10))) * aspect;
                    float2 q2 = (uv - (_Center.xy + float2(-0.16, -0.13))) * aspect;
                    float2 q3 = (uv - (_Center.xy + float2( 0.08, -0.22))) * aspect;
                    float2 q4 = (uv - (_Center.xy + float2(-0.22,  0.18))) * aspect;
                    float n = fbm(uv * _NoiseScale + float2(fbm(uv * 3.1), fbm(uv * 3.7 + 8.0)));
                    float inkDistance = length(q0);
                    inkDistance = min(inkDistance, length(q1) * 1.28 + 0.05);
                    inkDistance = min(inkDistance, length(q2) * 1.22 + 0.08);
                    inkDistance = min(inkDistance, length(q3) * 1.35 + 0.12);
                    inkDistance = min(inkDistance, length(q4) * 1.30 + 0.10);
                    inkDistance += (n - 0.5) * 0.42;
                    float radius = p * 2.25 - 0.08;
                    mask = 1.0 - smoothstep(radius - 0.11, radius + 0.025, inkDistance);

                    float speckle = step(0.91, hash21(floor(uv * 115.0))) * step(inkDistance, radius + 0.24);
                    speckle *= step(radius - 0.08, inkDistance) * saturate(p * 10.0);
                    mask = max(mask, speckle * 0.9);
                    edge = (1.0 - smoothstep(0.0, 0.085, abs(inkDistance - radius)))
                           * saturate(p * 8.0) * saturate((1.0 - p) * 7.0) * 0.45;
                }
                else
                {
                    // 纯色全覆盖（爆闪 / 柔光罩）
                    mask = p;
                }

                half4 col;
                col.rgb = _Color.rgb + _EdgeColor.rgb * edge;
                col.a = mask * _Color.a * IN.color.a;
                return col;
            }
            ENDCG
        }
    }
}
