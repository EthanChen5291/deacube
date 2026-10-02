Shader "DeaCube/PresentFlat"
{
    // SPEC v3 §4 + §7 (package C): every flat, graphic element of the waterfall in one instanced alpha-blended pass - comic
    // FX (ink rings, spiky starbursts, flat beams, speed lines, halftone flashes, confetti dots), the tiles' role glyphs, the
    // landing-target shadow blobs and the stage's star dots. Shapes are analytic signed distances on a quad (uv -> p in
    // [-1, 1]^2): flat fill with a crisp ink rim (screen-space anti-aliasing), no soft glow.
    // Per instance (linear colours): _PFill rgba, _PInk rgba, _PShape x = type, y/z = shape params, w = ink width (p units).
    // Types: 0 disc, 1 ring (y radius, z half width), 2 star (y points, z inner radius), 3 speed line (y half width),
    // 4 box (y half width, z half height), 5 halftone disc (y dot density), 6 glyph (y = slice of _PGlyphs), 7 flat triangle spark.
    Properties
    {
        _PGlyphs ("Glyphs", 2DArray) = "" {}
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Off
        Offset -1, -1
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.5
            #pragma require 2darray
            #include "UnityCG.cginc"

            UNITY_DECLARE_TEX2DARRAY(_PGlyphs);
            float4 _PFogParams;   // x fog start, y fog end (distance)

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _PFill)
                UNITY_DEFINE_INSTANCED_PROP(float4, _PInk)
                UNITY_DEFINE_INSTANCED_PROP(float4, _PShape)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wpos : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.wpos = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0)).xyz;
                return o;
            }

            float StarSD(float2 p, float n, float inner)
            {
                float a = atan2(p.y, p.x);
                float c = abs(cos(a * n * 0.5));
                float r = lerp(inner, 1.0, pow(c, 3.0));
                return (length(p) - r) * 0.72;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float4 fill = UNITY_ACCESS_INSTANCED_PROP(Props, _PFill);
                float4 ink = UNITY_ACCESS_INSTANCED_PROP(Props, _PInk);
                float4 sh = UNITY_ACCESS_INSTANCED_PROP(Props, _PShape);
                float2 p = i.uv * 2.0 - 1.0;
                int type = (int)round(sh.x);
                float4 col = float4(0, 0, 0, 0);
                if (type == 6)
                {
                    float a = UNITY_SAMPLE_TEX2DARRAY(_PGlyphs, float3(i.uv, sh.y)).a;
                    col = float4(fill.rgb, fill.a * a);
                }
                else if (type == 5)
                {
                    float2 g = p * sh.y;
                    float2 cell = frac(g) - 0.5;
                    float fall = saturate(1.0 - length(p));
                    float rr = 0.5 * sqrt(fall);
                    float d = length(cell) - rr;
                    float aa = fwidth(d) + 1e-4;
                    col = float4(fill.rgb, fill.a * saturate(0.5 - d / aa));
                }
                else
                {
                    float d;
                    if (type == 1) d = abs(length(p) - sh.y) - sh.z;
                    else if (type == 2) d = StarSD(p, sh.y, sh.z);
                    else if (type == 3)
                    {
                        float w = sh.y * (0.35 + 0.65 * saturate(p.x * 0.5 + 0.5));
                        float2 q = p - float2(clamp(p.x, -1.0 + w, 1.0 - w), 0.0);
                        d = length(q) - w;
                    }
                    else if (type == 4) { float2 q = abs(p) - float2(sh.y, sh.z); d = max(q.x, q.y); }
                    else if (type == 7) { float2 q = abs(p); d = (q.x * 0.9 + q.y * 0.45) - 0.6; }
                    else d = length(p) - 1.0;
                    float aa = fwidth(d) + 1e-4;
                    float inside = saturate(0.5 - d / aa);                 // the whole shape (ink included)
                    float core = saturate(0.5 - (d + sh.w) / aa);          // the fill, inset by the ink width
                    float4 inkC = float4(ink.rgb, ink.a * inside);
                    col = lerp(inkC, float4(fill.rgb, fill.a * inside), core);
                }
                float dist = distance(_WorldSpaceCameraPos.xyz, i.wpos);
                float f = saturate((dist - _PFogParams.x) / max(_PFogParams.y - _PFogParams.x, 1e-3));
                col.a *= 1.0 - f * 0.85;
                return col;
            }
            ENDCG
        }
    }
    Fallback Off
}
