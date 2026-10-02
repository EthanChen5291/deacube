Shader "DeaCube/Flow"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _Intensity ("Intensity", Float) = 1
        _Tiling ("Dashes per unit", Float) = 1.2
        _Speed ("Speed", Float) = 1.5
        _Base ("Base brightness", Range(0,1)) = 0.25
        // additive by default; the Route cable sets One / OneMinusSrcAlpha (the output is premultiplied): alpha-blended (fix F10)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
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
            fixed4 _Color; float _Intensity; float _Tiling; float _Speed; float _Base;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; o.color = v.color * _Color; return o; }
            fixed4 frag (v2f i) : SV_Target
            {
                float d = frac(i.uv.x * _Tiling - _Time.y * _Speed);
                float dash = smoothstep(0.0, 0.18, d) * smoothstep(0.62, 0.42, d);
                float edge = 1.0 - abs(i.uv.y * 2.0 - 1.0);
                edge = pow(saturate(edge), 1.5);
                float a = (_Base + (1.0 - _Base) * dash) * edge * i.color.a;
                return fixed4(i.color.rgb * a * _Intensity, a);
            }
            ENDCG
        }
    }
}
