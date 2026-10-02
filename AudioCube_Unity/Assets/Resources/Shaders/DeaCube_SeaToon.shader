Shader "DeaCube/SeaToon"
{
    // SPEC v3 §7 (package E): the stylised sea under the islands (Fx.BuildSea, y = -4). Opaque and flat: two pastel tones from a
    // slow, wide swell field (stretched along x so it reads as waves), light contour lines drawn at a few iso-levels of that
    // field (they scroll on twos and fade out before they could crowd into mush), the islands' cast shadows as flat violet
    // shapes with one-direction hatching, and toward the horizon a Ben-Day halftone ramp into _Far (the sky's below-horizon
    // colour) instead of a smooth fog. _Tint (the lit island's colour) leans the whole sea a little toward the song.
    Properties
    {
        _Deep ("Sea", Color) = (0.29, 0.25, 0.53, 1)
        _Swell ("Swell", Color) = (0.34, 0.29, 0.6, 1)
        _Line ("Wave lines", Color) = (0.58, 0.5, 0.86, 1)
        _Far ("Horizon", Color) = (0.69, 0.54, 0.78, 1)
        _ShadowCol ("Island shadows", Color) = (0.2, 0.15, 0.4, 1)
        _Tint ("Tint", Color) = (0.5, 0.4, 0.7, 1)
        _TintAmt ("Tint amount", Range(0, 1)) = 0.12
        _Scale ("Swell scale", Float) = 0.055
        _Drift ("Drift speed", Float) = 0.035
        _Levels ("Contour levels", Float) = 3
        _LineFade ("Wave lines fade (start, end)", Vector) = (30, 62, 0, 0)
        _FarFade ("Halftone ramp (start, end)", Vector) = (60, 170, 0, 0)
        _Cell ("Halftone cell (px at 1080p)", Float) = 8
    }

    SubShader
    {
        Tags { "Queue" = "Geometry-10" "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite On

        Pass
        {
            Name "SeaToon"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex SeaVert
            #pragma fragment SeaFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "DeaStage.hlsl"   // v9 (L) the stage lights (all zero = no change)

            CBUFFER_START(UnityPerMaterial)
                half4 _Deep, _Swell, _Line, _Far, _ShadowCol, _Tint;
                half _TintAmt;
                float _Scale, _Drift, _Levels;
                float4 _LineFade, _FarFade;
                float _Cell;
            CBUFFER_END

            float _DeaPrintOff;

            struct SeaAttributes { float4 positionOS : POSITION; };
            struct SeaVaryings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            SeaVaryings SeaVert(SeaAttributes v)
            {
                SeaVaryings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            float Hash21(float2 p)
            {
                float3 q = frac(float3(p.xyx) * 0.1031);
                q += dot(q, q.yzx + 33.33);
                return frac((q.x + q.y) * q.z);
            }

            float Noise(float2 p)
            {
                float2 c = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash21(c), b = Hash21(c + float2(1, 0)), d = Hash21(c + float2(0, 1)), e = Hash21(c + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(d, e, f.x), f.y);
            }

            float DotCover(float2 px, float cell, float radius)
            {
                float2 q = float2(px.x - px.y, px.x + px.y) * (0.70710678 / cell);
                float d = length(frac(q) - 0.5);
                float aa = 0.8 / cell;
                return (1.0 - smoothstep(radius - aa, radius + aa, d)) * step(0.02, radius);
            }

            half4 SeaFrag(SeaVaryings i) : SV_Target
            {
                float t12 = floor(_Time.y * 12.0) / 12.0;   // the water moves on twos
                float2 p = i.positionWS.xz * float2(0.45, 1.35) * _Scale;
                float2 drift = float2(t12 * _Drift, t12 * _Drift * 0.37);
                float n = Noise(p + drift) * 0.68 + Noise(p * 2.03 - drift * 1.7 + 17.3) * 0.32;

                half3 col = lerp(_Deep.rgb, _Swell.rgb, step(0.52, n));

                float dist = distance(GetCameraPositionWS(), i.positionWS);
                // contour lines at iso-levels of the swell field, a constant ~1.5 px wide, gone before they would crowd
                float lv = n * _Levels;
                float dIso = abs(frac(lv + 0.5) - 0.5) / _Levels;
                float fw = max(fwidth(n), 1e-5);
                float waveLine = 1.0 - smoothstep(fw * 0.55, fw * 1.25, dIso);
                float lineFade = 1.0 - smoothstep(_LineFade.x, _LineFade.y, dist);
                col = lerp(col, _Line.rgb, waveLine * lineFade * 0.8);

                // the islands' shadows: flat violet shapes, hatched in one direction
                float sh = MainLightRealtimeShadow(TransformWorldToShadowCoord(i.positionWS));
                float inShadow = 1.0 - smoothstep(0.42, 0.58, sh);
                col = lerp(col, _ShadowCol.rgb, inShadow * 0.5);
                float s = max(_ScreenParams.y, 1.0) / 1080.0;
                float2 px = i.positionCS.xy;
                float spacing = 6.0 * s;
                float hd = abs(frac(dot(px, float2(-0.8829, 0.4695)) / spacing) - 0.5);
                float hatch = 1.0 - smoothstep(0.17 - 0.8 / spacing, 0.17 + 0.8 / spacing, 0.5 - hd);
                col = lerp(col, _ShadowCol.rgb * 0.8, hatch * inShadow * 0.45 * (1.0 - _DeaPrintOff));

                col = lerp(col, lerp(col, _Tint.rgb, 0.5), _TintAmt);

                // toward the horizon: a halftone ramp into the far colour (dots grow until they merge)
                float f = saturate((dist - _FarFade.x) / max(_FarFade.y - _FarFade.x, 1.0));
                col = lerp(col, _Far.rgb, f * 0.55);                               // the haze itself, then dots of it growing until they merge
                float dots = DotCover(px, _Cell * s, pow(f, 1.4) * 0.72);
                col = lerp(col, _Far.rgb, max(dots * 0.8, smoothstep(0.88, 1.0, f)));
                // v9 (L) the stage lights: the sea sits back while the song plays; a pool of light on the water over a lead grid under it
                float stagePool = DeaPool(i.positionWS);
                col = DeaStageGrade(col, _DeaStageDim * (1.0 - stagePool), stagePool * _DeaStageDim);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
