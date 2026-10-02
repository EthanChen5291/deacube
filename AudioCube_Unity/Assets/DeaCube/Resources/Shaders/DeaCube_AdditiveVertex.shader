Shader "DeaCube/AdditiveVertex"
{
    // Copy of DeaCube/Additive that multiplies by the mesh's vertex colour: for the segmented beat ring
    // (MeshFactory.SegmentRing) and the Moon ring, whose per-segment colours live in mesh.colors.
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _Intensity ("Intensity", Float) = 1
        _Fill ("Fill (uv.x cutoff)", Float) = 2
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Blend One One
        ZWrite Off
        Cull Off
        Lighting Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _MainTex_ST; fixed4 _Color; float _Intensity; float _Fill;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float2 rawuv : TEXCOORD1; fixed4 color : COLOR; };
            v2f vert (appdata v)
            {
                v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = TRANSFORM_TEX(v.uv, _MainTex); o.rawuv = v.uv; o.color = v.color * _Color; return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                if (i.rawuv.x > _Fill) discard;
                fixed4 t = tex2D(_MainTex, i.uv);
                fixed4 c = t * i.color;
                c.rgb *= c.a * _Intensity;
                return fixed4(c.rgb, c.a);
            }
            ENDCG
        }
    }
}
