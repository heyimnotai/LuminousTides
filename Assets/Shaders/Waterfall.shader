// Falling water sheet: streaky noise scrolling down, soft edges, slight glow. UV.y runs top (1) to bottom (0).
Shader "LT/Waterfall"
{
    Properties
    {
        _Color ("Colour", Color) = (0.55, 1.0, 0.98, 1)
        _Core ("Core Colour", Color) = (1, 1, 1, 1)
        _Noise ("Noise", 2D) = "gray" {}
        _Speed ("Fall Speed", Float) = 1.6
        _Opacity ("Opacity", Range(0, 1)) = 0.62
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+10" "RenderType" = "Transparent" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);
            CBUFFER_START(UnityPerMaterial)
                half4 _Color, _Core;
                float _Speed, _Opacity;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; float fogFactor : TEXCOORD2; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = v.uv;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y * _Speed;
                float seed = frac(dot(floor(i.positionWS.xz * 0.37), float2(0.13, 0.71)));
                // Stretch the noise vertically so it reads as falling streaks.
                float a = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, float2(i.uv.x * 0.55 + seed, i.uv.y * 0.07 + t * 0.11)).r;
                float b = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, float2(i.uv.x * 1.3 + seed * 3.0, i.uv.y * 0.16 + t * 0.19)).b;
                float streak = saturate(a * 0.7 + b * 0.6);
                float edge = smoothstep(0.0, 0.3, i.uv.x) * smoothstep(1.0, 0.7, i.uv.x);
                float ends = smoothstep(1.0, 0.94, i.uv.y) * smoothstep(0.0, 0.06, i.uv.y);
                float alpha = saturate(smoothstep(0.25, 0.8, streak * edge + edge * 0.25)) * ends * _Opacity;
                float3 col = lerp(_Color.rgb, _Core.rgb, smoothstep(0.6, 0.98, streak)) * 1.05;
                col = MixFog(col, i.fogFactor);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
