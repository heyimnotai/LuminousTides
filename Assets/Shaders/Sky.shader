// Stylised alien sky: colour gradient, sun, a banded gas giant hanging in the haze and drifting clouds.
Shader "LT/Sky"
{
    Properties
    {
        _Zenith ("Zenith", Color) = (0.05, 0.08, 0.40, 1)
        _Mid ("Mid Sky", Color) = (0.18, 0.42, 0.98, 1)
        _Horizon ("Horizon", Color) = (0.62, 0.86, 1.0, 1)
        _Below ("Below Horizon", Color) = (0.35, 0.78, 0.86, 1)
        _SunColor ("Sun Colour", Color) = (1.0, 0.95, 0.8, 1)
        _PlanetDir ("Planet Direction", Vector) = (0.40, 0.36, 0.84, 0)
        _PlanetSize ("Planet Angular Radius", Float) = 0.24
        _PlanetAxis ("Planet Band Axis", Vector) = (0.25, 0.95, 0.15, 0)
        _PlanetColorA ("Planet Colour A", Color) = (0.80, 0.66, 0.96, 1)
        _PlanetColorB ("Planet Colour B", Color) = (0.56, 0.48, 0.90, 1)
        _PlanetColorC ("Planet Colour C", Color) = (0.98, 0.80, 0.90, 1)
        _CloudColor ("Cloud Colour", Color) = (1.0, 0.93, 0.88, 1)
        _CloudShade ("Cloud Shade", Color) = (0.66, 0.56, 0.96, 1)
        _CloudCoverage ("Cloud Coverage", Range(0, 1)) = 0.30
        _Noise ("Noise", 2D) = "gray" {}
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);

            CBUFFER_START(UnityPerMaterial)
                half4 _Zenith, _Mid, _Horizon, _Below, _SunColor, _PlanetColorA, _PlanetColorB, _PlanetColorC, _CloudColor, _CloudShade;
                float4 _PlanetDir, _PlanetAxis;
                float _PlanetSize, _CloudCoverage;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            float Fbm(float2 uv)
            {
                float a = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, uv).r;
                float b = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, uv * 2.7 + 0.37).g;
                float c = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, uv * 6.1 + 0.11).b;
                return a * 0.70 + b * 0.24 + c * 0.06;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 L = normalize(_MainLightPosition.xyz);
                float h = d.y;

                // Gradient.
                float3 col = lerp(_Horizon.rgb, _Mid.rgb, smoothstep(0.0, 0.28, h));
                col = lerp(col, _Zenith.rgb, smoothstep(0.25, 0.95, h));
                col = lerp(col, _Below.rgb, smoothstep(0.0, -0.12, h));

                // Sun glow (the disc itself is added after the clouds).
                float sd = dot(d, L);
                col += _SunColor.rgb * (pow(saturate(sd), 24.0) * 0.22 + pow(saturate(sd), 400.0) * 0.6);

                // A band of rose light low in the sky away from the sun, and gold toward it.
                col += float3(1.0, 0.48, 0.72) * exp(-pow((h - 0.07) / 0.11, 2.0)) * 0.24 * (1.0 - saturate(sd));
                col += float3(1.0, 0.72, 0.35) * exp(-pow((h - 0.03) / 0.09, 2.0)) * 0.30 * pow(saturate(sd), 2.0);

                // High up the sky thins to space: stars that twinkle, and a nebula drifting behind them.
                float high = smoothstep(0.20, 0.78, h);
                float az = atan2(d.x, d.z);
                float2 suv = float2(az * 40.0, asin(saturate(h)) * 64.0);
                float2 cell = floor(suv);
                float rnd = frac(sin(dot(cell, float2(127.1, 311.7))) * 43758.5453);
                float2 off = (frac(rnd * float2(13.7, 7.3)) - 0.5) * 0.6;
                float star = step(0.88, rnd) * smoothstep(0.20, 0.0, length(frac(suv) - 0.5 + off));
                float twinkle = 0.6 + 0.4 * sin(_Time.y * (2.0 + rnd * 4.0) + rnd * 40.0);
                col += star * twinkle * high * (0.5 + rnd) * float3(0.85, 0.92, 1.0);
                float2 nuv = d.xz / (h + 0.55) * 0.30;
                float neb = Fbm(nuv + 3.1 + _Time.y * 0.0006);
                float neb2 = Fbm(nuv * 1.7 - 1.3);
                float3 nebCol = lerp(float3(0.80, 0.22, 0.95), float3(0.15, 0.85, 0.98), neb2);
                col += nebCol * smoothstep(0.48, 0.82, neb) * high * 0.5;

                // Aurora: curtains of green rising into violet, rippling slowly round the whole horizon.
                float ripple = sin(az * 3.0 + _Time.y * 0.05) * 0.06 + sin(az * 7.0 - _Time.y * 0.08) * 0.03;
                float curtain = exp(-pow((h - 0.36 - ripple) / 0.11, 2.0));
                float rays = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, float2(az * 0.8 + _Time.y * 0.004, 0.37)).r;
                rays = smoothstep(0.30, 0.75, rays) * (0.7 + 0.3 * sin(az * 60.0 + _Time.y * 0.6));
                float3 aurora = lerp(float3(0.10, 1.0, 0.62), float3(0.62, 0.30, 1.0), saturate((h - 0.30 - ripple) / 0.22));
                col += aurora * curtain * rays * 0.6 * (1.0 - saturate(sd * 1.4));

                // Two small moons, each a lit crescent.
                for (int m = 0; m < 2; m++)
                {
                    float3 md = m == 0 ? normalize(float3(-0.55, 0.50, 0.67)) : normalize(float3(-0.80, 0.26, -0.30));
                    float msize = m == 0 ? 0.045 : 0.026;
                    float3 mr = normalize(cross(float3(0, 1, 0), md));
                    float3 mu = cross(md, mr);
                    float2 mp = float2(dot(d, mr), dot(d, mu)) / msize;
                    float mr2 = dot(mp, mp);
                    if (dot(d, md) > 0.0 && mr2 < 1.0)
                    {
                        float mz = sqrt(1.0 - mr2);
                        float3 mn = normalize(mp.x * mr + mp.y * mu - mz * md);
                        float lit = smoothstep(-0.1, 0.5, dot(mn, L));
                        float crater = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, mp * 0.35 + m * 0.4).g;
                        float3 mc = (m == 0 ? float3(0.95, 0.90, 0.82) : float3(0.75, 0.92, 0.95)) * (0.75 + 0.35 * crater);
                        col = lerp(col, lerp(col, mc, 0.25 + 0.75 * lit), smoothstep(1.0, 0.94, mr2));
                    }
                }

                // Gas giant, drawn as a lit sphere and washed by atmospheric haze.
                float3 pd = normalize(_PlanetDir.xyz);
                float3 right = normalize(cross(float3(0, 1, 0), pd));
                float3 up = cross(pd, right);
                float2 p = float2(dot(d, right), dot(d, up)) / _PlanetSize;
                float r2 = dot(p, p);
                if (dot(d, pd) > 0.0 && r2 < 1.0)
                {
                    float z = sqrt(1.0 - r2);
                    float3 n = normalize(p.x * right + p.y * up - z * pd);
                    float3 axis = normalize(_PlanetAxis.xyz);
                    float lat = dot(n, axis);
                    float lon = atan2(dot(n, cross(axis, pd)), -dot(n, pd));
                    float swirl = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, float2(lon * 0.35 + _Time.y * 0.0015, lat * 1.2)).a;
                    float bands = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, float2(lon * 0.03 + swirl * 0.04, lat * 0.55 + swirl * 0.03)).r;
                    float fine = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, float2(lon * 0.06 + swirl * 0.06, lat * 1.7)).g;
                    float3 pc = lerp(_PlanetColorB.rgb, _PlanetColorA.rgb, smoothstep(0.25, 0.75, bands));
                    pc = lerp(pc, _PlanetColorC.rgb, smoothstep(0.55, 0.95, fine) * 0.55);
                    float sunLit = smoothstep(-0.35, 0.55, dot(n, L));
                    float rim = pow(1.0 - z, 2.5);
                    pc = pc * lerp(0.38, 1.12, sunLit) + _Horizon.rgb * rim * 0.45;
                    // Daytime haze: the dark side dissolves into the sky colour.
                    float haze = lerp(0.50, 0.10, sunLit) + smoothstep(0.25, -0.02, h) * 0.3;
                    float edge = smoothstep(1.0, 0.985, r2);
                    col = lerp(col, lerp(pc, col, saturate(haze)), edge);
                }
                // Its rings: a flat disc round the planet's equator, seen at an angle, banded light and dark.
                if (dot(d, pd) > 0.0)
                {
                    float3 ax3 = normalize(_PlanetAxis.xyz);
                    float3 ra = float3(dot(ax3, right), dot(ax3, up), -dot(ax3, pd));
                    float zc = -(p.x * ra.x + p.y * ra.y) / (abs(ra.z) < 0.05 ? 0.05 : ra.z);       // depth of the ring plane on this line of sight
                    float rho = sqrt(r2 + zc * zc);
                    float visible = (r2 >= 1.0 || zc > 0.0) ? 1.0 : 0.0;                              // hidden where the planet stands in front
                    float band = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, float2(rho * 0.9, 0.61)).r;
                    float ring = smoothstep(1.30, 1.38, rho) * smoothstep(2.05, 1.95, rho) * (0.25 + 0.75 * smoothstep(0.3, 0.7, band));
                    ring *= 1.0 - smoothstep(1.62, 1.66, rho) * smoothstep(1.74, 1.70, rho) * 0.85;   // a dark gap part-way out
                    float3 ringCol = lerp(float3(0.78, 0.62, 0.86), float3(0.56, 0.70, 0.90), band);
                    col = lerp(col, ringCol, ring * visible * 0.5);
                }

                // Clouds on a plane high above, lit from the sun side.
                if (h > 0.005)
                {
                    float2 cuv = d.xz / (h + 0.16) * 0.065 + _Time.y * float2(0.0010, 0.0004);
                    float n0 = Fbm(cuv);
                    float n1 = Fbm(cuv + L.xz * 0.012);
                    float cover = 1.0 - _CloudCoverage;
                    float density = smoothstep(cover, cover + 0.22, n0) * smoothstep(0.005, 0.10, h);
                    float light = saturate((n0 - n1) * 6.0 + 0.72);
                    float3 cc = lerp(_CloudShade.rgb, _CloudColor.rgb, light);
                    cc = lerp(cc, _Horizon.rgb, smoothstep(0.22, 0.02, h) * 0.6);
                    col = lerp(col, cc, density * 0.88);
                }

                col += _SunColor.rgb * smoothstep(0.99965, 0.9998, sd) * 6.0;
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
