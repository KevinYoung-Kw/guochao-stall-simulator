Shader "Guochao/TwoToneAlbedo"
{
    Properties
    {
        _MainTex ("Clean Albedo", 2D) = "white" {}
        _Color ("Authored Colour", Color) = (1,1,1,1)
        _LightTint ("Warm amber light", Color) = (1.1,1.02,.86,1)
        _ShadeTint ("Soft slate-blue shadow", Color) = (.75,.78,.84,1)
        _RampThreshold ("Two-tone threshold", Range(0,1)) = .42
        _RampSoftness ("Edge antialiasing", Range(.001,.15)) = .025
        _OutlineColor ("Ink", Color) = (.012,.020,.030,1)
        _OutlineWidth ("Object silhouette metres", Range(0,.04)) = 0
        _EmissionColor ("Authored local emission", Color) = (0,0,0,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200
        Pass
        {
            Name "OBJECT_INK"
            Tags { "LightMode"="Always" }
            Cull Front ZWrite On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _OutlineWidth; fixed4 _OutlineColor;
            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; };
            struct v2f { float4 pos:SV_POSITION; };
            v2f vert(appdata v)
            {
                v2f o;float3 world=mul(unity_ObjectToWorld,v.vertex).xyz;
                world+=UnityObjectToWorldNormal(v.normal)*_OutlineWidth;
                o.pos=mul(UNITY_MATRIX_VP,float4(world,1));return o;
            }
            fixed4 frag(v2f i):SV_Target { clip(_OutlineWidth-.00001);return _OutlineColor; }
            ENDCG
        }
        Pass
        {
            Name "TWO_TONE"
            Tags { "LightMode"="ForwardBase" }
            Cull Back ZWrite On
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"
            sampler2D _MainTex; float4 _MainTex_ST; fixed4 _Color,_LightTint,_ShadeTint,_EmissionColor;
            float _RampThreshold,_RampSoftness;
            struct appdata { float4 vertex:POSITION;float3 normal:NORMAL;float2 uv:TEXCOORD0; };
            struct v2f { float4 pos:SV_POSITION;float2 uv:TEXCOORD0;float3 normal:TEXCOORD1;float3 world:TEXCOORD2;LIGHTING_COORDS(3,4) };
            v2f vert(appdata v)
            {
                v2f o;o.pos=UnityObjectToClipPos(v.vertex);o.uv=TRANSFORM_TEX(v.uv,_MainTex);
                o.normal=UnityObjectToWorldNormal(v.normal);o.world=mul(unity_ObjectToWorld,v.vertex).xyz;
                TRANSFER_VERTEX_TO_FRAGMENT(o);return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                fixed4 albedo=tex2D(_MainTex,i.uv)*_Color;
                float3 n=normalize(i.normal),l=normalize(UnityWorldSpaceLightDir(i.world));
                UNITY_LIGHT_ATTENUATION(atten,i,i.world);
                float lightAmount=saturate(dot(n,l)*.5+.5)*atten;
                float band=smoothstep(_RampThreshold-_RampSoftness,_RampThreshold+_RampSoftness,lightAmount);
                // Two explicit colour regions. No metallic/roughness, specular or normal noise.
                float3 palette=lerp(_ShadeTint.rgb,_LightTint.rgb,band);
                float3 mainColour=lerp(float3(1,1,1),saturate(_LightColor0.rgb),.20);
                return fixed4(albedo.rgb*palette*mainColour+_EmissionColor.rgb,1);
            }
            ENDCG
        }
        Pass
        {
            Name "LOCAL_LIGHTS"
            Tags { "LightMode"="ForwardAdd" }
            Blend One One ZWrite Off Cull Back
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdadd_fullshadows
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"
            sampler2D _MainTex;float4 _MainTex_ST;fixed4 _Color;
            struct appdata { float4 vertex:POSITION;float3 normal:NORMAL;float2 uv:TEXCOORD0; };
            struct v2f { float4 pos:SV_POSITION;float2 uv:TEXCOORD0;float3 normal:TEXCOORD1;float3 world:TEXCOORD2;LIGHTING_COORDS(3,4) };
            v2f vert(appdata v)
            {
                v2f o;o.pos=UnityObjectToClipPos(v.vertex);o.uv=TRANSFORM_TEX(v.uv,_MainTex);
                o.normal=UnityObjectToWorldNormal(v.normal);o.world=mul(unity_ObjectToWorld,v.vertex).xyz;
                TRANSFER_VERTEX_TO_FRAGMENT(o);return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                float3 n=normalize(i.normal),l=normalize(UnityWorldSpaceLightDir(i.world));
                UNITY_LIGHT_ATTENUATION(atten,i,i.world);
                float facing=smoothstep(.12,.18,dot(n,l));
                // A real attenuated point/spot pool, with a restrained two-tone normal response.
                float strength=lerp(.13,.42,_WorldSpaceLightPos0.w);
                float3 colour=tex2D(_MainTex,i.uv).rgb*_Color.rgb;
                return fixed4(colour*_LightColor0.rgb*atten*facing*strength,0);
            }
            ENDCG
        }
        UsePass "Legacy Shaders/VertexLit/SHADOWCASTER"
    }
    Fallback "Diffuse"
}
