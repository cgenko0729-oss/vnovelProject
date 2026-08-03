// ============================================================================
// VN/ParticleAlpha
// 实体粒子的普通 Alpha 混合 Shader（花瓣 / 落叶 / 雨 / 雪用）。
//
// 【为什么不能用 VN/Additive】
// 加法混合（Blend SrcAlpha One）只会给背景「加亮」，永远无法遮挡背景，
// 所以粉色花瓣叠上明亮背景后三个通道全部溢出 → 被 Bloom + Tonemapping 压成白色。
// 花瓣是不透光的实体，必须用 SrcAlpha OneMinusSrcAlpha 才有「一片东西挡住了背景」的观感。
// 发光类粒子（星光/萤火虫/尘埃/光斑）仍然用 VN/Additive，那些本来就是光。
//
// _TintColor 仍支持 HDR：>1 时会被 Bloom 泛光（近景高光花瓣可以用一点点）。
// _SoftBlur 给近景层做「虚焦」用：>0 时做 5-tap 十字模糊，模拟浅景深下的散焦花瓣。
// ============================================================================
Shader "VN/ParticleAlpha"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        [HDR] _TintColor ("Tint Color (HDR)", Color) = (1,1,1,1)
        _SoftBlur ("Soft Blur (texel)", Range(0, 4)) = 0
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
        Blend SrcAlpha OneMinusSrcAlpha   // 普通透明混合：会遮挡背景

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
            float4 _MainTex_TexelSize;
            half4 _TintColor;
            half _SoftBlur;

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
                half4 tex;
                if (_SoftBlur > 0.001h)
                {
                    // 十字 5-tap。半径受限于 4 texel，图集每格留了透明边距，不会串到邻帧
                    float2 o = _MainTex_TexelSize.xy * _SoftBlur;
                    tex  = tex2D(_MainTex, IN.texcoord);
                    tex += tex2D(_MainTex, IN.texcoord + float2( o.x,  o.y));
                    tex += tex2D(_MainTex, IN.texcoord + float2(-o.x,  o.y));
                    tex += tex2D(_MainTex, IN.texcoord + float2( o.x, -o.y));
                    tex += tex2D(_MainTex, IN.texcoord + float2(-o.x, -o.y));
                    tex *= 0.2h;
                }
                else
                {
                    tex = tex2D(_MainTex, IN.texcoord);
                }

                half4 c;
                c.rgb = tex.rgb * IN.color.rgb * _TintColor.rgb;
                c.a   = tex.a   * IN.color.a   * _TintColor.a;
                return c;
            }
            ENDCG
        }
    }
}
