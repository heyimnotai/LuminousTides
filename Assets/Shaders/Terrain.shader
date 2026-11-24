// Tidal rock: triplanar rock texture with large-scale colour variation, golden lichen on flat tops,
// glowing cyan veins, a wet band at the waterline and a bright sandy tint under water.
Shader "LT/Terrain"
{
    Properties
    {
        _BaseMap ("Rock Albedo", 2D) = "white" {}
        _BumpMap ("Rock Normal", 2D) = "bump" {}
        _Noise ("Noise", 2D) = "gray" {}
        _BaseColor ("Tint", Color) = (0.36, 0.39, 0.56, 1)
        _ColorA ("Variation A", Color) = (0.72, 0.84, 1.10, 1)
        _ColorB ("Variation B", Color) = (0.98, 0.86, 1.12, 1)
        _LichenColor ("Lichen", Color) = (0.95, 0.66, 0.16, 1)
        _VeinColor ("Vein Glow", Color) = (0.15, 1.0, 0.92, 1)
        _VeinIntensity ("Vein Intensity", Float) = 2.2
        _UnderwaterColor ("Underwater Tint", Color) = (0.55, 0.85, 0.82, 1)
        _Tiling ("Tiling", Float) = 0.16
        _Cutoff ("Alpha Cutoff", Float) = 0.5
        // Which ground this is: x salt flats, y forest moss, z storm slate (all zero = tidal rock). w = 1 uses this
        // value for the whole mesh; w = 0 reads it per vertex from the mesh colours, so the terrain can blend.
        _Biome ("Biome", Vector) = (0, 0, 0, 1)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" "RenderType" = "Opaque" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            TEXTURE2D(_Noise);   SAMPLER(sampler_Noise);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor, _ColorA, _ColorB, _LichenColor, _VeinColor, _UnderwaterColor;
                float _VeinIntensity, _Tiling, _Cutoff;
                float4 _Biome;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  fogFactor  : TEXCOORD2;
                float3 biome      : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                o.biome = lerp(v.color.rgb, _Biome.xyz, _Biome.w);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 P = i.positionWS;
                float3 Ng = normalize(i.normalWS);

                // Triplanar albedo; the detail normal is taken from the dominant (top) projection.
                float3 w = pow(abs(Ng), 4.0);
                w /= (w.x + w.y + w.z);
                float2 uvY = P.xz * _Tiling, uvX = P.zy * _Tiling, uvZ = P.xy * _Tiling;
                float3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvY).rgb * w.y
                              + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvX).rgb * w.x
                              + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvZ).rgb * w.z;
                float3 nTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvY), 2.4);
                float3 nTS2 = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvY * 3.7), 1.2);
                float3 N = normalize(Ng + float3(nTS.x + nTS2.x, 0.0, nTS.y + nTS2.y) * w.y);

                albedo = pow(saturate(albedo), 1.6) * 1.5;

                // Large-scale colour drift so the ground never reads as one flat colour.
                float4 big = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, P.xz * 0.011);
                float4 mid = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, P.xz * 0.06);
                albedo *= lerp(_ColorA.rgb, _ColorB.rgb, big.r) * lerp(0.78, 1.18, mid.g) * _BaseColor.rgb;

                // Lighter worn edges where slabs step up, darker in creases.
                float flatness = saturate(Ng.y);
                albedo *= lerp(0.72, 1.0, smoothstep(0.55, 0.95, flatness));
                float cliff = smoothstep(0.80, 0.50, flatness) * step(0.1, P.y);
                float strata = 0.82 + 0.36 * smoothstep(0.35, 0.65, frac(P.y * 1.7 + mid.r * 0.6));
                albedo = lerp(albedo, (albedo * float3(2.1, 1.9, 2.5) + float3(0.035, 0.03, 0.075)) * strata, cliff);

                // ---- The other kinds of ground, blended in by the biome weights.
                float salt = saturate(i.biome.x), moss = saturate(i.biome.y), slate = saturate(i.biome.z);
                float tidal = saturate(1.0 - salt - moss - slate);
                float lum = dot(albedo, float3(0.3, 0.45, 0.25));
                float crackField = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, P.xz * 0.13 + mid.rg * 0.05).a;
                float cracks = pow(saturate(1.0 - abs(crackField - 0.5) * 40.0), 2.0);
                // Salt flats: pale and chalky, split by dark cracks, a little pink where minerals pool.
                float3 saltCol = (lum * 0.9 + 0.33) * lerp(float3(0.96, 0.90, 0.78), float3(1.0, 0.80, 0.74), big.b * 0.6);
                saltCol = lerp(saltCol, saltCol * 0.38, cracks * smoothstep(0.7, 0.95, flatness));
                // Forest floor: deep green moss with bruise-purple patches.
                float3 mossCol = (lum * 2.6 + 0.10) * lerp(float3(0.14, 0.56, 0.34), float3(0.46, 0.16, 0.50), smoothstep(0.45, 0.7, big.r));
                // Storm slate: cold grey rock, snow lying on the level tops.
                float3 slateCol = (lum * 1.5 + 0.11) * float3(0.50, 0.56, 0.68);
                float snow = smoothstep(0.80, 0.93, flatness) * smoothstep(0.52, 0.80, big.a * 0.6 + mid.b * 0.4 + (P.y - 8.0) * 0.05) * step(3.0, P.y);
                slateCol = lerp(slateCol, float3(0.92, 0.95, 1.0), snow);
                albedo = albedo * tidal + saltCol * salt + mossCol * moss + slateCol * slate;

                // Golden lichen speckles on sun-facing tops.
                float fine = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, P.xz * 0.9).b;
                float lichen = smoothstep(0.86, 0.94, mid.a) * smoothstep(0.62, 0.80, fine) * smoothstep(0.35, 0.6, big.b) * smoothstep(0.8, 0.95, flatness) * step(0.2, P.y);
                lichen *= tidal;
                albedo = lerp(albedo, _LichenColor.rgb * 0.8, lichen * 0.85);

                // Glowing veins follow noise iso-lines, only in some regions, with a slow pulse.
                float veinField = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, P.xz * 0.045 + mid.rg * 0.02).a;
                float vein = pow(saturate(1.0 - abs(veinField - 0.5) * 95.0), 1.5);
                float veinMask = smoothstep(0.58, 0.78, big.g) * step(0.05, P.y);
                float pulse = 0.75 + 0.25 * sin(_Time.y * 1.3 + P.x * 0.2 + P.z * 0.17);
                // Veins: cyan in the tidal rock, green and more of them in the forest, amber on the peaks, none in the salt.
                float3 veinCol = _VeinColor.rgb * tidal + float3(0.35, 1.0, 0.30) * moss + float3(1.0, 0.62, 0.14) * slate;
                veinMask = lerp(veinMask, smoothstep(0.35, 0.6, big.g) * step(0.05, P.y), moss) * (1.0 - salt);
                vein *= 1.0 - snow * slate;
                float3 emission = veinCol * vein * veinMask * _VeinIntensity * pulse * (1.0 + moss * 0.6);
                albedo = lerp(albedo, albedo * 0.45, vein * veinMask);

                // Waterline: wet and dark just above it, bright sandy turquoise below it.
                float wet = smoothstep(0.45, 0.02, P.y);
                float under = smoothstep(0.02, -0.3, P.y);
                albedo = lerp(albedo, albedo * 0.62, wet * (1.0 - under));
                albedo = lerp(albedo, (albedo * 2.2 + 0.22) * _UnderwaterColor.rgb, under);
                float smoothness = lerp(0.22, 0.78, wet * (1.0 - under)) * (1.0 - lichen);

                InputData inputData = (InputData)0;
                inputData.positionWS = P;
                inputData.normalWS = N;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(P);
                inputData.shadowCoord = TransformWorldToShadowCoord(P);
                inputData.fogCoord = i.fogFactor;
                inputData.bakedGI = SampleSH(N);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.smoothness = smoothness;
                surfaceData.occlusion = 1.0;
                surfaceData.emission = emission;
                surfaceData.alpha = 1.0;
                surfaceData.normalTS = float3(0, 0, 1);

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return half4(color.rgb, 1.0);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
