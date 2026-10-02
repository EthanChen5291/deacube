Shader "DeaCube/StageBeam"
{
    // v9 (L) the stage lights' BEAM: a cone of light coming down onto a lead grid (StageLights builds the mesh: an open frustum from the
    // grid's rounded footprint up to a small lamp circle; uv.y 0 at the pool .. 1 at the lamp). Additive, double-sided, no depth write.
    // The toon / print look: the light is cut into three flat bands (brighter through the middle of the beam as seen, fading up toward
    // the lamp), the faint outer band printed as Ben-Day dots, a thin brighter edge where the beam meets the pool. Fades with the fog.
    Properties
    {
        _Color ("Light", Color) = (1.0, 0.92, 0.72, 1)
        _Amount ("Amount (StageLights)", Range(0, 1)) = 1
        _Strength ("Strength", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Beam"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex BeamVert
            #pragma fragment BeamFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Amount;
                half _Strength;
            CBUFFER_END

            struct BeamAttributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct BeamVaryings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; };

            BeamVaryings BeamVert(BeamAttributes v)
            {
                BeamVaryings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                return o;
            }

            float DotCover(float2 px, float cell, float radius)
            {
                float2 q = float2(px.x - px.y, px.x + px.y) * (0.70710678 / cell);
                float d = length(frac(q) - 0.5);
                float aa = 0.8 / cell;
                return (1.0 - smoothstep(radius - aa, radius + aa, d)) * step(0.02, radius);
            }

            half4 BeamFrag(BeamVaryings i) : SV_Target
            {
                float v = i.uv.y;
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float facing = abs(dot(normalize(i.normalWS), V));          // 1 = the middle of the beam as seen, 0 = its silhouette
                float along = (1.0 - smoothstep(0.05, 0.92, v));             // full at the pool, gone at the lamp
                float I = lerp(0.3, 1.0, facing) * along;
                // three flat bands
                float aa = max(fwidth(I), 1e-3);
                float b1 = smoothstep(0.10 - aa, 0.10 + aa, I);
                float b2 = smoothstep(0.32 - aa, 0.32 + aa, I);
                float b3 = smoothstep(0.62 - aa, 0.62 + aa, I);
                // the outer band printed as dots (screen space, 45 degrees)
                float s = max(_ScreenParams.y, 1.0) / 1080.0;
                float dots = DotCover(i.positionCS.xy, 7.0 * s, 0.34);
                float band = b1 * (1.0 - b2) * dots * 0.55 + b2 * (1.0 - b3) * 0.55 + b3 * 0.85;
                // a thin brighter lip where the beam meets the pool
                float lip = (1.0 - smoothstep(0.0, 0.035, v)) * 0.6 * facing;
                // fade with the linear fog (the beam is light, not a surface: it thins out with distance)
                float dist = distance(GetCameraPositionWS(), i.positionWS);
                float vis = unity_FogParams.z != 0.0 ? saturate(dist * unity_FogParams.z + unity_FogParams.w) : 1.0;
                half3 c = _Color.rgb * (band + lip) * _Amount * _Strength * vis;
                return half4(c, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
