Shader "DeaCube/Sky"
{
    Properties
    {
        _Top ("Top", Color) = (0.03, 0.02, 0.08, 1)
        _Horizon ("Horizon", Color) = (0.25, 0.12, 0.40, 1)
        _Bottom ("Bottom", Color) = (0.02, 0.015, 0.05, 1)
        _HorizonPow ("Horizon width", Float) = 3.0
        _Glow ("Horizon glow", Float) = 0.55
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Front
        ZWrite Off
        Lighting Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Top, _Horizon, _Bottom; float _HorizonPow, _Glow;
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };
            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.dir = wp - _WorldSpaceCameraPos;
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                float y = normalize(i.dir).y;
                float band = pow(saturate(1.0 - abs(y)), _HorizonPow);
                fixed4 c = y > 0 ? lerp(_Horizon, _Top, pow(saturate(y), 0.55)) : lerp(_Horizon, _Bottom, pow(saturate(-y), 0.5));
                c.rgb += _Horizon.rgb * band * _Glow;
                return c;
            }
            ENDCG
        }
    }
}
