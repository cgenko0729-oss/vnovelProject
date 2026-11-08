// ============================================================================
// VN/CgFit
// 非 16:9 CG 的「合成成一张舞台尺寸的图」工具 shader（只在 VNCgFit 里 Blit 用，
// 不挂在任何 Image 上）。三个 pass：
//   0 = 从 sprite 的 uv 子区域拷贝（降采样链的第一步，自动处理图集 uv）
//   1 = 9-tap tent 模糊（在小 RT 上反复跑，等效于大半径高斯）
//   2 = 合成：模糊底 / 主色渐变底 + 暗角 + 前景投影 + 前景原图
//
// 为什么合成成一张图而不是做成两层 Image：转场（PlayBackground 的几何 shader）、
// Ken Burns、色调匹配、bgscroll 全都假设「舞台主图是一张铺满的 sprite」，
// 拆两层就得每个都改一遍，而且转场时前后景会分开换、画面割裂。
//
// 所有调色运算都在 gamma 空间做（Linear 工程里采样得到的是线性值，
// 直接乘 0.65 的视觉效果只相当于 gamma 下的 0.82，参数就和对比图对不上了）。
// ============================================================================
Shader "VN/CgFit"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _BlurTex ("Blurred Cover", 2D) = "black" {}
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        CGINCLUDE
        #include "UnityCG.cginc"

        sampler2D _MainTex;
        float4 _MainTex_TexelSize;
        float4 _SrcRect;      // 源 sprite 在贴图里的 uv 子区域 (xMin, yMin, w, h)
        float4 _CropRect;     // pass0：要拷贝的区域（相对 sprite 的 0~1）(xMin, yMin, w, h)

        struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

        v2f vert(appdata_img v)
        {
            v2f o;
            o.pos = UnityObjectToClipPos(v.vertex);
            o.uv = v.texcoord;
            return o;
        }

        float2 SpriteUv(float2 local) { return _SrcRect.xy + local * _SrcRect.zw; }

        float3 ToGamma(float3 c)
        {
        #if defined(UNITY_COLORSPACE_GAMMA)
            return c;
        #else
            return LinearToGammaSpace(c);
        #endif
        }

        float3 ToLinear(float3 c)
        {
        #if defined(UNITY_COLORSPACE_GAMMA)
            return c;
        #else
            return GammaToLinearSpace(c);
        #endif
        }
        ENDCG

        // ---------------- Pass 0：子区域拷贝 ----------------
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag(v2f i) : SV_Target
            {
                float2 local = _CropRect.xy + i.uv * _CropRect.zw;
                return fixed4(tex2D(_MainTex, SpriteUv(local)).rgb, 1);
            }
            ENDCG
        }

        // ---------------- Pass 1：9-tap tent 模糊 ----------------
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag(v2f i) : SV_Target
            {
                float2 d = _MainTex_TexelSize.xy;
                float3 c = tex2D(_MainTex, i.uv).rgb * 4;
                c += tex2D(_MainTex, i.uv + float2( d.x, 0)).rgb * 2;
                c += tex2D(_MainTex, i.uv + float2(-d.x, 0)).rgb * 2;
                c += tex2D(_MainTex, i.uv + float2(0,  d.y)).rgb * 2;
                c += tex2D(_MainTex, i.uv + float2(0, -d.y)).rgb * 2;
                c += tex2D(_MainTex, i.uv + d).rgb;
                c += tex2D(_MainTex, i.uv - d).rgb;
                c += tex2D(_MainTex, i.uv + float2(d.x, -d.y)).rgb;
                c += tex2D(_MainTex, i.uv + float2(-d.x, d.y)).rgb;
                return fixed4(c / 16, 1);
            }
            ENDCG
        }

        // ---------------- Pass 2：合成 ----------------
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            sampler2D _BlurTex;
            float4 _FgRect;       // 前景在输出 uv 里的位置 (xMin, yMin, xMax, yMax)，cover 时会超出 0~1
            float4 _OutSize;      // (宽px, 高px, 0, 0)
            float _Mode;          // 0 = blur  1 = blurvig  2 = gradient  3 = cover
            float _BgBrightness;  // 模糊底压暗（gamma）
            float _BgSaturation;  // 模糊底饱和度
            float _Vignette;      // 暗角强度 0~1
            float _TintStrength;  // blurvig：主色叠染强度
            float _ShadowAlpha;   // 前景投影最深处
            float _ShadowPx;      // 前景投影扩散像素

            // 源图左/右边缘一条窄带的平均色（gamma）：主色渐变与 blurvig 叠染用
            float3 EdgeColor(float side)
            {
                float3 acc = 0;
                [unroll] for (int k = 0; k < 12; k++)
                {
                    float v = (k + 0.5) / 12.0;
                    float u0 = side < 0.5 ? 0.012 : 0.988;
                    float u1 = side < 0.5 ? 0.035 : 0.965;
                    acc += ToGamma(tex2Dlod(_MainTex, float4(SpriteUv(float2(u0, v)), 0, 0)).rgb);
                    acc += ToGamma(tex2Dlod(_MainTex, float4(SpriteUv(float2(u1, v)), 0, 0)).rgb);
                }
                return acc / 24.0;
            }

            // 两侧渐变底：靠近屏幕边缘 = 0.25×主色，贴近前景 = 0.75×主色，t^1.6 曲线
            float3 GradientBg(float2 uv)
            {
                bool left = uv.x < (_FgRect.x + _FgRect.z) * 0.5;
                float3 col = EdgeColor(left ? 0 : 1);
                float t = left ? uv.x / max(1e-4, _FgRect.x)
                               : (1 - uv.x) / max(1e-4, 1 - _FgRect.z);
                t = pow(saturate(t), 1.6);
                return lerp(col * 0.25, col * 0.75, t);
            }

            float VignetteMask(float2 uv, float strength)
            {
                float2 p = (uv - 0.5) / float2(0.65, 0.75);
                float v = 1 - smoothstep(0.55, 1.05, length(p));
                return lerp(1 - strength, 1, v);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float3 bg;

                if (_Mode > 1.5 && _Mode < 2.5)
                {
                    bg = GradientBg(uv);
                }
                else
                {
                    bg = ToGamma(tex2D(_BlurTex, uv).rgb);
                    float lum = dot(bg, float3(0.299, 0.587, 0.114));
                    bg = lerp(lum.xxx, bg, _BgSaturation) * _BgBrightness;
                    if (_Mode > 0.5 && _Mode < 1.5)
                    {
                        float3 tint = saturate(GradientBg(uv) * 3);
                        bg = lerp(bg, bg * tint, _TintStrength);
                    }
                }
                bg *= VignetteMask(uv, _Vignette);

                // 前景投影：到前景矩形的像素距离做柔和衰减
                float2 px = uv * _OutSize.xy;
                float2 rMin = _FgRect.xy * _OutSize.xy;
                float2 rMax = _FgRect.zw * _OutSize.xy;
                float2 outside = max(rMin - px, px - rMax);
                float dist = length(max(outside, 0));
                float shadow = _ShadowAlpha * (1 - smoothstep(0, _ShadowPx, dist));
                bg *= 1 - shadow;

                // 前景
                float2 fgUv = (uv - _FgRect.xy) / (_FgRect.zw - _FgRect.xy);
                bool inside = all(fgUv >= 0) && all(fgUv <= 1);
                float3 col = inside ? ToGamma(tex2D(_MainTex, SpriteUv(saturate(fgUv))).rgb) : bg;

                return fixed4(ToLinear(col), 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
