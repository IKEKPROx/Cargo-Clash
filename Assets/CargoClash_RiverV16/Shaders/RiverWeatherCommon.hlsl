#ifndef CARGO_RIVER_WEATHER_INCLUDED
#define CARGO_RIVER_WEATHER_INCLUDED
float RiverHash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
float RainRing(float2 world, float t)
{
    float2 p=world*1.8, cell=floor(p), local=frac(p)-.5;
    float phase=frac(t*.85+RiverHash(cell)*8.0);
    float radius=phase*.48;
    float ring=pow(saturate(1-abs(length(local)-radius)*45),2);
    return ring*(1-phase);
}
float3 RainNormalWS(float2 world,float t,float strength)
{
    float e=.012;
    float dx=RainRing(world+float2(e,0),t)-RainRing(world-float2(e,0),t);
    float dz=RainRing(world+float2(0,e),t)-RainRing(world-float2(0,e),t);
    return normalize(float3(-dx*strength,1,-dz*strength));
}
InputData RiverLightingInput(float3 world,half3 normal,float4 shadow,float4 clip)
{
    InputData data=(InputData)0;
    data.positionWS=world;data.normalWS=normalize(normal);data.viewDirectionWS=GetWorldSpaceNormalizeViewDir(world);
    data.shadowCoord=shadow;
    #if defined(MAIN_LIGHT_CALCULATE_SHADOWS) && !defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
        data.shadowCoord=TransformWorldToShadowCoord(world);
    #endif
    data.fogCoord=ComputeFogFactor(clip.z);data.bakedGI=SampleSH(data.normalWS);
    data.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(clip);data.shadowMask=half4(1,1,1,1);
    return data;
}
#endif
