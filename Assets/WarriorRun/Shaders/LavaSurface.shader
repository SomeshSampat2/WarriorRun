// Animated molten lava — Voronoi crust plates with glowing cracks.
// Plates are near-black basalt; the gaps between them pulse with heat on
// individual phases so the melt reads as alive. World-space pattern +
// vertex swell keep the flow continuous across tiles. URP, unlit+emissive.
Shader "WarriorRun/LavaSurface"
{
    Properties
    {
        _CellScale   ("Crust Cell Scale", Float) = 0.6
        _CrackWidth  ("Crack Width", Range(0.01, 0.4)) = 0.14
        _FlowSpeed   ("Flow Speed", Float) = 0.055
        _WobbleAmp   ("Wobble Amplitude", Float) = 0.12
        _WobbleSpeed ("Wobble Speed", Float) = 0.45
        _PulseSpeed  ("Pulse Speed", Float) = 0.5
        _SwellAmp    ("Swell Amplitude", Float) = 0.09
        _ShoreGlow   ("Shore Glow", Float) = 0.8
        _Glow        ("Emissive Glow", Float) = 2.4
        _CrustColor  ("Crust Color", Color) = (0.045, 0.028, 0.032, 1)
        _Hot1        ("Hot Deep", Color) = (0.75, 0.08, 0.015, 1)
        _Hot2        ("Hot Orange", Color) = (1.0, 0.38, 0.03, 1)
        _Hot3        ("Hot Core", Color) = (1.0, 0.85, 0.30, 1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _CellScale, _CrackWidth, _FlowSpeed, _WobbleAmp, _WobbleSpeed;
                float _PulseSpeed, _SwellAmp, _ShoreGlow, _Glow;
                float4 _CrustColor, _Hot1, _Hot2, _Hot3;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv         : TEXCOORD1;
                float  fogFactor  : TEXCOORD2;
            };

            float2 Hash2(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453);
            }

            // F2-F1 edge distance + random per-cell value.
            float VoronoiEdge(float2 uv, out float cellRnd)
            {
                float2 g = floor(uv);
                float2 f = frac(uv);
                float f1 = 8.0, f2 = 8.0;
                float2 win = g;
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    float2 l = float2(x, y);
                    float2 r = l + Hash2(g + l) - f;
                    float d = dot(r, r);
                    if (d < f1) { f2 = f1; f1 = d; win = g + l; }
                    else if (d < f2) { f2 = d; }
                }
                cellRnd = Hash2(win).x;
                return sqrt(f2) - sqrt(f1);
            }

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(i.positionOS.xyz);
                float t = _Time.y;
                // slow molten heave — the whole surface breathes
                posWS.y += ( sin(posWS.x * 0.55 + t * 0.85)
                           + sin(posWS.z * 0.42 - t * 0.60)
                           + sin((posWS.x + posWS.z) * 0.21 + t * 0.30) ) * _SwellAmp;
                o.positionWS = posWS;
                o.positionCS = TransformWorldToHClip(posWS);
                o.uv = i.uv;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                // world-space pattern → continuous flow across tile seams
                float2 uv = i.positionWS.xz * _CellScale;
                uv.y += t * _FlowSpeed;
                float w = t * _WobbleSpeed;
                uv += float2(sin(uv.y * 1.35 + w), cos(uv.x * 1.15 - w * 0.85)) * _WobbleAmp;

                float cellRnd;
                float edge = VoronoiEdge(uv, cellRnd);
                float crack = 1.0 - smoothstep(0.0, _CrackWidth, edge);

                // fine secondary veins for detail
                float rnd2;
                float veins = 1.0 - smoothstep(0.0, _CrackWidth * 0.55, VoronoiEdge(uv * 2.6 + 17.31, rnd2));

                // every plate breathes on its own phase — cracks pump like a heartbeat
                float pulse = 0.62 + 0.38 * sin(t * _PulseSpeed * (0.5 + cellRnd * 1.6) + cellRnd * 37.0);
                float heat = crack * (0.85 + 0.35 * pulse) + veins * 0.30 * pulse;

                // molten shimmer peeking between plates — never fully dark
                heat += (0.14 + 0.10 * sin(t * 0.5 + uv.x * 0.7 + uv.y * 0.45)) * (1.0 - crack * 0.4);

                // hotter where the melt meets the ledge/rock shores
                float shore = smoothstep(0.34, 0.5, abs(i.uv.x - 0.5));
                heat += shore * _ShoreGlow * (0.8 + 0.2 * sin(t * 1.7 + uv.y * 2.0));

                half3 hot = heat < 0.55 ? lerp(_Hot1.rgb, _Hot2.rgb, saturate(heat * 1.82))
                                      : lerp(_Hot2.rgb, _Hot3.rgb, saturate((heat - 0.55) * 1.82));
                half3 col = lerp(_CrustColor.rgb, hot * _Glow, saturate(heat));
                col = MixFog(col, i.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
