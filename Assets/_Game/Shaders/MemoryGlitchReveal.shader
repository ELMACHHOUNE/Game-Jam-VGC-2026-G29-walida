Shader "Photography/MemoryGlitchReveal"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)

        [Header(Memory Reveal and Glitch)]
        _RevealAmount ("Preview Reveal Amount", Range(0, 1)) = 0
        _GlitchIntensity ("Preview Glitch Intensity", Range(0, 1)) = 0
        _ChromaticSplit ("Chromatic Split", Range(0, 0.05)) = 0.014
        _SliceJitter ("Horizontal Slice Jitter", Range(0, 0.08)) = 0.028
        _ScanlineStrength ("Scanline Strength", Range(0, 0.6)) = 0.16
        _NoiseStrength ("Static Noise Strength", Range(0, 0.8)) = 0.32
        _VintageGrain ("1960s Film Grain", Range(0, 0.25)) = 0.055
        _GlitchTint ("Glitch Accent Tint", Color) = (0.95, 0.78, 0.52, 1)
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

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            Name "MemoryGlitchRevealPass"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            // Runtime-driven globals from MemoryGlitchController (syncs main + 360 loop tiles without dirtying the material).
            float _MemoryRevealAmount;
            float _MemoryGlitchIntensity;
            float _MemoryGlitchTime;

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half _RevealAmount;
                half _GlitchIntensity;
                half _ChromaticSplit;
                half _SliceJitter;
                half _ScanlineStrength;
                half _NoiseStrength;
                half _VintageGrain;
                half4 _GlitchTint;
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
                float4 screenPos : TEXCOORD1;
                half4 color : COLOR;
            };

            float Hash11(float p)
            {
                p = frac(p * 0.1031);
                p *= p + 33.33;
                p *= p + p;
                return frac(p);
            }

            float Hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.screenPos = ComputeScreenPos(output.positionCS);
                output.color = input.color * _Color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half reveal = saturate(max(_RevealAmount, (half)_MemoryRevealAmount));
                half glitch = saturate(max(_GlitchIntensity, (half)_MemoryGlitchIntensity));

                if (reveal <= 0.001h && glitch <= 0.001h)
                {
                    return half4(0, 0, 0, 0);
                }

                float t = _MemoryGlitchTime > 0.0001 ? _MemoryGlitchTime : _Time.y;
                float steppedTime = floor(t * 18.0);
                float fastTime = floor(t * 36.0);

                float2 screenUV = input.screenPos.xy / max(0.0001, input.screenPos.w);
                float2 uv = input.uv;

                // Quantized horizontal tear bands
                float bandLarge = floor(screenUV.y * 18.0);
                float bandFine = floor(screenUV.y * 72.0);
                float bandRand = Hash11(bandLarge * 17.3 + steppedTime * 3.17);
                float fineRand = Hash11(bandFine * 31.7 + fastTime * 5.41);

                float tearTrigger = step(1.0 - glitch * 0.65, bandRand);
                float fineTrigger = step(1.0 - glitch * 0.35, fineRand);

                float jitterX = ((bandRand - 0.5) * 2.0 * tearTrigger + (fineRand - 0.5) * 0.7 * fineTrigger) * _SliceJitter * glitch;
                float2 glitchedUV = float2(frac(uv.x + jitterX), saturate(uv.y));

                // Chromatic aberration RGB split
                float split = _ChromaticSplit * glitch * (0.55 + 0.9 * tearTrigger);
                half r = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, float2(frac(glitchedUV.x + split), glitchedUV.y)).r;
                half4 gSample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, glitchedUV);
                half b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, float2(frac(glitchedUV.x - split), glitchedUV.y)).b;

                half3 rgb = half3(r, gSample.g, b) * input.color.rgb;

                // Subtle 1960s film grain + scanlines
                float grain = Hash21(floor(screenUV * 420.0) + fastTime) - 0.5;
                rgb += grain * (_VintageGrain * reveal + _NoiseStrength * glitch * (0.45 + 0.55 * tearTrigger));

                float scanline = sin(screenUV.y * 540.0 + t * 8.0) * 0.5 + 0.5;
                half scanFactor = 1.0h - (scanline * _ScanlineStrength * max(glitch, reveal * 0.35h));
                rgb *= scanFactor;

                // Warm memory flash on active glitch tear strips
                half flashBand = tearTrigger * glitch * 0.35h;
                rgb = lerp(rgb, rgb + _GlitchTint.rgb * 0.35h, flashBand);

                // Alpha: solidifies with reveal, while glitching strips tear the 1960s memory into view
                half stripReveal = saturate(tearTrigger * glitch * 0.85h + fineTrigger * glitch * 0.45h);
                half baseReveal = smoothstep(0.02h, 0.96h, reveal);
                half finalAlpha = saturate(max(baseReveal, stripReveal)) * gSample.a * input.color.a;

                return half4(saturate(rgb), finalAlpha);
            }
            ENDHLSL
        }
    }
}
