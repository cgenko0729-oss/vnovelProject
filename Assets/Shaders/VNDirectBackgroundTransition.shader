// 背景 A 直接离场并露出下层背景 B：不经过全屏黑色遮罩。
// Mode 0 PageCurl / 1 Ripple / 2 InkBleed / 3 Shatter
Shader "VN/DirectBackgroundTransition"
{
    Properties
    {
        [PerRendererData] _MainTex ("Old Background", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Progress ("Progress", Range(0,1)) = 0
        _Mode ("Mode", Float) = 0
        _Center ("Center", Vector) = (0.5,0.5,0,0)
        _Aspect ("Aspect", Float) = 1.7778
        _UVRect ("Sprite UV Rect", Vector) = (0,0,1,1)
        _RectMinMax ("Local Rect", Vector) = (-960,-540,960,540)
        _Scatter ("Shatter Scatter", Float) = 0.42

        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
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
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1; // Shatter: triangle center in normalized rect
                float2 uv2 : TEXCOORD2; // Shatter: random seed
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 normalizedUv : TEXCOORD1;
                float shardProgress : TEXCOORD2;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float _Progress;
            float _Mode;
            float4 _Center;
            float _Aspect;
            float4 _UVRect;
            float4 _RectMinMax;
            float _Scatter;

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float valueNoise(float2 p)
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
                float result = 0.0;
                float amplitude = 0.55;
                for (int i = 0; i < 3; i++)
                {
                    result += valueNoise(p) * amplitude;
                    p = p * 2.03 + 17.13;
                    amplitude *= 0.5;
                }
                return result;
            }

            v2f vert(appdata_t v)
            {
                v2f o;
                o.uv = v.uv0;
                o.color = v.color * _Color;
                o.shardProgress = 0.0;

                float2 uvRange = max(_UVRect.zw - _UVRect.xy, float2(0.0001, 0.0001));
                o.normalizedUv = (v.uv0 - _UVRect.xy) / uvRange;

                if (_Mode > 2.5)
                {
                    float2 rectSize = _RectMinMax.zw - _RectMinMax.xy;
                    float2 shardCenter = _RectMinMax.xy + v.uv1 * rectSize;
                    float2 fromImpact = (v.uv1 - _Center.xy) * float2(_Aspect, 1.0);
                    float radial = saturate(length(fromImpact) / 1.05);
                    float delay = radial * 0.16 + v.uv2.x * 0.10;
                    float t = saturate((_Progress - delay) / max(0.001, 1.0 - delay));
                    float eased = t * t * (3.0 - 2.0 * t);
                    float2 direction = normalize(fromImpact + float2(0.0001, 0.0001));
                    direction.x /= max(_Aspect, 0.001);

                    float2 local = v.vertex.xy - shardCenter;
                    float angle = (v.uv2.x - 0.5) * 4.2 * eased;
                    float sineValue = sin(angle);
                    float cosineValue = cos(angle);
                    local = float2(local.x * cosineValue - local.y * sineValue,
                                   local.x * sineValue + local.y * cosineValue);
                    local *= lerp(1.0, 0.72, eased);

                    float travel = min(rectSize.x, rectSize.y) * _Scatter;
                    float2 translation = direction * travel * eased * (0.7 + v.uv2.x * 0.65);
                    translation.y -= travel * 0.32 * t * t;
                    v.vertex.xy = shardCenter + local + translation;
                    o.normalizedUv = v.uv1;
                    o.shardProgress = t;
                }

                o.vertex = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv01 = saturate(i.normalizedUv);
                float2 sampleUv = i.uv;
                fixed4 oldColor = tex2D(_MainTex, sampleUv) * i.color;
                int mode = (int)(_Mode + 0.5);

                if (mode == 0)
                {
                    // 从右向左卷起。页背使用镜像采样，卷边之外的半透明黑色形成投影。
                    float bend = sin(uv01.y * 3.14159265) * 0.065;
                    float fold = 1.08 - _Progress * 1.18;
                    float distanceToFold = uv01.x + bend - fold;
                    float curlWidth = 0.105;
                    float page = 1.0 - smoothstep(-0.008, 0.008, distanceToFold);
                    float curl = (1.0 - smoothstep(curlWidth * 0.72, curlWidth, abs(distanceToFold)))
                               * step(0.0, distanceToFold);
                    float active = saturate(_Progress * 14.0) * saturate((1.0 - _Progress) * 14.0);
                    float shadow = (1.0 - smoothstep(curlWidth, curlWidth * 1.85, distanceToFold))
                                 * step(curlWidth, distanceToFold) * active;

                    float reflectedX = fold - (distanceToFold * 0.65) - bend;
                    float2 backUv01 = float2(saturate(reflectedX), uv01.y);
                    float2 backUv = _UVRect.xy + backUv01 * (_UVRect.zw - _UVRect.xy);
                    fixed4 backColor = tex2D(_MainTex, backUv) * i.color;
                    backColor.rgb = backColor.rgb * 0.52 + fixed3(0.22, 0.17, 0.11);
                    fixed4 pageColor = lerp(oldColor, backColor, curl);
                    pageColor.rgb += curl * fixed3(0.28, 0.20, 0.10) * active;
                    pageColor.a *= saturate(page + curl);
                    fixed4 shadowColor = fixed4(0, 0, 0, shadow * 0.34);
                    return pageColor.a > shadowColor.a ? pageColor : shadowColor;
                }

                if (mode == 1)
                {
                    // 波纹内部直接挖掉旧背景，波前附近扭曲旧图，露出下面的新背景。
                    float2 q = (uv01 - _Center.xy) * float2(_Aspect, 1.0);
                    float distanceFromCenter = length(q);
                    float radius = _Progress * 1.18;
                    float ringDistance = distanceFromCenter - radius;
                    float wave = sin(ringDistance * 72.0) * exp(-abs(ringDistance) * 18.0);
                    float2 direction = q / max(distanceFromCenter, 0.0001);
                    float2 distortion = direction * wave * 0.012;
                    distortion.x /= max(_Aspect, 0.001);
                    float2 distorted01 = saturate(uv01 + distortion);
                    sampleUv = _UVRect.xy + distorted01 * (_UVRect.zw - _UVRect.xy);
                    oldColor = tex2D(_MainTex, sampleUv) * i.color;
                    float revealed = (1.0 - smoothstep(radius - 0.035, radius + 0.02, distanceFromCenter))
                                   * step(0.0001, _Progress);
                    float highlight = exp(-abs(ringDistance) * 75.0) * saturate(1.0 - _Progress);
                    oldColor.rgb += highlight * fixed3(0.18, 0.52, 0.85);
                    oldColor.a *= 1.0 - revealed;
                    return oldColor;
                }

                if (mode == 2)
                {
                    // 多墨团与纸纤维噪声共同挖去旧背景，边界略微染黑但不形成全屏黑幕。
                    float2 aspect = float2(_Aspect, 1.0);
                    float2 q0 = (uv01 - _Center.xy) * aspect;
                    float2 q1 = (uv01 - (_Center.xy + float2( 0.18,  0.10))) * aspect;
                    float2 q2 = (uv01 - (_Center.xy + float2(-0.16, -0.13))) * aspect;
                    float2 q3 = (uv01 - (_Center.xy + float2( 0.08, -0.22))) * aspect;
                    float noiseValue = fbm(uv01 * 6.0 + float2(fbm(uv01 * 3.1), fbm(uv01 * 3.7 + 8.0)));
                    float inkDistance = length(q0);
                    inkDistance = min(inkDistance, length(q1) * 1.28 + 0.05);
                    inkDistance = min(inkDistance, length(q2) * 1.22 + 0.08);
                    inkDistance = min(inkDistance, length(q3) * 1.35 + 0.12);
                    inkDistance += (noiseValue - 0.5) * 0.38;
                    float radius = _Progress * 1.35 - 0.06;
                    float revealed = (1.0 - smoothstep(radius - 0.07, radius + 0.035, inkDistance))
                                   * step(0.0001, _Progress);
                    float inkEdge = 1.0 - smoothstep(0.0, 0.065, abs(inkDistance - radius));
                    oldColor.rgb = lerp(oldColor.rgb, oldColor.rgb * fixed3(0.22, 0.16, 0.28), inkEdge * 0.75);
                    oldColor.a *= 1.0 - revealed;
                    return oldColor;
                }

                // Shatter：顶点阶段移动每个独立三角形，后段再淡出。
                oldColor.rgb += (1.0 - i.shardProgress) * 0.04 * fixed3(0.3, 0.65, 1.0);
                oldColor.a *= 1.0 - smoothstep(0.62, 1.0, i.shardProgress);
                return oldColor;
            }
            ENDCG
        }
    }
}
