Shader "MissionBit/DancingCrowd"
{
    // Cheap GPU "dance" for dense crowds on Quest / OpenXR.
    // Vertex sway + bob only — no skeletal Animator cost.
    // Enable GPU Instancing on the material for best batching.
    Properties
    {
        [MainTexture] _BaseMap ("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor ("Color", Color) = (1,1,1,1)

        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        [Toggle(_ALPHATEST_ON)] _AlphaClip ("Alpha Clip", Float) = 0

        [Header(Dance)]
        _DanceSpeed ("Dance Speed", Range(0, 8)) = 2.2
        _SwayAmount ("Sway Amount", Range(0, 0.35)) = 0.08
        _BobAmount ("Bob Amount", Range(0, 0.25)) = 0.05
        _TwistAmount ("Twist Amount", Range(0, 0.5)) = 0.12
        _HeightMaskPower ("Height Mask Power", Range(0.5, 4)) = 1.6
        _PhaseFromPosition ("Phase From World XZ", Range(0, 2)) = 1.0
        _RandomSeed ("Per-Object Seed", Float) = 0

        [Header(Shading)]
        _ShadeColor ("Shade Tint", Color) = (0.75, 0.78, 0.85, 1)
        _ShadeMin ("Shade Min", Range(0,1)) = 0.55

        [Header(Render)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [Toggle] _ZWrite ("ZWrite", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]
            ZWrite [_ZWrite]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma shader_feature_local _ALPHATEST_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Cutoff;
                half _DanceSpeed;
                half _SwayAmount;
                half _BobAmount;
                half _TwistAmount;
                half _HeightMaskPower;
                half _PhaseFromPosition;
                half _RandomSeed;
                half4 _ShadeColor;
                half _ShadeMin;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float fogFactor   : TEXCOORD1;
                half shade        : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float Hash11(float p)
            {
                p = frac(p * 0.1031);
                p *= p + 33.33;
                p *= p + p;
                return frac(p);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 posOS = v.positionOS.xyz;

                // Height mask: feet stay planted, torso/head dance.
                float heightMask = saturate(pow(saturate(posOS.y * 0.55), _HeightMaskPower));

                float3 worldPos = TransformObjectToWorld(posOS);
                float phase =
                    _RandomSeed
                    + _PhaseFromPosition * dot(worldPos.xz, float2(0.37, 0.19))
                    + Hash11(worldPos.x * 12.9898 + worldPos.z * 78.233) * 6.2831853;

                float t = _Time.y * _DanceSpeed + phase;
                float sway = sin(t) * _SwayAmount;
                float bob = abs(sin(t * 2.0)) * _BobAmount;
                float twist = sin(t * 0.85 + 1.7) * _TwistAmount * heightMask;

                // Lateral sway + vertical bob, weighted by height.
                posOS.x += sway * heightMask;
                posOS.z += sin(t * 0.7 + 0.4) * (_SwayAmount * 0.55) * heightMask;
                posOS.y += bob * heightMask;

                // Cheap yaw twist around object Y (no matrix rebuild — 2D rotate XZ).
                float cs = cos(twist);
                float sn = sin(twist);
                float px = posOS.x;
                float pz = posOS.z;
                posOS.x = px * cs - pz * sn;
                posOS.z = px * sn + pz * cs;

                VertexPositionInputs posInputs = GetVertexPositionInputs(posOS);
                o.positionCS = posInputs.positionCS;
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(posInputs.positionCS.z);

                // Fake hemispheric shade from object normal (no real lighting cost).
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.shade = lerp(_ShadeMin, 1.0, saturate(normalWS.y * 0.5 + 0.5));

                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;

                #ifdef _ALPHATEST_ON
                clip(albedo.a - _Cutoff);
                #endif

                half3 color = albedo.rgb * i.shade * _ShadeColor.rgb;
                color = MixFog(color, i.fogFactor);
                return half4(color, albedo.a);
            }
            ENDHLSL
        }

        // Shadow caster kept minimal so optional shadows still work without custom lighting.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ALPHATEST_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Cutoff;
                half _DanceSpeed;
                half _SwayAmount;
                half _BobAmount;
                half _TwistAmount;
                half _HeightMaskPower;
                half _PhaseFromPosition;
                half _RandomSeed;
                half4 _ShadeColor;
                half _ShadeMin;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float Hash11(float p)
            {
                p = frac(p * 0.1031);
                p *= p + 33.33;
                p *= p + p;
                return frac(p);
            }

            Varyings ShadowVert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                float3 posOS = v.positionOS.xyz;
                float heightMask = saturate(pow(saturate(posOS.y * 0.55), _HeightMaskPower));
                float3 worldPos = TransformObjectToWorld(posOS);
                float phase =
                    _RandomSeed
                    + _PhaseFromPosition * dot(worldPos.xz, float2(0.37, 0.19))
                    + Hash11(worldPos.x * 12.9898 + worldPos.z * 78.233) * 6.2831853;
                float t = _Time.y * _DanceSpeed + phase;
                float sway = sin(t) * _SwayAmount;
                float bob = abs(sin(t * 2.0)) * _BobAmount;
                float twist = sin(t * 0.85 + 1.7) * _TwistAmount * heightMask;
                posOS.x += sway * heightMask;
                posOS.z += sin(t * 0.7 + 0.4) * (_SwayAmount * 0.55) * heightMask;
                posOS.y += bob * heightMask;
                float cs = cos(twist);
                float sn = sin(twist);
                float px = posOS.x;
                float pz = posOS.z;
                posOS.x = px * cs - pz * sn;
                posOS.z = px * sn + pz * cs;

                o.positionCS = TransformObjectToHClip(posOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 ShadowFrag(Varyings i) : SV_Target
            {
                #ifdef _ALPHATEST_ON
                half a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * _BaseColor.a;
                clip(a - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
