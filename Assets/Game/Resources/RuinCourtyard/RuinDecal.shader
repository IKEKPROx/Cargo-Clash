// Ground paint for the Ruin Courtyard: the road surface, tyre tracks and safety lines.
// Alpha blended over the opaque ground, lit like RuinV09Surface (main light, shadows, fog, Forward+ lights).
// Only RuinCourtyard.Maya.cs and RuinCourtyard.Depot.cs draw with it, on meshes they build themselves, so
// SRP batches of this shader never mix with the imported models' compressed vertex layout.
Shader "CargoClash/RuinDecal"
{
    Properties
    {
        [MainColor] _BaseColor("Tint", Color) = (1,1,1,1)
        [MainTexture] _BaseMap("Paint", 2D) = "white" {}
        _Smoothness("Smoothness", Range(0,1)) = .18
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-400" "IgnoreProjector"="True"}
        Pass
        {
            Name "ForwardLit"
            Tags {"LightMode"="UniversalForward"}
            Blend SrcAlpha OneMinusSrcAlpha, Zero One
            ZWrite Off
            ZTest LEqual
            Offset -2, -2
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "RuinWeatherCommon.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor; float4 _BaseMap_ST; float _Smoothness;
            CBUFFER_END
            struct A {float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; half4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct V {float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; float2 uv:TEXCOORD2; half4 color:COLOR; float fog:TEXCOORD3;};
            V vert(A a)
            {
                UNITY_SETUP_INSTANCE_ID(a);
                V o;
                VertexPositionInputs p = GetVertexPositionInputs(a.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(a.normalOS);
                o.uv = TRANSFORM_TEX(a.uv, _BaseMap);
                o.color = a.color;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                half4 paint = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor * i.color;
                float4 shadow = TransformWorldToShadowCoord(i.positionWS);
                SurfaceData s = (SurfaceData)0;
                s.albedo = paint.rgb; s.alpha = paint.a; s.metallic = 0; s.specular = .04;
                s.smoothness = _Smoothness; s.normalTS = half3(0,0,1); s.occlusion = 1;
                InputData d = RiverLightingInput(i.positionWS, i.normalWS, shadow, i.positionCS, i.fog);
                half4 c = UniversalFragmentPBR(d, s);
                c.rgb = MixFog(c.rgb, d.fogCoord);
                c.a = paint.a;
                return c;
            }
            ENDHLSL
        }
    }
}
