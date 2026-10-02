Shader "DeaCube/InkGlow"
{
    // SPEC v3 §7 (package E): Fx.Additive's shader. Same properties and behaviour as DeaCube/Additive (_MainTex, _Color, _Intensity,
    // _Fill uv.x cutoff; additive, vertex colour x texture x colour x intensity) but the texture's soft alpha is posterised into
    // _Steps flat levels with a 1-px anti-aliased edge, so every glow in the game reads as a designed flat shape (concentric
    // discs, flat rings) instead of haze. The global _DeaGlowOff (Look.FlatGlows = false) restores the smooth ramp.
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Float) = 1
        _Fill ("Fill (uv.x cutoff)", Float) = 2
        _Steps ("Flat levels (0 = smooth)", Float) = 3
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Blend One One
        ZWrite Off
        Cull Off
        Lighting Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _MainTex_ST; fixed4 _Color; float _Intensity; float _Fill; float _Steps;
            float _DeaGlowOff;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float2 rawuv : TEXCOORD1; fixed4 color : COLOR; };
            v2f vert (appdata v)
            {
                v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = TRANSFORM_TEX(v.uv, _MainTex); o.rawuv = v.uv; o.color = v.color * _Color; return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                if (i.rawuv.x > _Fill) discard;
                fixed4 t = tex2D(_MainTex, i.uv);
                float steps = _Steps * (1.0 - _DeaGlowOff);
                if (steps > 0.5)
                {
                    float s = t.a * steps;
                    float lo = floor(s);
                    float w = max(fwidth(s), 1e-4);
                    t.a = saturate((lo + smoothstep(0.5 - w, 0.5 + w, s - lo)) / steps);
                }
                fixed4 c = t * i.color;
                c.rgb *= c.a * _Intensity;
                return fixed4(c.rgb, c.a);
            }
            ENDCG
        }
    }
}
