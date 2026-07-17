// ============================================================================
// VN/RetroFilter
// 复古滤镜 overlay：一个 Pass 内通过 _Mode 切换两种风格。
//   Mode 0: 胶片（回忆用）—— 动态颗粒 + 竖向划痕 + 尘点 + 放映机亮度抖动 + 暗角
//   Mode 1: CRT（梦境用，柔和版）—— 扫描线 + RGB 荫罩条纹 + 滚动亮带 + 微闪烁 + 暗角
// 纯 overlay 叠加（不需要屏幕纹理，URP + uGUI 下无 GrabPass 也能用），
// 亮部/暗部按占比混合，_Intensity 作为总强度做淡入淡出。
// 时间用 floor 量化成"帧"（胶片 12fps / CRT 快闪），复刻逐帧跳变的质感。
// ============================================================================
Shader "VN/RetroFilter"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Intensity ("Master Intensity", Range(0,1)) = 0
        _Mode ("Mode (0 Film / 1 CRT)", Float) = 0
        _GrainAmount ("Film Grain Amount", Range(0,1)) = 0.55
        _ScratchAmount ("Film Scratch Amount", Range(0,1)) = 0.7
        _ScanlineCount ("CRT Scanline Count", Float) = 220
        _Tint ("Tint (film sepia / crt phosphor)", Color) = (1,1,1,1)
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
            float _Intensity;
            float _Mode;
            float _GrainAmount;
            float _ScratchAmount;
            float _ScanlineCount;
            half4 _Tint;

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
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
                int mode = (int)(_Mode + 0.5);

                float bright = 0.0; // 加亮成分（0~1）
                float dark = 0.0;   // 压暗成分（0~1）
                half3 brightColor = _Tint.rgb;

                // 共通暗角：四角压暗（胶片重、CRT 轻）
                float2 vc = uv - 0.5;
                float vignette = dot(vc, vc) * 2.2;

                if (mode == 0)
                {
                    // ---- 胶片：12fps 帧量化，每"帧"颗粒/划痕/尘点整体跳变 ----
                    float ft = floor(_Time.y * 12.0);

                    // 颗粒：细密噪点，亮暗各半
                    float g = hash21(uv * float2(680.0, 383.0) + ft * 7.13);
                    bright += step(1.0 - 0.06 * _GrainAmount, g) * 0.5;
                    dark += step(g, 0.06 * _GrainAmount) * 0.45;

                    // 竖向划痕：3 条候选，随帧跳位置、随机隐现，亮痕暗痕交替
                    for (int i = 0; i < 3; i++)
                    {
                        float seed = ft * 3.7 + i * 17.31;
                        float x = hash21(float2(seed, 4.7 + i));
                        float visible = step(0.55, hash21(float2(seed, 91.3)));
                        // 注意：line 是 HLSL 保留字，不能用作变量名
                        float scratch = 1.0 - smoothstep(0.0, 0.0022,
                            abs(uv.x - x));
                        float isBright = step(0.5, hash21(float2(seed, 55.1)));
                        float w = scratch * visible * _ScratchAmount
                                * (0.6 + 0.4 * hash21(float2(seed, uv.y * 3.0 + i)));
                        bright += w * isBright * 0.5;
                        dark += w * (1.0 - isBright) * 0.6;
                    }

                    // 尘点/毛发斑：大格随机偶发的暗斑
                    float2 cell = floor(uv * float2(26.0, 15.0));
                    float dseed = hash21(cell + ft * 11.7);
                    if (dseed > 0.985)
                    {
                        float2 dc = frac(uv * float2(26.0, 15.0)) - 0.5;
                        float blob = 1.0 - smoothstep(0.04, 0.16, length(dc));
                        dark += blob * 0.7;
                    }

                    // 放映机亮度抖动：整屏轻微忽明忽暗
                    float flicker = (hash21(float2(ft, 3.3)) - 0.5) * 0.1;
                    bright += max(flicker, 0.0);
                    dark += max(-flicker, 0.0);

                    // 胶片暗角更重
                    dark += vignette * 0.55;
                }
                else
                {
                    // ---- CRT（柔和版）：扫描线 + 荫罩 + 滚动亮带 + 微闪烁 ----
                    // 扫描线：横向暗线（柔和低对比）
                    float scan = 0.5 + 0.5 * cos(uv.y * _ScanlineCount * 6.2832);
                    dark += (1.0 - scan) * 0.30;

                    // RGB 荫罩：竖向三色相位条纹（叠加出屏幕像素感的彩色微光）
                    float px = uv.x * _ScanlineCount * 1.7778;
                    half3 stripe = half3(
                        0.5 + 0.5 * cos(px * 6.2832),
                        0.5 + 0.5 * cos(px * 6.2832 + 2.094),
                        0.5 + 0.5 * cos(px * 6.2832 + 4.189));
                    float stripeAmount = 0.10;
                    bright += stripeAmount;
                    brightColor = lerp(_Tint.rgb, _Tint.rgb * (0.4 + 1.2 * stripe),
                        0.85);

                    // 滚动亮带：一条柔亮横带缓慢向下扫过（老显像管回扫感）
                    float band = frac(uv.y + _Time.y * 0.11);
                    bright += smoothstep(0.0, 0.12, band) *
                              (1.0 - smoothstep(0.12, 0.3, band)) * 0.10;

                    // 微闪烁：高频帧量化的整屏亮度抖动（幅度小，柔和梦境不刺眼）
                    float cft = floor(_Time.y * 40.0);
                    bright += (hash21(float2(cft, 7.7)) - 0.5) * 0.05;

                    // CRT 暗角较轻 + 弧面感（横向边缘略压暗）
                    dark += vignette * 0.35 + abs(vc.x) * abs(vc.x) * 0.5;
                }

                bright = saturate(bright);
                dark = saturate(dark);
                float total = saturate(bright + dark);

                half4 col;
                col.rgb = brightColor * (bright / max(total, 0.001));
                col.a = total * _Intensity * _Tint.a * IN.color.a;
                return col;
            }
            ENDCG
        }
    }
}
