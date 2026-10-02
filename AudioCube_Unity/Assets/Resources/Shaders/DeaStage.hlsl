#ifndef DEACUBE_STAGE_INCLUDED
#define DEACUBE_STAGE_INCLUDED
// v9 (L) THE STAGE LIGHTS (StageLights.cs writes these globals; all zero = off: every surface draws exactly as before).
// While the song plays the stage dims (_DeaStageDim 0..1: darker, a little less saturated, cooler; the glows lower) and up to four
// pools of light lie on the lead grids that sound (_DeaSpotPos xyz = the pool's centre, w = its light 0..1; _DeaSpotBox xy = its half
// extents in x / z, zw = the lowest / highest world y it lights). Inside a pool the surface keeps its full colour, a touch warmer.
float _DeaStageDim;
float4 _DeaSpotPos[4];
float4 _DeaSpotBox[4];

// the light of the pools at world point p: 0 none .. 1 inside a full pool. A pool is the grid's footprint as a rounded rectangle
// (a superellipse, n = 4): a bright core and a softer rim band, flat bands as everything else (toon), anti-aliased.
float DeaPool(float3 p)
{
    float s = 0.0;
    [unroll] for (int k = 0; k < 4; k++)
    {
        float4 a = _DeaSpotPos[k];
        float4 b = _DeaSpotBox[k];
        float2 d = abs(p.xz - a.xz) / max(b.xy, 0.01);
        float2 d2 = d * d;
        float r = sqrt(sqrt(d2.x * d2.x + d2.y * d2.y));
        float aa = max(fwidth(r), 1e-3);
        float core = 1.0 - smoothstep(0.88 - aa, 0.88 + aa, r);
        float rim = 1.0 - smoothstep(1.0 - aa, 1.0 + aa, r);
        float vert = step(b.z, p.y) * step(p.y, b.w);
        s = max(s, a.w * max(core, rim * 0.6) * vert);
    }
    return s;
}

// the grade of a finished colour (fog included): `dim` = how far this pixel sits back (the stage's dim outside the pools), `lit` = how
// much a pool lights it. dim 0 and lit 0 leave the colour exactly as it was.
half3 DeaStageGrade(half3 col, float dim, float lit)
{
    half l = dot(col, half3(0.2126, 0.7152, 0.0722));
    col = lerp(col, l.xxx, 0.32 * dim);                               // a little less saturated
    col *= lerp(half3(1.0, 1.0, 1.0), half3(0.50, 0.49, 0.62), dim);  // darker and cooler (linear: about 0.72 / 0.81 of the sRGB value)
    col *= lerp(half3(1.0, 1.0, 1.0), half3(1.12, 1.05, 0.90), lit);  // a pool: warm stage light
    return col;
}
#endif
