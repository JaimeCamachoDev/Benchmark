Shader "Hidden/VFX/Lluvia Salpicaduras"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Effect ("Effect Amount", Range(0, 1)) = 1
        _Speed ("Fall Speed", Range(-3, 3)) = 0.6
        _NormalStrength ("Distortion Strength", Range(0, 0.5)) = 0.12
        _StaticUVScale ("Static Droplets Tiling", Range(1, 60)) = 24
        _RollingUVScale ("Rolling Droplets Tiling", Range(1, 60)) = 16
        _StaticDensity ("Static Density", Range(0, 1)) = 0.18
        _RollingDensity ("Rolling Density", Range(0, 1)) = 0.5
        _RandomSeed ("Random Seed", Float) = 1.0
        _EdgeHighlight ("Edge Highlight", Range(0, 1)) = 0.18
        _EyeSeparation ("Eye Separation (Stereo Parallax)", Range(-1, 1)) = 0.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend Off

        Pass
        {
            Name "LluviaSalpicaduras"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Lluvia Salpicaduras.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Effect;
                float _Speed;
                float _NormalStrength;
                float _StaticUVScale;
                float _RollingUVScale;
                float _StaticDensity;
                float _RollingDensity;
                float _RandomSeed;
                float _EdgeHighlight;
                float _EyeSeparation;
            CBUFFER_END

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                float2 localUV = float2((uv.x - 0.5) * aspect, uv.y - 0.5);
                float time = _Time.y * _Speed;

                // Artificial parallax: shift where we evaluate the drop pattern (not the
                // background sample) per eye, so the rain layer itself carries adjustable
                // stereo depth instead of being perfectly flat/screen-locked in both eyes.
                float eyeSign = 0.0;
                #if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
                    eyeSign = (unity_StereoEyeIndex == 0) ? -1.0 : 1.0;
                #endif
                float2 patternUV = localUV + float2(eyeSign * _EyeSeparation * 0.5, 0.0);

                float4 drop = RaindropField(patternUV, _StaticUVScale, _RollingUVScale, time,
                    _StaticDensity, _RollingDensity, _RandomSeed);

                float mask = saturate(drop.x);
                float2 normal = drop.yz;
                float rim = drop.w;

                float2 sampleUV = uv + normal * _NormalStrength;

                half3 original = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb;
                half3 refracted = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, sampleUV, 0).rgb;

                half3 tinted = refracted * _Color.rgb;
                half3 dropColor = tinted + rim * _EdgeHighlight;

                half3 finalColor = lerp(original, dropColor, mask * _Effect);
                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }
    }
}
