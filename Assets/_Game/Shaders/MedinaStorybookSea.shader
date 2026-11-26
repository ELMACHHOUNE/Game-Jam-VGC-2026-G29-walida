Shader "Medina/StorybookSea"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sea Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)

        [Header(Tiling and Drift)]
        _UVScale ("UV Scale (XY)", Vector) = (1, 1, 0, 0)
        _ScrollSpeed ("Current Drift Speed (XY)", Vector) = (0, 0, 0, 0)
        _ClampUV ("Clamp UV (1=Cutout Sprite, 0=Tiled)", Range(0, 1)) = 1

        [Header(Wave Motion)]
        _WaveAmplitude ("Wave Ripple Amplitude (UV XY)", Vector) = (0.0085, 0.0022, 0, 0)
        _WaveFrequency ("Wave World Frequency (XZ)", Vector) = (0.14, 0.24, 0, 0)
        _WaveSpeed ("Wave Speed", Float) = 1.25
        _WavePhase ("Wave Phase Offset", Float) = 0.0

        [Header(Storybook Sunlight Shimmer)]
        _ShimmerColor ("Shimmer Highlight Tint", Color) = (0.86, 0.94, 0.99, 1)
        _ShimmerStrength ("Sunlit Shimmer Strength", Range(0, 0.35)) = 0.085
        _ShimmerScale ("Shimmer World Scale", Float) = 0.11
        _ShimmerSpeed ("Shimmer Speed", Float) = 0.75

        [Header(Atmospheric Depth Haze)]
        _HazeColor ("Coastal Haze Color", Color) = (0.78, 0.85, 0.92, 1)
        _HazeStart ("Haze Start Distance", Float) = 60.0
        _HazeEnd ("Haze End Distance", Float) = 230.0
        _HazeStrength ("Haze Max Strength", Range(0, 0.45)) = 0.14
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            Name "StorybookSeaPass"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                float4 _UVScale;
                float4 _ScrollSpeed;
                half _ClampUV;
                float4 _WaveAmplitude;
                float4 _WaveFrequency;
                float _WaveSpeed;
                float _WavePhase;
                half4 _ShimmerColor;
                half _ShimmerStrength;
                float _ShimmerScale;
                float _ShimmerSpeed;
                half4 _HazeColor;
                float _HazeStart;
                float _HazeEnd;
                half _HazeStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half4 color : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color * _Color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float t = _Time.y * _WaveSpeed + _WavePhase;
                float2 wp = input.positionWS.xz;

                // Dual-harmonic world-space ocean wave ripples so painted white wave crests gently roll and breathe
                float swellPrimary = sin(wp.y * _WaveFrequency.y + wp.x * _WaveFrequency.x * 0.65 + t);
                float swellSecondary = sin(wp.y * _WaveFrequency.y * 1.85 - wp.x * _WaveFrequency.x * 1.15 - t * 0.78);
                float crossRipple = cos(wp.x * _WaveFrequency.x * 1.45 + wp.y * _WaveFrequency.y * 0.55 + t * 0.92);

                float2 waveOffset;
                waveOffset.x = (swellPrimary * 0.68 + swellSecondary * 0.32) * _WaveAmplitude.x;
                waveOffset.y = (crossRipple * 0.65 + swellSecondary * 0.35) * _WaveAmplitude.y;

                float2 baseUV = input.uv * _UVScale.xy + _ScrollSpeed.xy * _Time.y;
                float2 animatedUV = baseUV + waveOffset;

                // Cutout sprite mode clamps cleanly inside [0.002, 0.998]; tiled mode relies on hardware Repeat wrap (no frac() mipmap seam)
                float2 finalUV = lerp(animatedUV, clamp(animatedUV, 0.002, 0.998), _ClampUV);

                half4 texSample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, finalUV);
                half3 rgb = texSample.rgb * input.color.rgb;

                // Soft sunlit sheen drifting diagonally across the blue paper water
                float st = _Time.y * _ShimmerSpeed + _WavePhase * 0.7;
                float s1 = sin((wp.x * 0.75 + wp.y * 0.45) * _ShimmerScale + st);
                float s2 = sin((wp.x * 0.35 - wp.y * 0.85) * _ShimmerScale * 1.4 - st * 0.65);
                half shimmerBand = smoothstep(0.35h, 0.95h, (half)(s1 * 0.55 + s2 * 0.45));
                half crestLift = saturate((half)(swellPrimary * 0.5 + 0.5)) * 0.35h + shimmerBand * 0.65h;
                rgb = lerp(rgb, rgb * 0.82h + _ShimmerColor.rgb * 0.32h, crestLift * _ShimmerStrength);

                // Match coastal depth haze from Medina/CardboardDioramaLit
                float camDist = distance(_WorldSpaceCameraPos.xyz, input.positionWS);
                half hazeFactor = saturate((camDist - _HazeStart) / max(1.0, _HazeEnd - _HazeStart)) * _HazeStrength;
                rgb = lerp(rgb, _HazeColor.rgb, hazeFactor);

                return half4(rgb, texSample.a * input.color.a);
            }
            ENDHLSL
        }
    }
}
