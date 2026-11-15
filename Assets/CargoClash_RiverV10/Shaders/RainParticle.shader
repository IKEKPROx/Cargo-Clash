Shader "CargoClash/RiverV10/RainParticle"
{
    Properties { _Tint("Tint",Color)=(.67,.79,.86,.45) _Ring("Ring splash",Float)=0 }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent+10" "RenderType"="Transparent"}
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Tint;float _Ring;
            CBUFFER_END
            struct A {float4 p:POSITION;float2 uv:TEXCOORD0;half4 c:COLOR;};
            struct V {float4 p:SV_POSITION;float2 uv:TEXCOORD0;half4 c:COLOR;};
            V vert(A a){V o;o.p=TransformObjectToHClip(a.p.xyz);o.uv=a.uv;o.c=a.c;return o;}
            half4 frag(V i):SV_Target
            {
                float2 q=i.uv*2-1;float a=pow(saturate(1-abs(q.x)),2)*sin(saturate(i.uv.y)*3.14159);
                if(_Ring>.5)a=saturate(1-abs(length(q)-.78)*13);
                return half4(_Tint.rgb*i.c.rgb,_Tint.a*i.c.a*a);
            }
            ENDHLSL
        }
    }
}
