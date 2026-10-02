Shader "DeaCube/PresentBackdrop"
{
    // SPEC v3 §4 + §7 (package C): the waterfall's night sky, drawn first as a full-screen clip-space quad (no depth). Flat
    // posterized bands (deep indigo at the top to violet where the cascade pours), halftone dot transitions between the
    // bands (screen-space, print-like), and one flat pale moon disc with an ink rim and a halftone crescent shade. A chord
    // change tints the bands toward the new island's chord colour: the tint rises from the bottom behind an inked stripe and a
    // band of halftone dots (the sweep), the previous chord's tint stays above the front until it has passed. SPEC v4 §7: faint
    // broken strata (a pale ink line with halftone dots thinning under it) scroll up as the camera sinks (_Scroll, in screen
    // heights, parallax applied by the stage), so the descent reads even where no cube is falling. SPEC v5 §8: the camera rolls a
    // few degrees on big strikes; the sky tilts with it (_Roll, radians: the lookup turns about the screen centre, aspect-true).
    Properties
    {
        _C0 ("Band 0 (bottom)", Color) = (0.20, 0.09, 0.30, 1)
        _C1 ("Band 1", Color) = (0.13, 0.075, 0.24, 1)
        _C2 ("Band 2", Color) = (0.085, 0.06, 0.17, 1)
        _C3 ("Band 3 (top)", Color) = (0.05, 0.04, 0.11, 1)
        _MoonCol ("Moon", Color) = (0.62, 0.53, 0.86, 1)
        _MoonShade ("Moon shade", Color) = (0.42, 0.33, 0.66, 1)
        _Ink ("Ink", Color) = (0.05, 0.03, 0.09, 1)
        _Moon ("Moon centre (xy) radius (z) opacity (w)", Vector) = (0.17, 0.8, 0.1, 1)
        _TintOld ("Previous chord", Color) = (1, 1, 1, 1)
        _TintNew ("Chord", Color) = (1, 1, 1, 1)
        _Sweep ("Sweep front (x) width (y) strength (z) tint (w)", Vector) = (3, 0.2, 0, 0)
        _Tilt ("Band tilt", Float) = 0.18
        _Dot ("Halftone cell (px)", Float) = 9
        _Scroll ("Descent (screen heights)", Float) = 0
        _Strata ("Strata per screen", Float) = 1.7
        _StrataCol ("Strata", Color) = (0.36, 0.22, 0.52, 1)
        _Roll ("Camera roll (rad)", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "IgnoreProjector"="True" "PreviewType"="Plane" }
        ZWrite Off
        ZTest Always
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _C0, _C1, _C2, _C3, _MoonCol, _MoonShade, _Ink, _Moon, _TintOld, _TintNew, _Sweep;
            float _Tilt, _Dot, _Scroll, _Strata, _Roll;
            float4 _StrataCol;
            float Hash1(float n) { return frac(sin(n * 127.1 + 311.7) * 43758.5453); }
            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert (appdata v)
            {
                v2f o;
                o.pos = float4(v.vertex.x, v.vertex.y * _ProjectionParams.x, 0.5, 1.0);
                o.uv = v.vertex.xy * 0.5 + 0.5;
                return o;
            }
            float3 Band(float idx)
            {
                if (idx < 0.5) return _C0.rgb;
                if (idx < 1.5) return _C1.rgb;
                if (idx < 2.5) return _C2.rgb;
                return _C3.rgb;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                float2 uv = i.uv;
                {
                    // the view's roll: the world turns on screen, so does the sky
                    float cr = cos(_Roll), sr = sin(_Roll);
                    float2 q = (uv - 0.5) * float2(aspect, 1.0);
                    q = float2(cr * q.x - sr * q.y, sr * q.x + cr * q.y);
                    uv = q / float2(aspect, 1.0) + 0.5;
                }
                float t = saturate(uv.y + (uv.x - 0.5) * _Tilt);
                float x = t * 4.0;
                float idx = min(floor(x), 3.0);
                float f = x - idx;
                // halftone transition into the band above over the last third of each band
                float2 cell = frac(i.pos.xy / max(_Dot, 2.0)) - 0.5;
                float edge = saturate((f - 0.66) / 0.34);
                float dotR = 0.62 * sqrt(edge);
                float up = (idx < 3.0 && length(cell) < dotR) ? 1.0 : 0.0;
                float3 c = Band(idx + up);
                // chord tint (multiplicative: the bands stay posterized): the new chord below the sweep front, the previous above
                float3 tint = (t < _Sweep.x) ? _TintNew.rgb : _TintOld.rgb;
                c = lerp(c, c * (0.4 + 1.6 * tint), _Sweep.w);
                // the sweep: an ink edge, a solid stripe of the new chord, then halftone dots thinning out below it
                float sd = _Sweep.x - t;
                if (_Sweep.z > 0.001 && sd >= 0.0 && sd < _Sweep.y)
                {
                    if (sd < 0.005) c = lerp(c, _Ink.rgb, _Sweep.z);
                    else if (sd < 0.028) c = lerp(c, _TintNew.rgb, 0.62 * _Sweep.z);
                    else
                    {
                        float k = (sd - 0.028) / max(_Sweep.y - 0.028, 1e-3);
                        float2 cell3 = frac(i.pos.xy / max(_Dot, 2.0)) - 0.5;
                        if (length(cell3) < 0.62 * sqrt(saturate(1.0 - k))) c = lerp(c, _TintNew.rgb, 0.42 * _Sweep.z);
                    }
                }
                // strata: broken, slightly tilted pale ink lines scrolling up with the descent, halftone dots thinning below each
                {
                    float sy = (uv.y - _Scroll) * _Strata + (uv.x - 0.5) * 0.22;
                    float row = floor(sy);
                    float f2 = frac(sy);
                    float seg = floor(uv.x * 3.0 + Hash1(row) * 3.0);
                    float on = step(0.42, Hash1(row * 7.13 + seg * 1.7));
                    float2 cell4 = frac(i.pos.xy / max(_Dot * 0.8, 2.0)) - 0.5;
                    float strata = 0.0;
                    if (f2 > 0.975) strata = 0.34;
                    else if (f2 > 0.84 && length(cell4) < 0.45 * sqrt(saturate((f2 - 0.84) / 0.135))) strata = 0.2;
                    c = lerp(c, _StrataCol.rgb, strata * on * saturate(1.15 - uv.y * 0.55));
                }
                // flat moon: ink rim, pale face, halftone crescent shade toward the lower left
                float2 d = (uv - _Moon.xy) * float2(aspect, 1.0);
                float r = length(d) / max(_Moon.z, 1e-3);
                if (r < 1.0)
                {
                    float3 m = _MoonCol.rgb;
                    float2 sd = (d / max(_Moon.z, 1e-3)) - float2(0.38, 0.3);
                    float shade = saturate((length(sd) - 0.95) / 0.4);
                    float2 cell2 = frac(i.pos.xy / 6.0) - 0.5;
                    if (length(cell2) < 0.55 * sqrt(shade)) m = _MoonShade.rgb;
                    if (r > 0.93) m = _Ink.rgb;
                    c = lerp(c, m, _Moon.w);
                }
                return fixed4(c, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
