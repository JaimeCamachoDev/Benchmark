// Assets/2-Art/2-VFX/EfficientLit/EfficientLit.shader
//
// Material "estandar" eficiente para VR standalone (Quest 3 / Pico 4): mismo
// aspecto PBR metalico que URP Lit, pero con solo 2 texture samples fijos en
// vez de los 3-4 de Lit (BaseMap + MetallicGlossMap + OcclusionMap + NormalMap).
//
// - _BaseMap: RGB = Albedo, A = Smoothness.
// - _DataMap: R = Normal.X, G = Normal.Y, B = AO, A = Metallic.
//   IMPORTANTE: _DataMap debe importarse como textura "Default" (lineal, sin
//   sRGB), NUNCA como "Normal Map" - el importador Normal Map reformatea los
//   canales y rompe este packing custom. La Z de la normal se reconstruye en
//   el shader a partir de X/Y, no se almacena.
//
// Reutiliza los passes ShadowCaster/DepthOnly/DepthNormals/Meta del propio
// Lit de URP (via UsePass) en vez de reescribirlos: esos passes solo miran
// posicion/alpha, no el packing de color, asi que son validos sin cambios y
// evitan duplicar codigo fragil.
Shader "Custom/EfficientLit"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map (RGB=Albedo, A=Smoothness)", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color (tinte)", Color) = (1,1,1,1)
        _DataMap("Data Map (R=NormalX G=NormalY B=AO A=Metallic) - importar como Default/Lineal", 2D) = "bump" {}
        _NormalStrength("Normal Strength", Range(0,2)) = 1.0
        _SmoothnessScale("Smoothness Scale", Range(0,1)) = 1.0
        _MetallicScale("Metallic Scale", Range(0,1)) = 1.0
        _OcclusionStrength("Occlusion Strength", Range(0,1)) = 1.0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull Mode", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5

            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_DataMap); SAMPLER(sampler_DataMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _NormalStrength;
                float _SmoothnessScale;
                float _MetallicScale;
                float _OcclusionStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                float2 lightmapUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float2 uv          : TEXCOORD0;
                DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 1);
                float3 positionWS  : TEXCOORD2;
                float3 normalWS    : TEXCOORD3;
                float4 tangentWS   : TEXCOORD4; // xyz = tangente, w = signo
                half fogFactor     : TEXCOORD5;
                float4 shadowCoord : TEXCOORD6;
                float4 positionCS  : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);

                OUT.positionWS = positionInputs.positionWS;
                OUT.positionCS = positionInputs.positionCS;
                OUT.normalWS = normalInputs.normalWS;

                real sign = IN.tangentOS.w * GetOddNegativeScale();
                OUT.tangentWS = float4(normalInputs.tangentWS.xyz, sign);

                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);

                OUTPUT_LIGHTMAP_UV(IN.lightmapUV, unity_LightmapST, OUT.lightmapUV);
                OUTPUT_SH(OUT.normalWS, OUT.vertexSH);

                OUT.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                OUT.shadowCoord = GetShadowCoord(positionInputs);

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                // ===== Los DOS unicos texture samples de todo el shader =====
                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                half4 dataSample = SAMPLE_TEXTURE2D(_DataMap, sampler_DataMap, IN.uv);

                half3 albedo = baseSample.rgb * _BaseColor.rgb;
                half smoothness = baseSample.a * _SmoothnessScale;
                half metallic = dataSample.a * _MetallicScale;
                half occlusion = lerp(1.0h, dataSample.b, (half)_OcclusionStrength);

                // Reconstruye Z de la normal a partir de X/Y (sin almacenarla).
                half2 normalXY = (dataSample.rg * 2.0h - 1.0h) * (half)_NormalStrength;
                half normalZ = sqrt(saturate(1.0h - dot(normalXY, normalXY)));
                half3 normalTS = half3(normalXY, normalZ);

                float sgn = IN.tangentWS.w;
                float3 bitangentWS = sgn * cross(IN.normalWS.xyz, IN.tangentWS.xyz);
                half3x3 tangentToWorld = half3x3(IN.tangentWS.xyz, bitangentWS, IN.normalWS.xyz);
                half3 normalWS = normalize(TransformTangentToWorld(normalTS, tangentToWorld));

                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                inputData.shadowCoord = IN.shadowCoord;
                inputData.fogCoord = IN.fogFactor;
                inputData.vertexLighting = half3(0, 0, 0);
                inputData.bakedGI = SAMPLE_GI(IN.lightmapUV, IN.vertexSH, normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.metallic = metallic;
                surfaceData.specular = half3(0, 0, 0);
                surfaceData.smoothness = smoothness;
                surfaceData.normalTS = normalTS;
                surfaceData.emission = half3(0, 0, 0);
                surfaceData.occlusion = occlusion;
                surfaceData.alpha = 1.0h;

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return color;
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
        UsePass "Universal Render Pipeline/Lit/Meta"
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
