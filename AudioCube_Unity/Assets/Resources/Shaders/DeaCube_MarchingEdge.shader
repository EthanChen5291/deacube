Shader "DeaCube/MarchingEdge"
{
    // SPEC v3 §2.1: the hover outline, drawn flat and cel-like (SPEC v3 §7): every edge of the wire box is a thin quad pushed
    // apart in the vertex shader perpendicular to the edge and the view direction (so the line faces the camera from any angle
    // and never thins below _MinPixels). Each line is a dark ink wire; bright flat dashes 0.09 u long march along it at 0.6 u/s
    // inside an ink border, and the corners are lit solid (bracket look). Hard edges (one-pixel anti-aliasing only), a flat
    // colour whose brightness pulses ±15 %. uv.x = 0/1 at the edge's start/end, uv.y = -1/+1 across it, TEXCOORD1 = the other endpoint.
    Properties
    {
        _Color ("Dash colour", Color) = (1,1,1,1)
        _Ink ("Ink colour", Color) = (0.07,0.04,0.13,1)
        _Fade ("Fade", Range(0,1)) = 1
        _Width ("Line width (world units)", Float) = 0.05
        _MinPixels ("Minimum width (pixels)", Float) = 3.4
        _DashLen ("Dash length (world units)", Float) = 0.09
        _Speed ("March speed (world units / s)", Float) = 0.6
        _Corner ("Corner bracket length (world units)", Float) = 0.12
        _InkShare ("Ink border share of the half width", Range(0,1)) = 0.36
        _GapAlpha ("Ink wire opacity between dashes", Range(0,1)) = 0.78
        _Pulse ("Pulse amount", Range(0,1)) = 0.15
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
    }
    SubShader
    {
        Tags { "Queue"="Transparent+20" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest [_ZTest]
        Cull Off
        Lighting Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color, _Ink; float _Fade, _Width, _MinPixels, _DashLen, _Speed, _Corner, _InkShare, _GapAlpha, _Pulse;
            struct appdata { float4 vertex : POSITION; float4 uv : TEXCOORD0; float4 other : TEXCOORD1; };
            struct v2f { float4 pos : SV_POSITION; float side : TEXCOORD0; float along : TEXCOORD1; float len : TEXCOORD2; };

            v2f vert (appdata v)
            {
                v2f o;
                float3 wp = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0)).xyz;
                float3 wo = mul(unity_ObjectToWorld, float4(v.other.xyz, 1.0)).xyz;
                float t = v.uv.x;                                   // 0 at the edge's start, 1 at its end
                float s = v.uv.y;                                   // -1 / +1 across the line
                float3 e = (wo - wp) * (t < 0.5 ? 1.0 : -1.0);      // start -> end, world space (follows squash)
                float len = length(e);
                float3 dir = e / max(len, 1e-5);
                float3 viewDir = normalize(_WorldSpaceCameraPos - wp);
                float3 side = cross(dir, viewDir);
                float sl = length(side);
                side = sl > 1e-4 ? side / sl : normalize(cross(dir, float3(0.0, 1.0, 0.01)));
                float depth = abs(mul(UNITY_MATRIX_V, float4(wp, 1.0)).z);
                float pxWorld = 2.0 * depth / max(abs(UNITY_MATRIX_P[1][1]) * _ScreenParams.y, 1e-4);   // world units per pixel here
                float hw = max(_Width * 0.5, _MinPixels * 0.5 * pxWorld);
                float ext = (t < 0.5 ? -1.0 : 1.0) * hw;            // run past the corner by a half width: the edges join cleanly
                wp += side * (s * hw) + dir * ext;
                o.pos = mul(UNITY_MATRIX_VP, float4(wp, 1.0));
                o.side = s;
                o.along = t * len + ext;
                o.len = len;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float across = abs(i.side);                                          // 0 centre .. 1 outer edge
                float aaA = max(fwidth(across), 1e-4);
                float inside = 1.0 - smoothstep(1.0 - aaA, 1.0, across);             // the line's hard outer edge
                float core = 1.0 - smoothstep(1.0 - _InkShare - aaA, 1.0 - _InkShare + aaA, across);   // bright core inside the ink border
                float d = min(i.along, i.len - i.along);                             // distance to the nearest corner
                float aaL = max(fwidth(i.along), 1e-5);
                float corner = 1.0 - smoothstep(_Corner - aaL, _Corner + aaL, d);    // solid bracket at each corner
                float period = max(_DashLen * 2.0, 1e-3);
                float x = (i.along - _Time.y * _Speed) / period;
                float ph = frac(x);
                float aaP = max(fwidth(x), 1e-4);
                float dash = smoothstep(0.0, aaP, ph) * (1.0 - smoothstep(0.5 - aaP, 0.5 + aaP, ph));   // hard-edged marching dash
                float lit = max(dash, corner);
                float pulse = 1.0 + _Pulse * sin(_Time.y * 7.54);                    // +-15 % at 1.2 Hz
                float3 col = lerp(_Ink.rgb, saturate(_Color.rgb * pulse), core * lit);
                float a = inside * lerp(_GapAlpha, 1.0, lit) * _Fade;
                return fixed4(col, a);
            }
            ENDCG
        }
    }
}
