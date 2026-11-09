// Draws a dark line around a model: the back of the mesh, pushed outward a little, behind the model itself.
// A Render Objects feature on the URP renderer draws everything on the chosen layers again with this material.
Shader "CargoClash/Outline"
{
    Properties
    {
        _Color ("Color", Color) = (0.1, 0.08, 0.09, 1)
        _Width ("Width (fraction of screen height)", Float) = 0.0026
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Outline"
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Width;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);

                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float2 normalCS = TransformWorldToHClipDir(normalWS).xy;
                float aspect = _ScreenParams.y / _ScreenParams.x;
                float2 direction = normalize(normalCS + 0.00001);
                direction.x *= aspect;
                // Twice the width, because clip space runs from -1 to 1 across the screen.
                output.positionCS.xy += direction * _Width * 2.0 * output.positionCS.w;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return _Color;
            }
            ENDHLSL
        }
    }
}
