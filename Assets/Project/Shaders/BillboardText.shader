Shader "Simulation/BillboardText"
{
    Properties
    {
        _MainTex ("Font Texture", 2D) = "white" {}
        _Color ("Text Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Lighting Off
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "BillboardText"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;

                float3 worldOrigin = UNITY_MATRIX_M._m03_m13_m23;
                float3 scale = float3(
                    length(UNITY_MATRIX_M._m00_m10_m20),
                    length(UNITY_MATRIX_M._m01_m11_m21),
                    length(UNITY_MATRIX_M._m02_m12_m22)
                );

                float3 forward = worldOrigin - _WorldSpaceCameraPos;
                float forwardLenSq = dot(forward, forward);
                forward = forwardLenSq > 1e-8 ? forward * rsqrt(forwardLenSq) : float3(0.0, 0.0, 1.0);

                float3 camUp = UNITY_MATRIX_V[1].xyz;
                float3 right = cross(camUp, forward);
                float rightLenSq = dot(right, right);
                if (rightLenSq < 1e-6)
                {
                    right = UNITY_MATRIX_V[0].xyz;
                    rightLenSq = dot(right, right);
                }
                right = rightLenSq > 1e-8 ? right * rsqrt(rightLenSq) : float3(1.0, 0.0, 0.0);
                float3 up = normalize(cross(forward, right));

                float3 scaledOS = input.positionOS.xyz * scale;
                float3 worldPos = worldOrigin
                    + right * scaledOS.x
                    + up * scaledOS.y
                    + forward * scaledOS.z;

                output.positionCS = TransformWorldToHClip(worldPos);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color * _Color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                half4 col = input.color;
                col.a *= alpha;
                return col;
            }
            ENDHLSL
        }
    }
}
