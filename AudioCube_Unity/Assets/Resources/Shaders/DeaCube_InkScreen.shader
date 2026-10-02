Shader "DeaCube/InkScreen"
{
    // SPEC v3 §7 (package E): the page the frame is printed on. A camera-aligned quad (LookScreen draws one per base camera, uv =
    // viewport) blended as a 2x multiply (grey 0.5 = no change): a faint paper-fibre texture sampled 1:1 in pixels whose offset
    // jumps ~8 times per second, plus the impact frames (1-2 drawn frames on downbeats / accents): mode 0 = ink speed lines
    // radiating from _ImpactCenter toward the frame edges, mode 1 = a ring of Ben-Day dots around it. Never brightens.
    Properties
    {
        _PaperTex ("Paper fibres", 2D) = "gray" {}
        _Paper ("Paper strength", Range(0, 1)) = 0.3
        _PaperOffset ("Paper offset (px)", Vector) = (0, 0, 0, 0)
        _Impact ("Impact", Range(0, 1)) = 0
        _ImpactMode ("Impact mode", Float) = 0
        _ImpactCenter ("Impact centre (viewport)", Vector) = (0.5, 0.5, 0, 0)
        _ImpactSeed ("Impact seed", Float) = 0
        _Ink ("Ink (as a multiply colour)", Color) = (0.3, 0.2, 0.45, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend DstColor SrcColor
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "InkScreen"
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ScreenVert
            #pragma fragment ScreenFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_PaperTex);
            SAMPLER(sampler_PaperTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _PaperTex_ST;
                half _Paper;
                float4 _PaperOffset;
                half _Impact;
                float _ImpactMode;
                float4 _ImpactCenter;
                float _ImpactSeed;
                half4 _Ink;
            CBUFFER_END

            struct ScreenAttributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct ScreenVaryings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            ScreenVaryings ScreenVert(ScreenAttributes v)
            {
                ScreenVaryings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            float Hash11(float p) { p = frac(p * 0.1031); p *= p + 33.33; p *= p + p; return frac(p); }

            half4 ScreenFrag(ScreenVaryings i) : SV_Target
            {
                float2 res = max(_ScreenParams.xy, float2(1.0, 1.0));
                float2 pix = i.uv * res;
                half paper = SAMPLE_TEXTURE2D(_PaperTex, sampler_PaperTex, (pix + _PaperOffset.xy) / 256.0).r;
                half3 v = 0.5 + (paper - 0.5) * _Paper;

                if (_Impact > 0.001)
                {
                    float2 d = (i.uv - _ImpactCenter.xy) * float2(res.x / res.y, 1.0);
                    float r = length(d);
                    float mark = 0.0;
                    if (_ImpactMode < 0.5)
                    {
                        // speed lines: thin wedges from the centre, only toward the frame edges
                        float a = atan2(d.y, d.x) / 6.2831853 + 0.5;
                        float sectors = 96.0;
                        float k = floor(a * sectors);
                        float h = Hash11(k + _ImpactSeed * 13.0);
                        float w = 0.12 + 0.3 * h;
                        float f = abs(frac(a * sectors) - 0.5) * 2.0;
                        float aa = max(fwidth(a * sectors) * 1.5, 1e-4);
                        float wedge = 1.0 - smoothstep(w - aa, w + aa, f);
                        float reach = lerp(0.36, 0.56, Hash11(k * 1.7 + _ImpactSeed));
                        mark = wedge * smoothstep(reach, reach + 0.12, r) * step(0.35, h);
                    }
                    else
                    {
                        // a ring of halftone dots around the accent
                        float cell = 9.0 * res.y / 1080.0;
                        float2 q = float2(pix.x - pix.y, pix.x + pix.y) * (0.70710678 / cell);
                        float dd = length(frac(q) - 0.5);
                        float ring = smoothstep(0.05, 0.14, r) * (1.0 - smoothstep(0.2, 0.3, r));
                        float rad = 0.42 * ring;
                        mark = (1.0 - smoothstep(rad - 0.8 / cell, rad + 0.8 / cell, dd)) * step(0.02, rad);
                    }
                    v = lerp(v, _Ink.rgb * 0.5, mark * _Impact);
                }
                return half4(v, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
