Shader "DeaCube/Toon"
{
    // SPEC v3 §7 (package E): lit objects that read as drawn (Spider-Verse ink + Ben-Day print, Melatonin pastel flats).
    // Property names follow URP Lit (_BaseMap, _BaseColor, _EmissionColor + _EMISSION, _Smoothness, _Metallic; _Color is an
    // unused legacy slot, as in URP Lit) so every SetColor / MaterialPropertyBlock call in the project keeps working.
    //
    // UniversalForward: matte. The main light is quantised into three flat bands (light / mid / shade) on face-snapped normals,
    // so every face of a box is one flat value; received shadows are thresholded into the shade band. The shade tone is the base
    // colour hue-shifted toward violet, more saturated and darker (never grey). The light drives the print layers: Ben-Day dots
    // grow "thick and thin" with the light (small dots in the mid band, large ones in the shade band), cast shadows are hatched in
    // one direction (cross-hatched only where the face is half lit anyway), sparse light dots paint the highlight and the rim, and
    // a thin warm-red fringe marks the sunlit silhouette. The dot / hatch plates print slightly off register (cyan and magenta
    // fringes), further off and coarser for far objects (the depth of field of a printed page). _Smoothness only sizes the small
    // painted highlight and is clamped by _SpecMax: no reflections, no Fresnel gloss. Emission is added flat. Fog is flat per
    // object (every pixel of an object takes its pivot's fog: far islands become flat cut-out layers).
    // SRPDefaultUnlit: inverted-hull ink outline ~2.5 px at 1080p at any distance, heavier on the shadow side, boiling (its
    // width noise is re-seeded 10 times per second like a redrawn line), slightly off register for far objects.
    // ShadowCaster / DepthOnly / DepthNormals: URP's own passes. SRP-Batcher compatible: one UnityPerMaterial CBUFFER.
    // Global off-switches (0 = on, set by Look): _DeaInkOff, _DeaPrintOff, _DeaBoilOff, _DeaMisregOff.
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        [HDR] _EmissionColor ("Emission", Color) = (0, 0, 0, 1)
        _Smoothness ("Smoothness (painted highlight size)", Range(0, 1)) = 0.4
        _Metallic ("Metallic (highlight takes the base colour)", Range(0, 1)) = 0
        _Cutoff ("Alpha cutoff (unused)", Range(0, 1)) = 0.5
        [HideInInspector] _Color ("Legacy colour (unused, as in URP Lit)", Color) = (1, 1, 1, 1)

        _FaceSnap ("Flat faces (snap normals to the box axes)", Range(0, 1)) = 1
        [Header(Ink)]
        _OutlineWidth ("Outline width (px at 1080p)", Float) = 2.5
        _OutlineColor ("Ink", Color) = (0.1, 0.06, 0.17, 1)
        _OutlineTint ("Ink takes the base colour", Range(0, 1)) = 0.18
        _FlatFog ("Flat per-object fog", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" "IgnoreProjector" = "True" }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "DeaStage.hlsl"   // v9 (L) the stage lights (all zero = no change)

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _EmissionColor;
            half4 _Color;
            half _Smoothness;
            half _Metallic;
            half _Cutoff;
            half _FaceSnap;
            half _OutlineWidth;
            half4 _OutlineColor;
            half _OutlineTint;
            half _FlatFog;
        CBUFFER_END

        // the look (shared by every Toon material; tuned here so every material follows)
        static const half _Band0 = 0.1;              // shade | mid threshold (N.L)
        static const half _Band1 = 0.55;             // mid | light threshold
        static const half _MidMix = 0.38;            // mid band toward the shade tone
        static const half _ShadeHue = 0.72;          // shade target hue (violet)
        static const half _ShadeShift = 0.07;        // max hue shift toward it
        static const half _ShadeSat = 1.3;
        static const half _ShadeVal = 0.64;
        static const half _LightTint = 0.3;          // how much of the key light's colour tints the lit band
        static const half _ShadowGate = 0.5;         // received shadow threshold
        static const half _SpecMax = 0.38;           // matte: the painted highlight never grows past this smoothness
        static const half _SpecStrength = 0.8;
        static const half4 _RimColor = half4(1.0, 0.93, 0.97, 0.4);
        static const half _RimWidth = 0.14;
        static const half4 _FringeColor = half4(1.0, 0.36, 0.42, 0.35);
        static const half _HalftoneCell = 9.0;       // px at 1080p
        static const half _HalftoneStrength = 0.6;
        static const half _HatchStrength = 0.6;
        static const half _Misreg = 1.0;             // px at 1080p
        static const half _Boil = 1.0;

        // global switches (0 = on); Look.PushGlobals writes them
        float _DeaInkOff;
        float _DeaPrintOff;
        float _DeaBoilOff;
        float _DeaMisregOff;

        // pixels per pixel-at-1080p
        float DeaPxScale() { return max(_ScreenParams.y, 1.0) / 1080.0; }

        // 0 near .. 1 far: far objects print coarser and further off register
        float DeaFar01(float3 positionWS) { return saturate((distance(GetCameraPositionWS(), positionWS) - 26.0) / 60.0); }

        // fog of the object's pivot (flat per object) blended with the fog of this vertex
        float DeaFlatFog(float4 positionCS)
        {
            float3 pivotWS = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
            float perObject = ComputeFogFactor(TransformWorldToHClip(pivotWS).z);
            float perVertex = ComputeFogFactor(positionCS.z);
            return lerp(perVertex, perObject, _FlatFog);
        }
        ENDHLSL

        // ------------------------------------------------------------------------------------------ lit (matte cel + print)
        Pass
        {
            Name "ToonForward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ToonVert
            #pragma fragment ToonFrag
            #pragma shader_feature_local_fragment _EMISSION
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            struct ToonAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ToonVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 normalOS : TEXCOORD3;
                float fogFactor : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            ToonVaryings ToonVert(ToonAttributes v)
            {
                ToonVaryings o = (ToonVaryings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.normalOS = v.normalOS;
                o.uv = TRANSFORM_TEX(v.texcoord, _BaseMap);
                o.fogFactor = DeaFlatFog(o.positionCS);
                return o;
            }

            // the dominant box axis of an object-space normal (every face of a rounded box becomes one flat value)
            float3 SnapAxis(float3 n)
            {
                float3 a = abs(n);
                if (a.x >= a.y && a.x >= a.z) return float3(n.x >= 0.0 ? 1.0 : -1.0, 0.0, 0.0);
                if (a.y >= a.z) return float3(0.0, n.y >= 0.0 ? 1.0 : -1.0, 0.0);
                return float3(0.0, 0.0, n.z >= 0.0 ? 1.0 : -1.0);
            }

            // shade tone: hue pulled toward violet (shortest way round), more saturated, darker; a greyish base takes the violet itself
            half3 ShadeTone(half3 linearColor)
            {
                half3 c = LinearToSRGB(saturate(linearColor));
                half3 hsv = RgbToHsv(c);
                half dh = _ShadeHue - hsv.x;
                dh -= round(dh);
                half3 shifted = half3(frac(hsv.x + clamp(dh, -_ShadeShift, _ShadeShift) + 1.0), saturate(hsv.y * _ShadeSat), hsv.z * _ShadeVal);
                half3 violet = half3(_ShadeHue, 0.24, hsv.z * _ShadeVal);
                half3 rgb = lerp(HsvToRgb(violet), HsvToRgb(shifted), saturate(hsv.y * 4.0));
                return SRGBToLinear(rgb);
            }

            // Ben-Day dot coverage on a 45-degree grid: cell in px, radius as a fraction of the cell, ~1 px anti-aliasing
            float DotCover(float2 px, float cell, float radius)
            {
                float2 q = float2(px.x - px.y, px.x + px.y) * (0.70710678 / cell);
                float d = length(frac(q) - 0.5);
                float aa = 0.8 / cell;
                return (1.0 - smoothstep(radius - aa, radius + aa, d)) * step(0.02, radius);
            }

            // parallel hatch lines across direction `perp`, spacing in px, line thickness as a fraction of the spacing
            float HatchCover(float2 px, float2 perp, float spacing, float width)
            {
                float d = abs(frac(dot(px, perp) / spacing) - 0.5);
                float aa = 0.8 / spacing;
                return 1.0 - smoothstep(width * 0.5 - aa, width * 0.5 + aa, 0.5 - d);
            }

            // two printing plates slightly off register: where both print = the ink; a lone plate leaves a cyan / magenta fringe
            half3 Plates(half3 col, half3 ink, float a, float b, float amount)
            {
                float both = a * b;
                col = lerp(col, ink, both * amount);
                col = lerp(col, lerp(col, half3(0.18, 0.62, 0.86), 0.5), (a - both) * amount * 0.35);
                col = lerp(col, lerp(col, half3(0.9, 0.26, 0.58), 0.5), (b - both) * amount * 0.35);
                return col;
            }

            half4 ToonFrag(ToonVaryings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                half3 base = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;

                float3 nSmooth = normalize(i.normalWS);
                float3 nFace = TransformObjectToWorldNormal(SnapAxis(i.normalOS));
                float3 N = normalize(lerp(nSmooth, nFace, _FaceSnap));
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);

                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float3 L = light.direction;
                float ndl = dot(N, L);
                float ndlS = dot(nSmooth, L);
                float sun = smoothstep(_ShadowGate - 0.07, _ShadowGate + 0.07, light.shadowAttenuation);   // 1 = not in a cast shadow
                float facing = step(0.0, ndl);
                float cast = (1.0 - sun) * facing;                     // a shadow falling on a face that looks at the light
                float lv = lerp(min(ndl, _Band0 - 0.3), ndl, sun);   // cast shadows join the shade band

                float aaB = max(fwidth(lv), 1e-4);
                float toMid = smoothstep(_Band0 - aaB, _Band0 + aaB, lv);
                float toLit = smoothstep(_Band1 - aaB, _Band1 + aaB, lv);

                half3 lc = light.color;
                half lmax = max(max(lc.r, lc.g), max(lc.b, 1e-3));
                half3 cLit = base * lerp(half3(1.0, 1.0, 1.0), lc / lmax, _LightTint);
                half3 cShade = ShadeTone(base);
                half3 cMid = lerp(cLit, cShade, _MidMix);
                half3 col = lerp(cShade, lerp(cMid, cLit, toLit), toMid);

                // ---- print layers (screen space, 45-degree screen, off register)
                float2 px = i.positionCS.xy;
                float s = DeaPxScale();
                float far01 = DeaFar01(i.positionWS);
                float print = 1.0 - _DeaPrintOff;
                float cell = _HalftoneCell * s * (1.0 + 0.7 * far01);
                float mis = _Misreg * s * (1.0 + 1.6 * far01) * (1.0 - _DeaMisregOff);
                float2 offC = float2(0.8, 0.5) * mis;
                float2 offM = float2(-0.5, -0.8) * mis;

                // Ben-Day form shading: dots in the mid and shade bands, thick and thin with the light (not inside cast shadows)
                // (the mid band stays a flat colour: Melatonin-clean faces; the dots belong to the shade)
                float dark = saturate((_Band0 - lv) / 0.6);
                float r = lerp(0.3, 0.42, dark);
                float dotsOn = (1.0 - toMid) * (1.0 - cast) * _HalftoneStrength * print;
                half3 dotInk = cShade * 0.66;
                col = Plates(col, dotInk, DotCover(px + offC, cell, r), DotCover(px + offM, cell, r), dotsOn);

                // hatching for cast shadows: one direction, heavier where darker; cross-hatch only where the face is half lit anyway
                float spacing = 5.5 * s * (1.0 + 0.5 * far01);
                float hw = lerp(0.3, 0.46, 1.0 - sun);
                float2 perpA = float2(-0.8829, 0.4695);
                float hatchOn = cast * _HatchStrength * print;
                half3 hatchInk = cShade * 0.58;
                col = Plates(col, hatchInk, HatchCover(px + offC, perpA, spacing, hw), HatchCover(px + offM, perpA, spacing, hw), hatchOn);
                float deep = cast * (1.0 - smoothstep(_Band1 - 0.05, _Band1 + 0.05, ndl));
                float2 perpB = float2(0.4695, 0.8829);
                col = Plates(col, hatchInk, HatchCover(px + offC, perpB, spacing * 1.15, hw * 0.8), HatchCover(px + offM, perpB, spacing * 1.15, hw * 0.8), deep * _HatchStrength * print);

                // painted highlight: a small cluster of light dots where the (smooth) surface faces the half vector
                float3 H = normalize(L + V);
                float gloss = min(_Smoothness, _SpecMax);
                float spec = pow(saturate(dot(nSmooth, H)), exp2(1.0 + 9.0 * gloss)) * sun * step(0.0, ndlS);
                float hiZone = smoothstep(0.72, 0.86, spec) * _SpecStrength * smoothstep(0.5, 0.6, _Smoothness) * toLit;   // only glossy things (cubes), a small cluster
                half3 hiCol = lerp(half3(1.0, 0.98, 0.95), saturate(cLit * 1.6), _Metallic * 0.7);
                col = lerp(col, hiCol, DotCover(px, cell * 0.85, lerp(0.16, 0.36, hiZone)) * hiZone * print);

                // rim light as sparse light dots on the sunlit silhouette + a thin warm-red fringe right at the edge
                float fres = 1.0 - saturate(dot(nSmooth, V));
                float sunSide = saturate(ndlS * 3.0) * sun;
                float rimZone = smoothstep(1.0 - _RimWidth, 1.0 - _RimWidth + 0.07, fres) * sunSide;
                col = lerp(col, _RimColor.rgb, DotCover(px, cell * 0.8, 0.3) * rimZone * _RimColor.a * print);
                float fringe = smoothstep(0.92, 0.95, fres) * sunSide;
                col = lerp(col, _FringeColor.rgb, fringe * _FringeColor.a * print);

                // v9 (L) the stage lights: outside the lead grids' pools the stage sits back while the song plays (glow lower, darker, cooler)
                float stagePool = DeaPool(i.positionWS);
                float stageDim = _DeaStageDim * (1.0 - stagePool);

                #if defined(_EMISSION)
                col += _EmissionColor.rgb * (1.0 - 0.55 * stageDim);
                #endif

                col = MixFog(col, i.fogFactor);
                col = DeaStageGrade(col, stageDim, stagePool * _DeaStageDim);
                return half4(col, 1.0);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------------------------------ ink outline (inverted hull)
        Pass
        {
            Name "InkOutline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex InkVert
            #pragma fragment InkFrag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            struct InkAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct InkVaryings
            {
                float4 positionCS : SV_POSITION;
                float fogFactor : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float InkHash(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float InkNoise(float3 p)
            {
                float3 c = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = lerp(InkHash(c), InkHash(c + float3(1, 0, 0)), f.x);
                float b = lerp(InkHash(c + float3(0, 1, 0)), InkHash(c + float3(1, 1, 0)), f.x);
                float d = lerp(InkHash(c + float3(0, 0, 1)), InkHash(c + float3(1, 0, 1)), f.x);
                float e = lerp(InkHash(c + float3(0, 1, 1)), InkHash(c + float3(1, 1, 1)), f.x);
                return lerp(lerp(a, b, f.y), lerp(d, e, f.y), f.z);
            }

            InkVaryings InkVert(InkAttributes v)
            {
                InkVaryings o = (InkVaryings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(v.normalOS);
                float4 posCS = TransformWorldToHClip(posWS);
                float2 nCS = mul((float3x3)GetWorldToHClipMatrix(), nWS).xy * _ScreenParams.xy;
                float len = length(nCS);
                float2 dir = len > 1e-6 ? nCS / len : float2(0.0, 0.0);

                float s = DeaPxScale();
                float far01 = DeaFar01(posWS);
                float shadowSide = saturate(0.5 - 0.5 * dot(nWS, _MainLightPosition.xyz));        // 0 sunlit side .. 1 shadow side
                float weight = lerp(0.72, 1.38, shadowSide);
                float seed = floor(_Time.y * 10.0);                                                  // re-drawn 10 times per second
                float boil = 1.0 + (InkNoise(v.positionOS.xyz * 2.2 + seed * 7.31) - 0.5) * 0.5 * _Boil * (1.0 - _DeaBoilOff);
                float widthPx = _OutlineWidth * s * weight * boil * lerp(1.0, 0.72, far01) * (1.0 - _DeaInkOff);
                float2 offPx = dir * widthPx + float2(0.9, -0.6) * far01 * 1.6 * s * (1.0 - _DeaMisregOff);
                posCS.xy += offPx * (2.0 / _ScreenParams.xy) * posCS.w;
                // ink off: collapse the hull outside the clip volume instead of z-fighting the surface
                o.positionCS = widthPx > 0.05 ? posCS : float4(2.0, 2.0, 2.0, 1.0);
                o.fogFactor = DeaFlatFog(posCS);
                return o;
            }

            half4 InkFrag(InkVaryings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half3 ink = lerp(_OutlineColor.rgb, _BaseColor.rgb * 0.22, _OutlineTint);
                return half4(DeaStageGrade(MixFog(ink, i.fogFactor), _DeaStageDim, 0.0), 1.0);   // v9 (L)
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------------------------------ URP utility passes
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"
            ENDHLSL
        }
    }
    FallBack Off
}
