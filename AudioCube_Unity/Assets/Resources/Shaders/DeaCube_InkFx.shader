Shader "DeaCube/InkFx"
{
    // SPEC v3 §7 (package E): Fx's comic effects, flat colour + ink, alpha blended (never additive haze). One shader, one pooled
    // material per effect kind (_Shape), per-instance values through a MaterialPropertyBlock: _Color, _Intensity (life, 1 = fresh
    // .. 0 = gone), _Age (0..1 animation clock, advanced on twos by Fx), _Seed.
    //   0 accent     camera-facing quad centred on its source: a Ben-Day halo blooming out, radial ink speed lines, an ink ring
    //                on the halo's front, a pale pop at the centre (a designed comic accent, not a star silhouette)
    //   1 ring       Fx ring mesh (uv.y 0 inner .. 1 outer): a flat colour band between two ink lines that thins to one ink ring
    //   2 beam       vertical quad (uv.x across, uv.y up): hard-edged flat beam with ink edges and a pale core; the top dissolves
    //                into Ben-Day dots, lower and lower as it fades
    //   3 streak     quad (uv.x head 0 .. tail 1): a tapered ink speed line with a thin colour core; draws in, then retracts
    //   4 kirby      camera-facing quad: a cluster of ink / colour / pale energy dots that pop one after another
    //   5 solid      any mesh: flat colour with an ink rim (Fx.Multiples echoes)
    Properties
    {
        _Color ("Colour", Color) = (1, 1, 1, 1)
        _Intensity ("Life", Float) = 1
        _Age ("Age", Float) = 0
        _Seed ("Seed", Float) = 0
        _Shape ("Shape", Float) = 0
        _Ink ("Ink", Color) = (0.1, 0.06, 0.17, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "InkFx"
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex FxVert
            #pragma fragment FxFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Intensity;
                float _Age;
                float _Seed;
                float _Shape;
                half4 _Ink;
            CBUFFER_END

            struct FxAttributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct FxVaryings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD1; float3 positionWS : TEXCOORD2; };

            FxVaryings FxVert(FxAttributes v)
            {
                FxVaryings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                return o;
            }

            float Hash11(float p) { p = frac(p * 0.1031); p *= p + 33.33; p *= p + p; return frac(p); }

            float DotCover(float2 px, float cell, float radius)
            {
                float2 q = float2(px.x - px.y, px.x + px.y) * (0.70710678 / cell);
                float d = length(frac(q) - 0.5);
                float aa = 0.8 / cell;
                return (1.0 - smoothstep(radius - aa, radius + aa, d)) * step(0.02, radius);
            }

            // 0 comic accent, centred on its source (no star silhouette): a Ben-Day halo that blooms out and thins (big dots at the
            //   centre, small at its front), radial ink speed lines racing outward past it, a thin ink ring on the halo's front and a
            //   pale pop at the very centre for the first quarter of its life
            half4 Starburst(float2 uv, float2 px)
            {
                float2 p = uv * 2.0 - 1.0;
                float r = length(p);
                float a = atan2(p.y, p.x) / 6.2831853 + 0.5;
                float t = saturate(_Age);
                float e = 1.0 - (1.0 - t) * (1.0 - t) * (1.0 - t);
                float aa = max(fwidth(r) * 1.2, 1e-4);
                float s = max(_ScreenParams.y, 1.0) / 1080.0;
                float front = lerp(0.2, 0.86, e);
                float haloR = (1.0 - saturate(r / front)) * 0.52 * (1.0 - t * 0.8);
                float halo = DotCover(px, 7.0 * s, haloR) * step(r, front);
                // thin radiating speed lines (spindle-shaped: fine at both ends), racing outward past the halo; many sectors stay empty
                const float N = 22.0;
                float k = floor(a * N);
                float h = Hash11(k + _Seed * 17.0);
                float fa = abs(frac(a * N) - 0.5 - (Hash11(k * 5.1 + _Seed) - 0.5) * 0.5);
                float r0 = lerp(0.36, 0.62, e) + 0.1 * h;
                float len = lerp(0.14, 0.3, Hash11(k * 3.7 + _Seed));
                float along = saturate((r - r0) / len);
                float w = (0.012 + 0.07 * (1.0 - abs(along * 2.0 - 1.0))) * step(0.45, h) / max(r, 0.2);
                float aaA = max(fwidth(a * N) * 1.2, 0.01);
                float lineMask = (1.0 - smoothstep(w - aaA, w + aaA, fa)) * step(r0, r) * step(r, r0 + len) * (1.0 - smoothstep(0.65, 1.0, t));
                float ring = (1.0 - smoothstep(0.013 - aa, 0.013 + aa, abs(r - front))) * (1.0 - t);
                float popR = 0.17 * saturate(1.0 - t * 4.0);
                float pop = (1.0 - smoothstep(popR - aa, popR + aa, r)) * step(0.001, popR);
                half3 col = _Color.rgb;
                float alpha = halo * 0.92;
                col = lerp(col, half3(1.0, 0.98, 0.95), pop); alpha = max(alpha, pop);
                float ink = max(lineMask, ring);
                col = lerp(col, _Ink.rgb, ink); alpha = max(alpha, ink);
                return half4(col, alpha * _Color.a);
            }

            half4 Ring(float2 uv)
            {
                float v = uv.y;                                         // 0 inner .. 1 outer edge of the ring mesh
                float life = saturate(_Intensity);
                float half_ = lerp(0.045, 0.2, life);                   // a slim band that thins as the ring spreads
                float d = abs(v - 0.5) - half_;
                float aa = max(fwidth(v) * 1.2, 1e-4);
                float inside = 1.0 - smoothstep(-aa, aa, d);
                float inkW = min(0.06, half_);
                float ink = smoothstep(-inkW - aa, -inkW + aa, d);
                half3 c = lerp(lerp(_Color.rgb, half3(1.0, 1.0, 1.0), 0.18), _Ink.rgb, ink);
                return half4(c, inside * lerp(0.55, 0.9, ink) * saturate(life * 4.0) * _Color.a);
            }

            half4 Beam(float2 uv, float2 px)
            {
                float x = abs(uv.x * 2.0 - 1.0);
                float life = saturate(_Intensity);
                float halfW = lerp(0.46, 0.36, uv.y);
                float d = x - halfW;
                float aa = max(fwidth(x) * 1.2, 1e-4);
                float inside = 1.0 - smoothstep(-aa, aa, d);
                float ink = smoothstep(-0.2 - aa, -0.2 + aa, d);
                float core = 1.0 - smoothstep(0.16 - aa, 0.16 + aa, x);
                half3 c = lerp(lerp(_Color.rgb, half3(1.0, 1.0, 1.0), 0.28 + 0.5 * core), _Ink.rgb, ink);
                // the top dissolves into halftone, lower as the beam fades
                float y0 = lerp(0.06, 0.62, life);
                float f = saturate((uv.y - y0) / max(1.0 - y0, 0.05));
                float s = max(_ScreenParams.y, 1.0) / 1080.0;
                float body = uv.y < y0 ? 1.0 : DotCover(px, 6.0 * s, (1.0 - f) * 0.62);
                return half4(c, inside * body * saturate(life * 5.0) * 0.92 * _Color.a);
            }

            half4 Streak(float2 uv)
            {
                float y = abs(uv.y * 2.0 - 1.0);
                float halfW = lerp(1.0, 0.06, uv.x);
                float aaY = max(fwidth(y) * 1.2, 1e-4);
                float aaX = max(fwidth(uv.x) * 1.2, 1e-4);
                float inside = 1.0 - smoothstep(halfW - aaY, halfW + aaY, y);
                float drawIn = 1.0 - smoothstep(saturate(_Age / 0.3) - aaX, saturate(_Age / 0.3) + aaX, uv.x);
                float retract = smoothstep(saturate((_Age - 0.45) / 0.55) - aaX, saturate((_Age - 0.45) / 0.55) + aaX, uv.x);
                float coreLine = (1.0 - smoothstep(halfW * 0.32 - aaY, halfW * 0.32 + aaY, y)) * step(uv.x, 0.55);
                half3 c = lerp(_Ink.rgb, _Color.rgb, coreLine);
                return half4(c, inside * drawIn * retract * _Color.a);
            }

            half4 Kirby(float2 uv)
            {
                float2 p = uv * 2.0 - 1.0;
                float aa = max(fwidth(p.x) * 1.5, 1e-4);
                half4 o = half4(0, 0, 0, 0);
                [unroll] for (int k = 0; k < 14; k++)
                {
                    float fk = (float)k + _Seed * 31.0;
                    float ang = Hash11(fk * 3.1) * 6.2831853;
                    float rad = sqrt(Hash11(fk * 7.7)) * 0.72;
                    float2 c = float2(cos(ang), sin(ang)) * rad;
                    float delay = Hash11(fk * 1.9) * 0.35;
                    float grow = sin(3.14159265 * saturate((_Age - delay) / 0.6));
                    float rr = lerp(0.07, 0.2, Hash11(fk * 5.3)) * grow;
                    float cover = (1.0 - smoothstep(rr - aa, rr + aa, length(p - c))) * step(0.005, rr);
                    int kind = k % 3;
                    half3 dc = kind == 0 ? _Ink.rgb : (kind == 1 ? _Color.rgb : lerp(_Color.rgb, half3(1.0, 1.0, 1.0), 0.6));
                    o = lerp(o, half4(dc, 1.0), cover);
                }
                o.a *= _Color.a;
                return o;
            }

            half4 Solid(FxVaryings i)
            {
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float rim = smoothstep(0.55, 0.62, 1.0 - abs(dot(normalize(i.normalWS), V)));
                half3 c = lerp(_Color.rgb, _Ink.rgb, rim * 0.85);
                return half4(c, saturate(_Intensity) * _Color.a);
            }

            half4 FxFrag(FxVaryings i) : SV_Target
            {
                int shape = (int)round(_Shape);
                if (shape == 0) return Starburst(i.uv, i.positionCS.xy);
                if (shape == 1) return Ring(i.uv);
                if (shape == 2) return Beam(i.uv, i.positionCS.xy);
                if (shape == 3) return Streak(i.uv);
                if (shape == 4) return Kirby(i.uv);
                return Solid(i);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
