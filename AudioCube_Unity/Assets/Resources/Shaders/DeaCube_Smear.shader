Shader "DeaCube/Smear"
{
    // The user (v4): "add a bit of smearing, like handrawn organic smearing that has no outline" — then "the smearing doesnt look organic
    // at all". A 2D paint BLOB on a camera-facing quad (CubeSmear places it at the cube's depth, just behind the cube): the signed distance
    // to a chain of up to 8 circles along the cube's real recent path (head = half the cube's width, tail ~0.18 of it, the tail end ROUND),
    // fused with a smooth minimum into one gooey drop; two octaves of value noise bend the distance (about 3-4 bumps per cube width, re-rolled
    // on twos with _Seed), so the silhouette is lumpy and soft-cornered; a couple of short drybrush gaps open near the tail. Flat paint in one
    // colour with a one-pixel anti-aliased edge: no outline, no shading, no glow.
    // _Circles[i] = (x, y, radius, path parameter 0 head .. 1 tail) in the quad's plane, world units, relative to the quad's centre;
    // _Quad = (width, height) of the quad in world units; _HeadR = the head radius.
    Properties
    {
        _Color ("Paint colour", Color) = (1, 1, 1, 1)
        _Seed ("Seed (re-rolled on twos)", Float) = 0
        _Count ("Circles", Float) = 0
        _HeadR ("Head radius", Float) = 0.3
        _Quad ("Quad size", Vector) = (1, 1, 0, 0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent-20" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Off
        Lighting Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color; float _Seed, _Count, _HeadR; float4 _Quad; float4 _Circles[8];
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 p : TEXCOORD0; };
            v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.p = (v.uv - 0.5) * _Quad.xy; return o; }

            float Hash12(float2 p) { float3 p3 = frac(float3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return frac((p3.x + p3.y) * p3.z); }
            float Noise2(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash12(i), b = Hash12(i + float2(1, 0)), c = Hash12(i + float2(0, 1)), d = Hash12(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }
            float SMin(float a, float b, float k) { float h = saturate(0.5 + 0.5 * (b - a) / k); return lerp(b, a, h) - k * h * (1.0 - h); }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 p = i.p;
                float k = 0.3 * _HeadR;
                float d = 1e5, along = 0.0, wsum = 0.0;
                [unroll] for (int n = 0; n < 8; n++)
                {
                    if (n >= (int)_Count) break;
                    float4 c = _Circles[n];
                    float dn = length(p - c.xy) - c.z;
                    d = n == 0 ? dn : SMin(d, dn, k);
                    // where along the drop this pixel is: the path parameter of the nearest circles
                    float w = 1.0 / (1e-3 + max(dn + c.z, 0.0) * max(dn + c.z, 0.0));
                    along += c.w * w; wsum += w;
                }
                along = wsum > 0.0 ? along / wsum : 0.0;
                // lumpy, soft-cornered silhouette: two octaves of value noise, ~3-4 bumps per cube width, re-rolled on twos
                float scale = 1.75 / max(_HeadR, 1e-3);
                float2 q = p * scale + _Seed * float2(1.7, 3.1);
                float nz = (Noise2(q) - 0.5) + 0.5 * (Noise2(q * 2.03 + 11.7) - 0.5);
                d += nz * 0.32 * _HeadR;
                // a couple of short drybrush gaps near the tail (stripes along the drop)
                float stripe = Noise2(float2(dot(p, float2(0.8, 0.6)) * scale * 1.6, _Seed * 0.7));
                float gap = smoothstep(0.58, 0.66, along) * smoothstep(0.7, 0.74, stripe);
                float aa = max(fwidth(d), 1e-5);
                float alpha = (1.0 - smoothstep(-aa, aa, d)) * (1.0 - gap) * _Color.a;
                clip(alpha - 0.01);
                return fixed4(_Color.rgb, alpha);
            }
            ENDCG
        }
    }
}
