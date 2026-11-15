Shader "CargoClash/RiverV10/WetSurface"
{
    Properties
    {
        [MainColor] _BaseColor("Base color",Color)=(1,1,1,1)
        [MainTexture] _BaseMap("Color texture",2D)="white"{}
        _UseVertexColor("Use authored terrain / nature color",Float)=0
        _Smoothness("Dry smoothness",Range(0,1))=.35
        _Metallic("Metallic",Range(0,1))=0
        _Wetness("Wetness",Range(0,1))=.45
        _RainStrength("Rain rings on puddles",Range(0,1))=0
        _Grain("Wood grain",Float)=0
        _PreviewTime("Preview time, -1 = automatic",Float)=-1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull",Float)=2
        [HideInInspector] _Cutoff("Cutoff",Float)=.5
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "RiverWeatherCommon.hlsl"
        TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
        // All passes must share this exact layout for persistent SRP batching.
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;float4 _BaseMap_ST;
            float _UseVertexColor,_Smoothness,_Metallic,_Wetness,_RainStrength,_Grain,_PreviewTime,_Cull,_Cutoff;
        CBUFFER_END
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags {"LightMode"="UniversalForward"}
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            struct A {float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;half4 color:COLOR;};
            struct V {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;float2 uv:TEXCOORD2;half4 color:COLOR;float4 shadow:TEXCOORD3;};
            V vert(A a){V o;VertexPositionInputs p=GetVertexPositionInputs(a.positionOS.xyz);o.positionCS=p.positionCS;o.positionWS=p.positionWS;o.normalWS=TransformObjectToWorldNormal(a.normalOS);o.uv=TRANSFORM_TEX(a.uv,_BaseMap);o.color=a.color;o.shadow=GetShadowCoord(p);return o;}
            half4 frag(V i):SV_Target
            {
                float t=_PreviewTime>=0?_PreviewTime:_Time.y;
                half3 base=lerp(_BaseColor.rgb,i.color.rgb,saturate(_UseVertexColor))*SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb;
                float grain=sin((i.uv.x*47+sin(i.uv.y*1.8)*1.2))*sin(i.uv.x*113+i.uv.y*2);
                base*=lerp(1, .96+.04*grain,_Grain);
                base*=1-_Wetness*.12;
                half3 rain=RainNormalWS(i.positionWS.xz,t,_RainStrength);
                half3 n=normalize(i.normalWS+half3(rain.x,0,rain.z));
                SurfaceData s=(SurfaceData)0;s.albedo=base;s.alpha=1;s.metallic=_Metallic;s.specular=.04;s.smoothness=lerp(_Smoothness,max(_Smoothness,.67),_Wetness);s.normalTS=half3(0,0,1);s.occlusion=1;
                InputData d=RiverLightingInput(i.positionWS,n,i.shadow,i.positionCS);half4 c=UniversalFragmentPBR(d,s);c.rgb=MixFog(c.rgb,d.fogCoord);return c;
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags {"LightMode"="ShadowCaster"}
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection;
            float3 _LightPosition;
            struct ShadowA {float4 positionOS:POSITION;float3 normalOS:NORMAL;};
            float4 ShadowVert(ShadowA v):SV_POSITION
            {
                float3 p=TransformObjectToWorld(v.positionOS.xyz);
                float3 n=TransformObjectToWorldNormal(v.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirection=normalize(_LightPosition-p);
                #else
                    float3 lightDirection=_LightDirection;
                #endif
                float4 clip=TransformWorldToHClip(ApplyShadowBias(p,n,lightDirection));
                return ApplyShadowClamping(clip);
            }
            half4 ShadowFrag():SV_Target{return 0;}
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags {"LightMode"="DepthOnly"}
            ZWrite On
            ColorMask R
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            float4 DepthVert(float4 p:POSITION):SV_POSITION{return TransformObjectToHClip(p.xyz);}
            half DepthFrag(float4 p:SV_POSITION):SV_Target{return p.z;}
            ENDHLSL
        }
    }
}
