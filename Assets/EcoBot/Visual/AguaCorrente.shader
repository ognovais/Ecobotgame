Shader "EcoBot/AguaCorrente"
{
    Properties
    {
        _ShallowColor ("Agua rasa", Color) = (0.15,0.62,0.59,0.72)
        _DeepColor ("Agua profunda", Color) = (0.035,0.28,0.39,0.92)
        _FoamColor ("Espuma", Color) = (0.8,0.94,0.9,0.8)
        _FlowSpeed ("Correnteza", Float) = 0.5
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Name "Agua"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor, _DeepColor, _FoamColor;
                float _FlowSpeed;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; half4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half edge:TEXCOORD1; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                float t = _Time.y * _FlowSpeed;
                world.y += (sin(world.x * 2.2 - t * 3) + cos(world.z * 3.5 + world.x - t * 2)) * 0.018;
                output.positionCS = TransformWorldToHClip(world);
                output.positionWS = world;
                output.edge = input.color.a;
                return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                float t = _Time.y * _FlowSpeed;
                float2 p = input.positionWS.xz;
                float a = p.x * 2.2 - t * 3;
                float b = p.y * 3.5 + p.x - t * 2;
                float3 normal = normalize(float3(-0.07*cos(a)-0.03*cos(b), 1, 0.09*sin(b)));
                float3 view = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                Light sun = GetMainLight();
                half fresnel = pow(1-saturate(dot(normal,view)),4);
                half3 color = lerp(_ShallowColor.rgb,_DeepColor.rgb,saturate(input.edge));
                color = lerp(color,half3(0.52,0.74,0.81),fresnel*0.65);
                half highlight = pow(saturate(dot(normal,normalize(sun.direction+view))),100);
                color += sun.color * highlight * 0.6;
                half ripple = pow(saturate(sin(a*1.8+sin(b))*0.5+0.5),18);
                color += ripple * half3(0.08,0.15,0.14);
                half shore = (1-smoothstep(0.02,0.22,input.edge))*(0.5+0.5*sin(p.x*5-t*4));
                color = lerp(color,_FoamColor.rgb,shore*0.7);
                half alpha = lerp(_ShallowColor.a,_DeepColor.a,input.edge);
                return half4(color, alpha*smoothstep(0,0.07,input.edge));
            }
            ENDHLSL
        }
    }
}
