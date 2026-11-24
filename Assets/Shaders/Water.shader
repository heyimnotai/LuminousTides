// Stylised tidal water: depth-based colour, refraction, caustics on the sea floor,
// shoreline foam, sky reflection and sun glints. Needs the URP depth and opaque textures.
Shader "LT/Water"
{
    Properties
    {
        _ShallowColor ("Shallow Tint", Color) = (0.55, 1.0, 0.95, 1)
        _DeepColor ("Deep Colour", Color) = (0.02, 0.42, 0.55, 1)
        _Density ("Depth Density", Float) = 0.55
        _NormalMap ("Ripple Normals", 2D) = "bump" {}
        _NormalStrength ("Ripple Strength", Float) = 0.35
        _Refraction ("Refraction", Float) = 0.035
        _Caustics ("Caustics", 2D) = "black" {}
        _CausticColor ("Caustic Colour", Color) = (0.75, 1.0, 0.95, 1)
        _CausticStrength ("Caustic Strength", Float) = 1.4
        _FoamColor ("Foam Colour", Color) = (0.92, 1, 1, 1)
        _FoamWidth ("Foam Width", Float) = 0.22
        _Noise ("Noise", 2D) = "gray" {}
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }
        Pass
        {
            Name "Water"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            Cull Back
            Blend One Zero

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
            TEXTURE2D(_Caustics);  SAMPLER(sampler_Caustics);
            TEXTURE2D(_Noise);     SAMPLER(sampler_Noise);

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor, _DeepColor, _CausticColor, _FoamColor;
                float _Density, _NormalStrength, _Refraction, _CausticStrength, _FoamWidth;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
                float  fogFactor  : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.screenPos = ComputeScreenPos(o.positionCS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float3 ScenePosition(float2 uv, out float eyeDepth)
            {
                float raw = SampleSceneDepth(uv);
                eyeDepth = LinearEyeDepth(raw, _ZBufferParams);
                return ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                float2 uv = i.positionWS.xz;
                float2 screenUV = i.screenPos.xy / i.screenPos.w;
                float surfaceDepth = i.screenPos.w;

                // Two scrolling ripple layers; fade them with distance to avoid shimmer.
                float3 n1 = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv * 0.11 + t * float2(0.021, 0.013)));
                float3 n2 = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv * 0.047 + t * float2(-0.012, 0.017)));
                float distFade = saturate(1.0 - surfaceDepth / 350.0);
                float2 ripple = (n1.xy + n2.xy) * _NormalStrength * lerp(0.25, 1.0, distFade);
                float3 N = normalize(float3(ripple.x, 1.0, ripple.y));

                // Refracted scene behind the surface.
                float sceneDepth;
                float3 scenePos = ScenePosition(screenUV, sceneDepth);
                float2 refractUV = screenUV + ripple * _Refraction * saturate((sceneDepth - surfaceDepth) * 0.5);
                float refractDepth;
                float3 refractPos = ScenePosition(refractUV, refractDepth);
                if (refractDepth < surfaceDepth) { refractUV = screenUV; refractPos = scenePos; refractDepth = sceneDepth; }
                float3 scene = SampleSceneColor(refractUV);

                float waterDepth = max(0.0, i.positionWS.y - refractPos.y);
                float viewDepth = max(0.0, refractDepth - surfaceDepth);

                // Caustic lattice projected on the floor, strongest in the shallows.
                float2 cuv = refractPos.xz;
                float c1 = SAMPLE_TEXTURE2D(_Caustics, sampler_Caustics, cuv * 0.21 + ripple * 0.15 + t * float2(0.02, 0.015)).r;
                float c2 = SAMPLE_TEXTURE2D(_Caustics, sampler_Caustics, cuv * 0.16 - ripple * 0.12 - t * float2(0.017, 0.012)).r;
                float caustic = min(c1, c2) * 2.0 + c1 * c2;
                float causticFade = saturate(1.0 - waterDepth / 2.2) * saturate(waterDepth * 6.0) * saturate(1.0 - surfaceDepth / 120.0);
                scene += caustic * _CausticColor.rgb * _CausticStrength * causticFade;

                // Absorption: clear turquoise in the shallows, saturated teal in the deep.
                float absorb = 1.0 - exp(-max(waterDepth, viewDepth * 0.35) * _Density);
                float3 col = lerp(scene * _ShallowColor.rgb, _DeepColor.rgb, absorb);

                // Sky reflection and sun glints.
                float3 V = normalize(_WorldSpaceCameraPos - i.positionWS);
                float fresnel = 0.03 + 0.97 * pow(1.0 - saturate(dot(N, V)), 5.0);
                float3 reflection = GlossyEnvironmentReflection(reflect(-V, N), 0.03, 1.0);
                col = lerp(col, reflection, fresnel * 0.85);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float3 H = normalize(mainLight.direction + V);
                float glint = pow(saturate(dot(N, H)), 520.0) * 6.0 + pow(saturate(dot(N, H)), 60.0) * 0.12;
                col += mainLight.color * glint * mainLight.shadowAttenuation;
                col *= lerp(0.72, 1.0, mainLight.shadowAttenuation);

                // Shoreline foam: a bright edge plus drifting bands that lap toward the shore.
                float noise = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, uv * 0.09 + t * 0.01).g;
                float edge = 1.0 - saturate(waterDepth / (_FoamWidth * (0.6 + noise)));
                float band = sin(waterDepth * 9.0 - t * 1.6 + noise * 6.0) * 0.5 + 0.5;
                float lap = smoothstep(0.75, 1.0, band) * saturate(1.0 - waterDepth / 0.9) * (0.35 + 0.65 * noise);
                float foam = saturate(smoothstep(0.35, 0.9, edge) + lap * 0.6) * saturate(1.0 - surfaceDepth / 200.0);
                col = lerp(col, _FoamColor.rgb, foam);

                col = MixFog(col, i.fogFactor);
                // Soft intersection: within a few centimetres of any surface the water fades to what is behind it,
                // so rocks, roots and the shoreline never cut a hard, aliased edge into it.
                float contact = saturate((sceneDepth - surfaceDepth) / 0.09);
                col = lerp(SampleSceneColor(screenUV), col, smoothstep(0.0, 1.0, contact));
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
