Shader "DeaCube/SkyToon"
{
    // SPEC v3 §7 (package E): a posterised pastel-dusk sky on the camera-centred sky sphere (Fx.BuildSky). Four flat bands over a
    // peach horizon glow; each band edge is an ordered (Bayer 4x4, 2 px blocks at 1080p) dither ramp, like printed banding.
    // Flat shapes: a pale sun disc with two thin halo rings at the key light's azimuth, a few two-tone clouds on flat bottoms
    // drifting on twos, and four-point stars in the upper bands that twinkle on twos. _Tint (the lit island's colour, set by Fx)
    // leans the upper bands toward the song. Below the horizon: _Below (= the sea's horizon colour, so the far sea melts in).
    Properties
    {
        _C0 ("Horizon glow", Color) = (1.0, 0.84, 0.69, 1)
        _C1 ("Low band", Color) = (0.97, 0.66, 0.75, 1)
        _C2 ("Mid band", Color) = (0.75, 0.55, 0.84, 1)
        _C3 ("High band", Color) = (0.49, 0.41, 0.77, 1)
        _C4 ("Top", Color) = (0.24, 0.2, 0.53, 1)
        _Below ("Below the horizon", Color) = (0.69, 0.54, 0.78, 1)
        _Edges ("Band edges (sin of elevation)", Vector) = (0.02, 0.055, 0.1, 0.16)
        _DitherWidth ("Dither ramp width", Float) = 0.022
        _Tint ("Tint", Color) = (0.6, 0.45, 0.8, 1)
        _TintAmt ("Tint amount", Range(0, 1)) = 0.22
        _SunDir ("Sun direction", Vector) = (0.5, 0.1, 0.86, 0)
        _SunColor ("Sun", Color) = (1.0, 0.95, 0.84, 1)
        _CloudColor ("Cloud", Color) = (1.0, 0.88, 0.92, 1)
        _CloudShade ("Cloud underside", Color) = (0.9, 0.7, 0.88, 1)
        _StarColor ("Stars", Color) = (1.0, 0.96, 0.86, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "RenderPipeline" = "UniversalPipeline" "PreviewType" = "Skybox" "IgnoreProjector" = "True" }
        Cull Front
        ZWrite Off

        Pass
        {
            Name "SkyToon"
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex SkyVert
            #pragma fragment SkyFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "DeaStage.hlsl"   // v9 (L) the stage lights (all zero = no change)

            CBUFFER_START(UnityPerMaterial)
                half4 _C0, _C1, _C2, _C3, _C4, _Below;
                float4 _Edges;
                float _DitherWidth;
                half4 _Tint;
                half _TintAmt;
                float4 _SunDir;
                half4 _SunColor, _CloudColor, _CloudShade, _StarColor;
            CBUFFER_END

            struct SkyAttributes { float4 positionOS : POSITION; };
            struct SkyVaryings { float4 positionCS : SV_POSITION; float3 dirWS : TEXCOORD0; };

            SkyVaryings SkyVert(SkyAttributes v)
            {
                SkyVaryings o;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(wp);
                o.dirWS = wp - GetCameraPositionWS();
                return o;
            }

            static const float kBayer4[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
            float Bayer(float2 cellPx)
            {
                uint2 q = (uint2)fmod(abs(cellPx), 4.0);
                return (kBayer4[q.y * 4 + q.x] + 0.5) / 16.0;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // a flat cloud: three puffs and a small tail on a flat bottom (units: one cloud is ~2.6 wide, ~1 tall); < 0 inside
            float CloudSdf(float2 q)
            {
                float d = length(q - float2(-0.78, 0.02)) - 0.42;
                d = min(d, length(q - float2(0.0, 0.26)) - 0.6);
                d = min(d, length(q - float2(0.74, 0.06)) - 0.45);
                d = min(d, length(q - float2(1.28, -0.02)) - 0.26);
                return max(d, -q.y - 0.14);
            }

            float WrapAngle(float a) { return a - 6.2831853 * round(a / 6.2831853); }

            half4 SkyFrag(SkyVaryings i) : SV_Target
            {
                float3 d = normalize(i.dirWS);
                float y = d.y;
                float s = max(_ScreenParams.y, 1.0) / 1080.0;
                float blockPx = max(1.0, round(2.0 * s));
                float th = Bayer(floor(i.positionCS.xy / blockPx));
                float t12 = floor(_Time.y * 12.0) / 12.0;   // on twos

                // posterised bands with ordered-dither ramps between them
                float w = _DitherWidth;
                float b0 = step(th, saturate((y - _Edges.x) / w + 0.5));
                float b1 = step(th, saturate((y - _Edges.y) / w + 0.5));
                float b2 = step(th, saturate((y - _Edges.z) / w + 0.5));
                float b3 = step(th, saturate((y - _Edges.w) / w + 0.5));
                half3 col = _C0.rgb;
                col = lerp(col, _C1.rgb, b0);
                col = lerp(col, _C2.rgb, b1);
                col = lerp(col, _C3.rgb, b2);
                col = lerp(col, _C4.rgb, b3);
                col = lerp(col, lerp(col, _Tint.rgb, 0.6), _TintAmt * b1);

                float az = atan2(d.x, d.z);
                float el = asin(clamp(y, -1.0, 1.0));

                // stars in the upper bands (4-point, twinkling on twos)
                float2 sg = float2(az, el) * 26.0;
                float2 cellId = floor(sg);
                float h = Hash21(cellId);
                float2 sp = frac(sg) - 0.5 - (float2(Hash21(cellId + 7.1), Hash21(cellId + 3.3)) - 0.5) * 0.5;
                float starR = (0.1 + 0.12 * Hash21(cellId + 1.7)) * (0.72 + 0.28 * sin(t12 * 2.1 + h * 40.0));
                float star = sqrt(abs(sp.x)) + sqrt(abs(sp.y));
                float starA = (1.0 - smoothstep(sqrt(starR) * 0.95, sqrt(starR) * 1.05, star)) * step(0.9, h) * smoothstep(_Edges.y, _Edges.z, y);
                col = lerp(col, _StarColor.rgb, starA * 0.85);

                // sun: flat disc + two thin halo rings, at the key light's azimuth, low over the horizon
                float3 sunDir = normalize(_SunDir.xyz);
                float ca = dot(d, sunDir);
                float ang = acos(clamp(ca, -1.0, 1.0));
                float aaS = max(fwidth(ang), 1e-5);
                float disc = 1.0 - smoothstep(0.036 - aaS, 0.036 + aaS, ang);
                float ring1 = 1.0 - smoothstep(0.0028 - aaS, 0.0028 + aaS, abs(ang - 0.054));
                float ring2 = 1.0 - smoothstep(0.002 - aaS, 0.002 + aaS, abs(ang - 0.072));
                float aboveSea = step(0.0, y);
                col = lerp(col, _SunColor.rgb, saturate(disc + ring1 * 0.55 + ring2 * 0.35) * aboveSea);

                // flat two-tone clouds low in the sky, drifting on twos
                [unroll] for (int k = 0; k < 6; k++)
                {
                    float fk = (float)k;
                    float cAz = fk * 1.0472 + 0.35 * sin(fk * 2.3) + t12 * 0.004 * (1.0 + 0.3 * fk);
                    float cEl = 0.035 + 0.04 * frac(sin(fk * 12.9898) * 43758.5453) + 0.025 * step(3.5, fk);
                    float sc = 0.028 + 0.014 * frac(sin(fk * 78.233) * 12345.678);
                    float2 q = float2(WrapAngle(az - cAz), el - cEl) / sc;
                    float cd = CloudSdf(q);
                    float aaC = max(fwidth(cd), 1e-4);
                    float inside = 1.0 - smoothstep(-aaC, aaC, cd);
                    half3 cc = lerp(_CloudShade.rgb, _CloudColor.rgb, step(0.1, q.y));
                    col = lerp(col, cc, inside * aboveSea * 0.92);
                }

                // below the horizon: the sea's far colour (the sea disc covers most of it)
                col = y < 0.0 ? _Below.rgb : col;
                col = DeaStageGrade(col, _DeaStageDim, 0.0);   // v9 (L) the night of the stage while the song plays
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
