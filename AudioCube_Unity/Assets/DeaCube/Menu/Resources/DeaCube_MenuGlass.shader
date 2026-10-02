Shader "DeaCube/MenuGlass"
{
    // The big translucent cubes floating in front of the menu wall (SPEC v3 §5.1, comic look of §7): flat translucent colour in
    // two cel tones (lit / shade faces), a crisp dark ink rim on every edge with a thin light inner rim, and a diagonal comic
    // glint on the front faces. Back faces are drawn first (dimmer), then the front faces; alpha blended, no depth write.
    // Object space is a unit cube (half extent 0.5).
    // v4 (user request: "liquid pixellated like goose/cookingsim"): _Pixel 1 = drawn by MenuStage into a low-res buffer (1/4 of the
    // screen, nearest-upscaled) — the flat lit/shade split becomes continuous shading quantised into _Bands cel bands whose
    // transitions are stippled with a 4x4 cluster-dot matrix on the buffer's own pixel grid (round dots), so as the cube turns its
    // bands and edges crawl through the fixed dot grid. Alpha blends with a separate coverage term (One OneMinusSrcAlpha) so the
    // buffer holds premultiplied colour and a true coverage alpha for the composite.
    Properties
    {
        _ColA ("Lit tone", Color) = (1, 0.45, 0.8, 1)
        _ColB ("Shade tone", Color) = (0.86, 0.24, 0.7, 1)
        _Ink ("Ink", Color) = (0.07, 0.045, 0.12, 1)
        _Fill ("Fill alpha", Float) = 0.5
        _InkWidth ("Ink width", Float) = 0.055
        _Fade ("Fade", Float) = 1
        _Pixel ("Pixel mode (low-res buffer)", Float) = 0
        _Bands ("Cel bands in pixel mode", Float) = 3
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _ColA, _ColB, _Ink;
            float _Fill, _InkWidth, _Fade, _Pixel, _Bands;
        CBUFFER_END
        // 4x4 cluster-dot threshold matrix: dithered bands grow round dots, not a Bayer crosshatch
        static const float kClusterDot[16] = { 12, 5, 6, 13, 4, 0, 1, 7, 11, 3, 2, 8, 15, 10, 9, 14 };
        float ClusterDot(float2 pixel)
        {
            uint x = (uint)fmod(floor(pixel.x), 4.0), y = (uint)fmod(floor(pixel.y), 4.0);
            return (kClusterDot[y * 4 + x] + 0.5) / 16.0;
        }
        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
        struct Varyings { float4 positionCS : SV_POSITION; float3 posOS : TEXCOORD0; float3 normalWS : TEXCOORD1; };
        Varyings vert (Attributes v)
        {
            Varyings o;
            o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
            o.normalWS = TransformObjectToWorldNormal(v.normalOS);
            o.posOS = v.positionOS.xyz;
            return o;
        }
        half4 shade (Varyings i, float faceMul)
        {
            float3 a = saturate(abs(i.posOS) * 2.0);
            float mx = max(a.x, max(a.y, a.z));
            float mn = min(a.x, min(a.y, a.z));
            float mid = a.x + a.y + a.z - mx - mn;
            float e = 1.0 - mid;                                           // distance to the nearest edge along the face (0 at the edge)
            float3 N = normalize(i.normalWS);
            float key = dot(N, normalize(float3(-0.45, 0.75, -0.5)));
            float3 fill = key > 0.25 ? _ColA.rgb : _ColB.rgb;               // two flat cel tones
            if (_Pixel > 0.5)
            {
                // continuous light (the key plus a soft sweep across every face) quantised into bands, the band edges stippled
                float s = saturate(0.5 + 0.7 * key + 0.34 * (i.posOS.y - 0.5 * i.posOS.x));
                float b = max(1.0, _Bands - 1.0);
                float q = saturate(floor(s * b + ClusterDot(i.positionCS.xy)) / b);
                fill = lerp(_ColB.rgb, _ColA.rgb, q);
            }
            float alpha = _Fill * faceMul;
            float3 col = fill;
            // comic glint: a diagonal stripe on the lit front faces
            float g = frac((i.posOS.x + i.posOS.y) * 1.2 + 0.35);
            if (faceMul > 0.9 && key > 0.25 && g > 0.08 && g < 0.2) { col = lerp(col, float3(1, 1, 1), 0.55); alpha = max(alpha, 0.7); }
            // thin light inner rim, then the ink rim on the edge
            if (e < _InkWidth * 2.1) { col = lerp(fill, float3(1, 1, 1), 0.6); alpha = max(alpha, 0.85 * faceMul); }
            if (e < _InkWidth) { col = _Ink.rgb; alpha = 0.95 * (faceMul > 0.9 ? 1.0 : 0.7); }
            return half4(col, alpha * _Fade);
        }
        ENDHLSL

        Pass
        {
            Name "Back"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragBack
            half4 fragBack (Varyings i) : SV_Target { return shade(i, 0.45); }
            ENDHLSL
        }
        Pass
        {
            Name "Front"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragFront
            half4 fragFront (Varyings i) : SV_Target { return shade(i, 1.0); }
            ENDHLSL
        }
    }
    FallBack Off
}
