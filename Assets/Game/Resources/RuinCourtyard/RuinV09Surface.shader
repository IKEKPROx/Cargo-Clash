Shader "CargoClash/RuinV09/EnvironmentSurface"
{
    Properties
    {
        [MainColor] _BaseColor("Base color",Color)=(1,1,1,1)
        [MainTexture] _BaseMap("Color texture",2D)="white"{}
        _UseVertexColor("Use authored terrain / nature color",Float)=0
        _Smoothness("Dry smoothness",Range(0,1))=.35
        _Metallic("Metallic",Range(0,1))=0
        _Wetness("Wetness",Range(0,1))=0
        _LeafSoftness("Soft foliage lighting",Float)=0
        _RainStrength("Rain rings on puddles",Range(0,1))=0
        _Grain("Wood grain",Float)=0
        _DetailScale("Surface detail scale",Float)=12
        _DetailStrength("Surface relief",Float)=0.015
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
        #include "RuinWeatherCommon.hlsl"
        TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
        // All passes must share this exact layout for persistent SRP batching.
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;float4 _BaseMap_ST;
            float _UseVertexColor,_Smoothness,_Metallic,_Wetness,_RainStrength,_Grain,_PreviewTime,_Cull,_Cutoff,_DetailScale,_DetailStrength,_LeafSoftness;
        CBUFFER_END
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags {"LightMode"="UniversalForward"}
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma multi_compile_instancing
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            struct A {float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;half4 color:COLOR;UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct V {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;float2 uv:TEXCOORD2;half4 color:COLOR;float4 shadow:TEXCOORD3;};
            V vert(A a){UNITY_SETUP_INSTANCE_ID(a);V o;VertexPositionInputs p=GetVertexPositionInputs(a.positionOS.xyz);o.positionCS=p.positionCS;o.positionWS=p.positionWS;o.normalWS=TransformObjectToWorldNormal(a.normalOS);o.uv=TRANSFORM_TEX(a.uv,_BaseMap);o.color=a.color;o.shadow=GetShadowCoord(p);return o;}
            
            float DetailHash(float3 p){p=frac(p*.1031);p+=dot(p,p.yzx+33.33);return frac((p.x+p.y)*p.z);}
            float DetailNoise(float3 p){float3 b=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(lerp(DetailHash(b),DetailHash(b+float3(1,0,0)),f.x),lerp(DetailHash(b+float3(0,1,0)),DetailHash(b+float3(1,1,0)),f.x),f.y),lerp(lerp(DetailHash(b+float3(0,0,1)),DetailHash(b+float3(1,0,1)),f.x),lerp(DetailHash(b+float3(0,1,1)),DetailHash(b+float3(1,1,1)),f.x),f.y),f.z);}
            half4 frag(V i):SV_Target
            {
                float t=_PreviewTime>=0?_PreviewTime:_Time.y;
                // URP LitForwardPass: cascade selection must happen per pixel.
                float4 shadowCoordinate=i.shadow;
                #if defined(MAIN_LIGHT_CALCULATE_SHADOWS) && !defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                    shadowCoordinate=TransformWorldToShadowCoord(i.positionWS);
                #endif
                half3 base=lerp(_BaseColor.rgb,i.color.rgb,saturate(_UseVertexColor))*SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb;
                float grain=sin((i.uv.x*47+sin(i.uv.y*1.8)*1.2))*sin(i.uv.x*113+i.uv.y*2);
                base*=lerp(1, .96+.04*grain,_Grain);
                base*=1-_Wetness*.12;
                half3 rain=RainNormalWS(i.positionWS.xz,t,_RainStrength);
                
                float3 p=i.positionWS*max(.1,_DetailScale);float h=DetailNoise(p);float eps=.08;
                float3 grad=float3(DetailNoise(p+float3(eps,0,0))-h,DetailNoise(p+float3(0,eps,0))-h,DetailNoise(p+float3(0,0,eps))-h)/eps;
                float leafRegion=(i.uv.x>.25 && i.uv.x<.75 && i.uv.y<.25)?1:0;
                float3 baseNormal=normalize(i.normalWS);grad-=baseNormal*dot(grad,baseNormal);grad*=1-leafRegion;
                half3 n=normalize(baseNormal-grad*_DetailStrength*_DetailScale*.22+half3(rain.x,0,rain.z));
                base*=1+(h-.5)*min(.045,_DetailStrength*2);
                if(_LeafSoftness>.5 || (i.uv.x>.25 && i.uv.x<.75 && i.uv.y<.25))
                {
                    SurfaceData leafSurface=(SurfaceData)0;
                    leafSurface.albedo=base;leafSurface.alpha=1;leafSurface.metallic=0;
                    leafSurface.specular=.015;leafSurface.smoothness=.08;
                    leafSurface.normalTS=half3(0,0,1);leafSurface.occlusion=1;
                    leafSurface.emission=base*.065;
                    InputData leafInput=RiverLightingInput(i.positionWS,n,shadowCoordinate,i.positionCS);
                    half4 foliage=UniversalFragmentPBR(leafInput,leafSurface);
                    foliage.rgb=MixFog(foliage.rgb,leafInput.fogCoord);
                    return foliage;
                }
                SurfaceData s=(SurfaceData)0;s.albedo=base;s.alpha=1;s.metallic=_Metallic;s.specular=.04;s.smoothness=lerp(_Smoothness,max(_Smoothness,.67),_Wetness);s.normalTS=half3(0,0,1);s.occlusion=1;
                InputData d=RiverLightingInput(i.positionWS,n,shadowCoordinate,i.positionCS);half4 c=UniversalFragmentPBR(d,s);c.rgb=MixFog(c.rgb,d.fogCoord);return c;
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
            #pragma multi_compile_instancing
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection;
            float3 _LightPosition;
            struct ShadowA {float4 positionOS:POSITION;float3 normalOS:NORMAL;UNITY_VERTEX_INPUT_INSTANCE_ID};
            float4 ShadowVert(ShadowA v):SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
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
            #pragma multi_compile_instancing
            #pragma fragment DepthFrag
            struct DepthA {float4 p:POSITION;UNITY_VERTEX_INPUT_INSTANCE_ID};
            float4 DepthVert(DepthA v):SV_POSITION{UNITY_SETUP_INSTANCE_ID(v);return TransformObjectToHClip(v.p.xyz);}
            half DepthFrag(float4 p:SV_POSITION):SV_Target{return p.z;}
            ENDHLSL
        }
    }
}
