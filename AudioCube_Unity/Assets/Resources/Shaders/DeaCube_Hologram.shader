Shader "DeaCube/Hologram"
{
    // SPEC v4 §4 R3 (package R): the draft cube while a path is drawn — a semi-transparent hologram in the instrument colour (Spider-Verse
    // multiverse glitch): a fresnel rim and bright rounded edges, fine scanlines scrolling up the cube, a flicker on twos, horizontal glitch
    // bands that tear the mesh sideways (time-quantised to 12 fps). AudioCube draws two more copies with this shader, additive, tinted red and
    // cyan and pushed apart along the camera's right axis: the chromatic split. _Flash turns it white (the solidify), _Wipe sweeps a
    // scanline front down through it and discards what is above (the cancel). Premultiplied output: the body blends One / OneMinusSrcAlpha,
    // the split copies One / One. Unlit, transparent, no shadows, both faces (the far edges show through, dimmer).
    Properties
    {
        _Color ("Colour", Color) = (0.4, 0.8, 1, 1)
        _Alpha ("Alpha", Range(0,1)) = 0.78
        _Glitch ("Glitch", Range(0,1)) = 0.22
        _Flash ("White flash", Range(0,1)) = 0
        _Wipe ("Wipe (cancel)", Range(0,1)) = 0
        _Seed ("Seed", Float) = 0
        _ScanDensity ("Scanlines per unit", Float) = 26
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10
    }
    SubShader
    {
        Tags { "Queue"="Transparent+5" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Sphere" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off
        Cull Off
        Lighting Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color; float _Alpha, _Glitch, _Flash, _Wipe, _Seed, _ScanDensity;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 wpos : TEXCOORD0; float3 wn : TEXCOORD1; float3 opos : TEXCOORD2; float tear : TEXCOORD3; };

            float hash11(float p) { p = frac(p * 0.1031); p *= p + 33.33; p *= p + p; return frac(p); }

            v2f vert (appdata v)
            {
                v2f o;
                float3 op = v.vertex.xyz;
                // glitch bands: 7 horizontal slices of the unit cube; on a 12 fps clock some of them slide sideways
                float tq = floor(_Time.y * 12.0);
                float band = floor((op.y + 0.5) * 7.0);
                float h = hash11(band * 17.0 + tq * 3.1 + _Seed * 13.0);
                float on = step(1.0 - 0.5 * _Glitch, h);
                float shift = on * (hash11(band * 5.0 + tq * 7.7 + _Seed) - 0.5) * 0.36 * _Glitch;
                op.x += shift;
                o.tear = on * _Glitch;
                o.opos = v.vertex.xyz;
                float4 wp = mul(unity_ObjectToWorld, float4(op, 1.0));
                o.wpos = wp.xyz;
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.pos = mul(UNITY_MATRIX_VP, wp);
                return o;
            }

            fixed4 frag (v2f i, fixed facing : VFACE) : SV_Target
            {
                // the cancel wipe: a scanline front sweeps from the top (object y +0.6) to the bottom (-0.6); above it only broken lines remain
                float front = lerp(0.62, -0.62, _Wipe);
                float yo = i.opos.y;
                float lines = frac(yo * 18.0 + _Time.y * 3.0);
                if (_Wipe > 0.0 && yo > front && lines > 0.18 * (1.0 - _Wipe)) discard;

                float3 n = normalize(i.wn) * (facing > 0 ? 1.0 : -1.0);
                float3 v = normalize(_WorldSpaceCameraPos - i.wpos);
                float ndv = saturate(abs(dot(n, v)));
                float rim = pow(1.0 - ndv, 2.2);
                // the rounded edges: close to two faces of the unit box at once
                float3 a = saturate((abs(i.opos) * 2.0 - 0.8) / 0.17);
                float edge = saturate(a.x * a.y + a.y * a.z + a.z * a.x);
                // scanlines scroll up the world (fine, flat-bottomed); a flicker on twos
                float scan = 0.5 + 0.5 * sin((i.wpos.y * _ScanDensity - _Time.y * 4.0) * 6.2832);
                scan = smoothstep(0.2, 0.8, scan);
                float flick = 0.84 + 0.16 * hash11(floor(_Time.y * 12.0) + _Seed * 7.0);
                // the instrument colour carries the body; only the edges and the rim lift toward white
                float3 hi = lerp(_Color.rgb, float3(1.0, 1.0, 1.0), 0.3);
                float3 col = _Color.rgb * (0.8 + 0.35 * scan) + hi * (rim * 0.4 + edge * 0.75) + hi * i.tear * 0.5;
                float alpha = _Alpha * (0.52 + 0.22 * scan + 0.45 * rim + 0.8 * edge) * flick;
                if (facing < 0) alpha *= 0.45;
                // the wipe front glows
                float fg = _Wipe > 0.0 ? saturate(1.0 - abs(yo - front) / 0.08) : 0.0;
                col += hi * fg * 1.5; alpha = saturate(alpha + fg * 0.8);
                // the solidify flash: white and opaque
                col = lerp(col, float3(1.0, 1.0, 1.0), _Flash);
                alpha = lerp(saturate(alpha), 1.0, _Flash);
                return fixed4(col * alpha, alpha);
            }
            ENDCG
        }
    }
}
