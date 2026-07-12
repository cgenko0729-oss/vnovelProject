// ============================================================================
// VN/ImageEffect
// 视觉小说 2D 图片特效 Shader（uGUI Image / RawImage 通用）
// 功能：
//   1. 噪声溶解出场/退场（带 HDR 辉光边缘，可被 Bloom 拾取）
//   2. 斜向扫光（Shine Sweep，一道高光带扫过图片）
//   3. HDR 自发光（Emission，用于"呼吸发光"效果）
//   4. 闪白（Flash，出场瞬间的爆闪）
//   5. HSV 调色（色相偏移 / 饱和度 / 亮度）
//   6. 微波浪 UV 扭曲（可选的轻微"微风"感）
// 兼容 uGUI 的 RectMask2D / Mask 裁剪。
// ============================================================================
Shader "VN/ImageEffect"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [Header(Dissolve)]
        _DissolveAmount ("Dissolve Amount (0=hidden 1=shown)", Range(0,1)) = 1
        _DissolveScale ("Dissolve Noise Scale", Float) = 14
        _DissolveEdgeWidth ("Dissolve Edge Width", Range(0.001,0.3)) = 0.09
        [HDR] _DissolveEdgeColor ("Dissolve Edge Color", Color) = (2.5,1.6,0.6,1)

        [Header(Shine Sweep)]
        _ShineProgress ("Shine Progress", Range(-0.3,1.3)) = -0.3
        _ShineWidth ("Shine Width", Range(0.01,0.6)) = 0.15
        _ShineAngle ("Shine Angle (deg)", Range(0,180)) = 60
        [HDR] _ShineColor ("Shine Color (a = strength)", Color) = (1.8,1.7,1.4,0.8)

        [Header(Flash and Emission)]
        _FlashAmount ("Flash Amount", Range(0,1)) = 0
        _FlashColor ("Flash Color", Color) = (1,1,1,1)
        [HDR] _EmissionColor ("Emission Color", Color) = (1,0.85,0.6,1)
        _EmissionAmount ("Emission Amount", Range(0,3)) = 0

        [Header(Color Grading)]
        _HueShift ("Hue Shift", Range(-0.5,0.5)) = 0
        _Saturation ("Saturation", Range(0,2)) = 1
        _Brightness ("Brightness", Range(0,2)) = 1

        [Header(Wave Distortion)]
        _WaveAmount ("Wave Amount", Range(0,0.05)) = 0
        _WaveSpeed ("Wave Speed", Float) = 2
        _WaveFreq ("Wave Frequency", Float) = 8

        // ---- uGUI 必需的模板/裁剪属性 ----
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "VN_IMAGE_EFFECT"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;

            float _DissolveAmount;
            float _DissolveScale;
            float _DissolveEdgeWidth;
            half4 _DissolveEdgeColor;

            float _ShineProgress;
            float _ShineWidth;
            float _ShineAngle;
            half4 _ShineColor;

            float _FlashAmount;
            half4 _FlashColor;
            half4 _EmissionColor;
            float _EmissionAmount;

            float _HueShift;
            float _Saturation;
            float _Brightness;

            float _WaveAmount;
            float _WaveSpeed;
            float _WaveFreq;

            // ---- 程序化值噪声（免噪声贴图）----
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

            // 3 层叠加分形噪声，输出约 0~0.96
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

            float3 rgb2hsv(float3 c)
            {
                float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
                float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
                float d = q.x - min(q.w, q.y);
                float e = 1.0e-10;
                return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
            }

            float3 hsv2rgb(float3 c)
            {
                float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
                float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
                return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
            }

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            half4 frag(v2f IN) : SV_Target
            {
                // 1. 微波浪 UV 扭曲
                float2 uv = IN.texcoord;
                uv.x += sin(uv.y * _WaveFreq + _Time.y * _WaveSpeed) * _WaveAmount;

                half4 color = (tex2D(_MainTex, uv) + _TextureSampleAdd) * IN.color;

                // 2. HSV 调色
                float3 hsv = rgb2hsv(color.rgb);
                hsv.x = frac(hsv.x + _HueShift + 1.0);
                hsv.y = saturate(hsv.y * _Saturation);
                color.rgb = hsv2rgb(hsv) * _Brightness;

                // 3. 斜向扫光
                float ang = radians(_ShineAngle);
                float2 dir = float2(cos(ang), sin(ang));
                float band = dot(IN.texcoord - 0.5, dir) + 0.5;
                float s = 1.0 - saturate(abs(band - _ShineProgress) / max(_ShineWidth, 1e-4));
                s = s * s;
                color.rgb += _ShineColor.rgb * (s * _ShineColor.a);

                // 4. HDR 自发光（呼吸发光）
                color.rgb += _EmissionColor.rgb * _EmissionAmount;

                // 5. 噪声溶解 + 辉光边缘
                float n = fbm(IN.texcoord * _DissolveScale);
                float cut = lerp(1.02, -0.02, _DissolveAmount);
                float d = n - cut; // > 0 即可见
                float edge = (1.0 - smoothstep(0.0, _DissolveEdgeWidth, d)) * step(0.0, d);
                edge *= saturate((1.0 - _DissolveAmount) * 8.0); // 完全显示后不再有边缘光
                color.rgb += _DissolveEdgeColor.rgb * edge;
                color.a *= smoothstep(-0.005, 0.005, d);

                // 6. 闪白
                color.rgb = lerp(color.rgb, _FlashColor.rgb, _FlashAmount);

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
