// Lens of the reflex sight. The reticle is not painted on the glass: it is drawn at the exact centre of the
// screen (where shots land) and only shows where the lens covers it, so it stays put while the weapon sways
// and kicks around it. The glass itself is a faint tint with a soft sheen toward its edges.
Shader "LT/SightLens"
{
    Properties
    {
        _Reticle ("Reticle (R = main, G = accent)", 2D) = "black" {}
        _ReticleColor ("Reticle Colour", Color) = (1.6, 1.5, 1.3, 1)
        _AccentColor ("Accent Colour", Color) = (2.4, 0.9, 0.3, 1)
        _ReticleSize ("Reticle Size (fraction of screen height)", Float) = 0.34
        _GlassColor ("Glass Tint", Color) = (0.45, 0.85, 0.95, 1)
        _GlassOpacity ("Glass Opacity", Range(0, 1)) = 0.012
        _Sheen ("Edge Sheen", Range(0, 1)) = 0.06
        _Visibility ("Reticle Visibility", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+50" "RenderType" = "Transparent" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            Cull Off
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_Reticle); SAMPLER(sampler_Reticle);
            CBUFFER_START(UnityPerMaterial)
                half4 _ReticleColor, _AccentColor, _GlassColor;
                float _ReticleSize, _GlassOpacity, _Sheen, _Visibility;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 screenPos : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.screenPos = ComputeScreenPos(o.positionCS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // Screen-centred reticle, square in pixels regardless of aspect ratio.
                float2 s = i.screenPos.xy / i.screenPos.w - 0.5;
                s.x *= _ScreenParams.x / _ScreenParams.y;
                float2 ruv = s / _ReticleSize + 0.5;
                half4 tex = SAMPLE_TEXTURE2D(_Reticle, sampler_Reticle, ruv);
                float inside = step(0.0, ruv.x) * step(ruv.x, 1.0) * step(0.0, ruv.y) * step(ruv.y, 1.0);
                float3 reticle = (tex.r * _ReticleColor.rgb + tex.g * _AccentColor.rgb) * inside * _Visibility;

                // Glass: barely there in the middle, a little sheen toward the rim.
                float2 c = i.uv - 0.5;
                float rim = saturate(dot(c, c) * 3.2);
                float glass = _GlassOpacity + _Sheen * rim * rim;
                return half4(_GlassColor.rgb * glass + reticle, glass);
            }
            ENDHLSL
        }
    }
}
