// Procedural detail skin for the flat-color look — no textures, all math.
// Keeps identical models from reading as clones: the pattern lives in world
// space so every instance gets a different surface, plus a per-instance tint
// jitter hashed from the object's pivot. Optional world-Y strata bands for
// canyon/gorge rock and an up-face dusting (moss / snow / sand settling).
// Lit: main directional light + SH ambient, matching the URP/Lit mats it
// replaces closely enough for the stylized look.
Shader "WarriorRun/DetailSurface"
{
    Properties
    {
        _BaseColor   ("Base Color", Color) = (0.7, 0.7, 0.7, 1)
        _MottleAmt   ("Mottle Amount", Range(0, 1)) = 0.22
        _MottleScale ("Mottle Scale", Float) = 1.6
        _BandAmt     ("Strata Band Amount", Range(0, 0.6)) = 0.0
        _BandFreq    ("Strata Band Freq", Float) = 2.4
        _DustColor   ("Dust Color", Color) = (0.85, 0.80, 0.70, 1)
        _DustAmt     ("Dust Amount", Range(0, 1)) = 0.15
        _JitterAmt   ("Instance Tint Jitter", Range(0, 1)) = 0.30
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _MottleAmt, _MottleScale, _BandAmt, _BandFreq;
                float4 _DustColor;
                float _DustAmt, _JitterAmt;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 pivotWS    : TEXCOORD2;
                float  fogFactor  : TEXCOORD3;
            };

            float Hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            // trilinear value noise — cheap, one octave per call
            float VNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = Hash13(i);
                float n100 = Hash13(i + float3(1, 0, 0));
                float n010 = Hash13(i + float3(0, 1, 0));
                float n110 = Hash13(i + float3(1, 1, 0));
                float n001 = Hash13(i + float3(0, 0, 1));
                float n101 = Hash13(i + float3(1, 0, 1));
                float n011 = Hash13(i + float3(0, 1, 1));
                float n111 = Hash13(i + float3(1, 1, 1));
                return lerp(
                    lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                    lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y),
                    f.z);
            }

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.normalWS   = TransformObjectToWorldNormal(i.normalOS);
                o.pivotWS    = UNITY_MATRIX_M._m03_m13_m23;
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fogFactor  = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 nrm = normalize(i.normalWS);
                half3 albedo = _BaseColor.rgb;

                // ---- per-instance tint jitter (stable per pivot) ----
                float h = Hash13(floor(i.pivotWS * 17.31 + 0.5));
                half3 tint = half3(0.82 + 0.36 * frac(h * 7.13),
                                   0.82 + 0.36 * frac(h * 13.7),
                                   0.82 + 0.36 * frac(h * 29.3));
                albedo *= lerp(half3(1, 1, 1), tint, _JitterAmt);

                // ---- world-space mottle: two octaves of value noise ----
                float n = VNoise(i.positionWS * _MottleScale) * 0.65
                        + VNoise(i.positionWS * _MottleScale * 3.1 + 11.7) * 0.35;
                albedo *= 1.0 + (n - 0.5) * 2.0 * _MottleAmt;

                // ---- strata bands on world Y, warp-bent so they wander ----
                if (_BandAmt > 0.001)
                {
                    float warp = VNoise(i.positionWS * 0.35) * 1.6;
                    float band = sin((i.positionWS.y + warp) * _BandFreq);
                    albedo *= 1.0 - band * _BandAmt;
                }

                // ---- dust / moss / snow settling on up-facing surfaces ----
                float up = saturate(nrm.y);
                float dustMask = up * up * (0.55 + 0.45 * n); // patchier where mottle is high
                albedo = lerp(albedo, _DustColor.rgb, dustMask * _DustAmt);

                // ---- lighting: main light + SH ambient, shadow-aware ----
                #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                    float4 sc = TransformWorldToShadowCoord(i.positionWS);
                    Light L = GetMainLight(sc);
                #else
                    Light L = GetMainLight();
                #endif
                float ndl = saturate(dot(nrm, L.direction));
                half3 col = albedo * (L.color.rgb * (ndl * L.shadowAttenuation) + SampleSH(nrm));

                col = MixFog(col, i.fogFactor);
                return half4(col, _BaseColor.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
