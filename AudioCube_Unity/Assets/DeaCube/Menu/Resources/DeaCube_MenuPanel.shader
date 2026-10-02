Shader "DeaCube/MenuPanel"
{
    // The title screen's exit (SPEC v4 §7b, MenuLeave / MenuPanel): the captured menu frame drawn on UI polygons. The capture's
    // alpha is ignored (the back buffer's alpha means nothing): alpha = vertex alpha. _Impact 1 = the impact frame: the same frame
    // printed in two tones, bright → ink, the rest → paper (a white page with the title and the menu as ink silhouettes).
    Properties
    {
        [PerRendererData] _MainTex ("Captured frame", 2D) = "white" {}
        _Impact ("Impact (two-tone)", Float) = 0
        _Thresh ("Impact threshold (linear luminance)", Float) = 0.2
        _InkCol ("Ink", Color) = (0.1, 0.06, 0.17, 1)
        _PaperCol ("Paper", Color) = (1, 0.98, 0.95, 1)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float _Impact, _Thresh;
            float4 _InkCol, _PaperCol;
            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }
            half4 frag (Varyings i) : SV_Target
            {
                half3 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb;
                half l = dot(c, half3(0.299, 0.587, 0.114));
                half3 two = l > _Thresh ? _InkCol.rgb : _PaperCol.rgb;
                c = lerp(c, two, saturate(_Impact));
                return half4(c * i.color.rgb, i.color.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
