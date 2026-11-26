Shader "Medina/CardboardDioramaLit"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _Color ("Color", Color) = (1, 1, 1, 1)

        [Header(Moroccan Sunlight Shading)]
        _SunDirection ("Sun Direction", Vector) = (0.56, 0.72, -0.40, 0)
        _SunColor ("Sun Highlight Tint", Color) = (1.07, 1.03, 0.95, 1)
        _MidColor ("Midtone Tint", Color) = (0.93, 0.92, 0.91, 1)
        _ShadowColor ("Shadow Side Tint", Color) = (0.58, 0.64, 0.76, 1)
        _RoofBoost ("Roof Sun Boost", Range(0.85, 1.25)) = 1.05

        [Header(Grounding AO and Paper Relief)]
        _GroundAOStrength ("Ground Contact AO", Range(0, 0.6)) = 0.28
        _GroundAOHeight ("Ground AO Height", Range(0.1, 4.0)) = 1.35
        _ReliefStrength ("Paper & Masonry Relief", Range(0, 0.8)) = 0.26
        _ReliefSpread ("Relief Sample Spread", Range(0.5, 3.0)) = 1.35
        _UseVertexColorRGB ("Use RGB Vertex Color", Float) = 0

        [Header(Atmospheric Depth Haze)]
        _HazeColor ("Coastal Haze Color", Color) = (0.78, 0.85, 0.92, 1)
        _HazeStart ("Haze Start Distance", Float) = 60.0
        _HazeEnd ("Haze End Distance", Float) = 230.0
        _HazeStrength ("Haze Max Strength", Range(0, 0.45)) = 0.14

        [HideInInspector] _Surface ("Surface", Float) = 0
        [HideInInspector] _Blend ("Blend", Float) = 0
        [HideInInspector] _SrcBlend ("SrcBlend", Float) = 1
        [HideInInspector] _DstBlend ("DstBlend", Float) = 0
        [HideInInspector] _ZWrite ("ZWrite", Float) = 1
        [HideInInspector] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "CardboardDioramaForward"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            float4 _BaseMap_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _Color;
                float4 _SunDirection;
                half4 _SunColor;
                half4 _MidColor;
                half4 _ShadowColor;
                half _RoofBoost;
                half _GroundAOStrength;
                half _GroundAOHeight;
                half _ReliefStrength;
                half _ReliefSpread;
                half _UseVertexColorRGB;
                half4 _HazeColor;
                float _HazeStart;
                float _HazeEnd;
                half _HazeStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                half4 color : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.color = input.color;
                return output;
            }

            half3 SampleLuma(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half4 tint = _BaseColor;

                float3 n = normalize(input.normalWS);
                float3 sunDir = normalize(_SunDirection.xyz);
                float ndl = dot(n, sunDir);

                half sunWrap = saturate(ndl * 0.55h + 0.45h);
                half3 litTone = lerp(_ShadowColor.rgb, _MidColor.rgb, saturate(sunWrap * 1.45h));
                litTone = lerp(litTone, _SunColor.rgb, saturate((sunWrap - 0.55h) * 2.1h));

                half upFactor = saturate(n.y);
                litTone *= lerp(1.0h, _RoofBoost, upFactor);

                float2 du = float2(_BaseMap_TexelSize.x * _ReliefSpread, 0.0);
                float2 dv = float2(0.0, _BaseMap_TexelSize.y * _ReliefSpread);
                half lCenter = dot(albedo.rgb, half3(0.299h, 0.587h, 0.114h));
                half lRight = dot(SampleLuma(input.uv + du), half3(0.299h, 0.587h, 0.114h));
                half lUp = dot(SampleLuma(input.uv + dv), half3(0.299h, 0.587h, 0.114h));
                half relief = ((lCenter - lRight) * 0.65h + (lCenter - lUp) * 0.85h) * _ReliefStrength;
                half reliefFactor = clamp(1.0h + relief, 0.84h, 1.14h);

                half heightAO = lerp(1.0h - _GroundAOStrength, 1.0h, saturate(input.positionWS.y / max(0.1h, _GroundAOHeight)));
                half vertAO = input.color.r > 0.001h ? input.color.r : 1.0h;
                half3 vertMod = _UseVertexColorRGB > 0.5h ? input.color.rgb : half3(vertAO, vertAO, vertAO);

                half3 finalRgb = albedo.rgb * tint.rgb * litTone * reliefFactor * heightAO * vertMod;

                float camDist = distance(_WorldSpaceCameraPos.xyz, input.positionWS);
                half hazeFactor = saturate((camDist - _HazeStart) / max(1.0, _HazeEnd - _HazeStart)) * _HazeStrength;
                finalRgb = lerp(finalRgb, _HazeColor.rgb, hazeFactor);

                return half4(finalRgb, albedo.a * tint.a);
            }
            ENDHLSL
        }
    }
}
