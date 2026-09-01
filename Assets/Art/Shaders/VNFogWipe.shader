// ============================================================================
// VN/FogWipe
// 擦雾小游戏的雾层 Shader（uGUI RawImage 专用）
//
// 雾 = 底图（当前 CG）的「模糊 + 提亮 + 偏雾色」版本，而不是一层纯白半透明：
//   剪影能透出来，玩家一眼看得见「那边有个人影」，自然知道往哪擦，
//   顺带让画面比纯白廉价感好一个档次。
//
// 擦除掩码 _MaskTex 是一张低分辨率 R8 贴图（256x144 就够——雾本身就是模糊的，
//   双线性放大 7.5 倍完全看不出来），r = 1 表示该处已擦净。
//
// ★ 边界必须用噪声扰动：掩码直接当 alpha 用会得到光滑的圆形边界，非常假。
//   噪声把边界打碎成水汽的形状，这一条对真实感的贡献超过任何其他单项。
//   噪声用 shader 内的 fbm 现算（同 VNImageEffect 的做法），免噪声贴图。
//
// UV 说明：RawImage 会把 uvRect 烘进顶点 uv，所以 IN.texcoord 已经是**图集内**的
//   坐标。_UVRect 用来把它反算回 0~1 的**局部**坐标，供掩码与噪声采样——
//   不反算的话，CG 一旦在图集里，擦痕会跟着图集偏移乱跑（VNScreenTransition 踩过）。
// ============================================================================
Shader "VN/FogWipe"
{
    Properties
    {
        [PerRendererData] _MainTex ("Base Texture (CG)", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _MaskTex ("Wipe Mask (r: 1=clean)", 2D) = "black" {}
        _UVRect ("Atlas UV Rect (xy=offset, zw=size)", Vector) = (0,0,1,1)

        [Header(Fog Look)]
        _FogColor ("Fog Color", Color) = (0.93,0.95,0.98,1)
        _FogMix ("Fog Color Mix", Range(0,1)) = 0.76
        _FogDensity ("Fog Density", Range(0,1)) = 0.92
        _BlurAmount ("Blur Radius (uv)", Range(0,0.05)) = 0.013
        _Brightness ("Brightness", Range(0.5,3)) = 1.3

        [Header(Edge and Grain)]
        _EdgeNoise ("Edge Noise", Range(0,1)) = 0.5
        _NoiseScale ("Noise Scale", Float) = 14
        _Grain ("Water Grain", Range(0,0.5)) = 0.075
        _Falloff ("Fog Falloff", Range(0.3,3)) = 1.0

        // uGUI 遮罩标配
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
            Name "VN_FOG_WIPE"
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
            sampler2D _MaskTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;

            float4 _UVRect;
            half4 _FogColor;
            float _FogMix;
            float _FogDensity;
            float _BlurAmount;
            float _Brightness;
            float _EdgeNoise;
            float _NoiseScale;
            float _Grain;
            float _Falloff;

            // ---- 程序化值噪声（免噪声贴图，同 VNImageEffect）----
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
                // 图集内 uv -> 局部 0~1（掩码与噪声都活在局部坐标里）
                float2 localUV = _UVRect.zw > 0.0001
                    ? (IN.texcoord - _UVRect.xy) / _UVRect.zw
                    : IN.texcoord;

                // ---- 雾的颜色：底图 9-tap 模糊 + 偏雾色 + 提亮 ----
                half4 texCol = tex2D(_MainTex, IN.texcoord);
                if (_BlurAmount > 0.0001)
                {
                    float r = _BlurAmount;
                    float d = r * 0.7071; // 对角
                    texCol += tex2D(_MainTex, IN.texcoord + float2( r, 0));
                    texCol += tex2D(_MainTex, IN.texcoord + float2(-r, 0));
                    texCol += tex2D(_MainTex, IN.texcoord + float2(0,  r));
                    texCol += tex2D(_MainTex, IN.texcoord + float2(0, -r));
                    texCol += tex2D(_MainTex, IN.texcoord + float2( d,  d));
                    texCol += tex2D(_MainTex, IN.texcoord + float2(-d,  d));
                    texCol += tex2D(_MainTex, IN.texcoord + float2( d, -d));
                    texCol += tex2D(_MainTex, IN.texcoord + float2(-d, -d));
                    texCol /= 9.0;
                }
                texCol += _TextureSampleAdd;

                half3 fogCol = lerp(texCol.rgb, _FogColor.rgb, _FogMix) * _Brightness;

                // 水汽颗粒：细密噪声，让整片雾不是一块死板的渐变
                if (_Grain > 0.0001)
                    fogCol += (fbm(localUV * _NoiseScale * 5.0) - 0.5) * _Grain;

                // ---- 擦除：掩码 + 噪声扰动的破碎边界 ----
                float m = tex2D(_MaskTex, localUV).r;
                float n = (fbm(localUV * _NoiseScale) - 0.48) * _EdgeNoise;

                float cleaned = saturate(m + n);
                float alpha = pow(saturate(1.0 - cleaned), _Falloff) * _FogDensity;

                half4 color = half4(saturate(fogCol), alpha * IN.color.a);

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
