Shader "CargoClash/RiverV16/FlowWater"
{
    Properties
    {
        _DeepColor("Deep water",Color)=(.035,.20,.25,1)
        _ShallowColor("Shallow water",Color)=(.24,.40,.35,1)
        _FoamColor("Bank foam",Color)=(.67,.82,.76,1)
        _FlowNormal("Flow normal (linear RGB)",2D)="bump"{}
        _FlowHeight("Foam breakup",2D)="gray"{}
        _FlowSpeed("UV flow per second",Float)=.25
        _NormalStrength("Water ripples",Range(0,3))=1.15
        _RainStrength("Rain rings",Range(0,1))=.2
        _Smoothness("Smoothness",Range(0,1))=.8
        _DepthFade("Use camera depth",Range(0,1))=1
        _PreviewTime("Preview time, -1 = automatic",Float)=-1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "RiverWeatherCommon.hlsl"
            TEXTURE2D(_FlowNormal);SAMPLER(sampler_FlowNormal);
            TEXTURE2D(_FlowHeight);SAMPLER(sampler_FlowHeight);
            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor,_ShallowColor,_FoamColor;
                float _FlowSpeed,_NormalStrength,_RainStrength,_Smoothness,_DepthFade,_PreviewTime;
            CBUFFER_END
            struct A {float4 positionOS:POSITION;float3 normalOS:NORMAL;float4 tangentOS:TANGENT;float2 uv:TEXCOORD0;half4 color:COLOR;};
            struct V {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;half4 tangentWS:TEXCOORD2;float2 uv:TEXCOORD3;half4 color:COLOR;float4 shadow:TEXCOORD4;};
            V vert(A a)
            {
                V o;VertexPositionInputs p=GetVertexPositionInputs(a.positionOS.xyz);VertexNormalInputs n=GetVertexNormalInputs(a.normalOS,a.tangentOS);
                o.positionCS=p.positionCS;o.positionWS=p.positionWS;o.normalWS=n.normalWS;o.tangentWS=half4(n.tangentWS,a.tangentOS.w*GetOddNegativeScale());
                o.uv=a.uv;o.color=a.color;o.shadow=GetShadowCoord(p);return o;
            }
            half4 frag(V i):SV_Target
            {
                float t=_PreviewTime>=0?_PreviewTime:_Time.y;
                float2 uv1=i.uv*float2(2.4,1)-float2(0,t*_FlowSpeed);
                float2 uv2=i.uv*float2(3.3,1.5)-float2(0,t*_FlowSpeed*.5);
                half3 n1=SAMPLE_TEXTURE2D(_FlowNormal,sampler_FlowNormal,uv1).xyz*2-1;
                half3 n2=SAMPLE_TEXTURE2D(_FlowNormal,sampler_FlowNormal,uv2).xyz*2-1;
                half3 nts=normalize(half3((n1.xy+n2.xy)*_NormalStrength,1));
                half3 bitangent=cross(i.normalWS,i.tangentWS.xyz)*i.tangentWS.w;
                half3 normal=normalize(mul(nts,half3x3(i.tangentWS.xyz,bitangent,i.normalWS)));
                half3 rain=RainNormalWS(i.positionWS.xz,t,_RainStrength);
                normal=normalize(normal+half3(rain.x,0,rain.z));
                float thickness=1;
                if(_DepthFade>.5)
                {
                    float raw=SampleSceneDepth(GetNormalizedScreenSpaceUV(i.positionCS));
                    float eye=LinearEyeDepth(raw,_ZBufferParams);
                    if(unity_OrthoParams.w>.5)
                    {
                        #if UNITY_REVERSED_Z
                            raw=1-raw;
                        #endif
                        eye=lerp(_ProjectionParams.y,_ProjectionParams.z,raw);
                    }
                    thickness=max(0,eye+TransformWorldToView(i.positionWS).z);
                }
                half h=SAMPLE_TEXTURE2D(_FlowHeight,sampler_FlowHeight,uv1).r;
                half bank=max(i.color.r,saturate(1-thickness*7)*_DepthFade);
                half shallow=max(i.color.g,i.color.r*.6);
                half foam=bank*smoothstep(.38,.66,h)*.75;
                SurfaceData s=(SurfaceData)0;s.albedo=lerp(lerp(_DeepColor.rgb,_ShallowColor.rgb,shallow),_FoamColor.rgb,foam);
                s.alpha=lerp(.66,.96,saturate(thickness*2));s.metallic=0;s.specular=.04;s.smoothness=lerp(_Smoothness,.35,foam);s.normalTS=half3(0,0,1);s.occlusion=1;
                InputData data=RiverLightingInput(i.positionWS,normal,i.shadow,i.positionCS);
                half4 c=UniversalFragmentPBR(data,s);c.rgb=MixFog(c.rgb,data.fogCoord);c.a=s.alpha;return c;
            }
            ENDHLSL
        }
    }
}
