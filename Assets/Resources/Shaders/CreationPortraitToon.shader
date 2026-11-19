Shader "Guochao/CreationPortraitToon"
{
    Properties
    {
        _MainTex ("Clean Albedo", 2D) = "white" {}
        _Color ("Authored Colour", Color) = (1,1,1,1)
        _LightTint ("Warm amber light", Color) = (1.1,1.02,.86,1)
        _ShadeTint ("Soft slate-blue shadow", Color) = (.75,.78,.84,1)
        _RampThreshold ("Two-tone threshold", Range(0,1)) = .42
        _RampSoftness ("Edge antialiasing", Range(.001,.15)) = .025
        _EmissionColor ("Authored local emission", Color) = (0,0,0,1)
        _ApronNavy ("Menu apron navy", Color) = (.125,.204,.290,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200
        // Menu portrait only: no OBJECT_INK silhouette pass, so the clone
        // never picks up the 4–5px gameplay outline in the creation panel.
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
            sampler2D _MainTex; float4 _MainTex_ST; fixed4 _Color,_LightTint,_ShadeTint,_EmissionColor,_ApronNavy;
            float _RampThreshold,_RampSoftness;
            struct appdata { float4 vertex:POSITION;float3 normal:NORMAL;float2 uv:TEXCOORD0; };
            struct v2f { float4 pos:SV_POSITION;float2 uv:TEXCOORD0;float3 normal:TEXCOORD1;float3 world:TEXCOORD2;LIGHTING_COORDS(3,4) };
            v2f vert(appdata v)
            {
                v2f o;o.pos=UnityObjectToClipPos(v.vertex);o.uv=TRANSFORM_TEX(v.uv,_MainTex);
                o.normal=UnityObjectToWorldNormal(v.normal);o.world=mul(unity_ObjectToWorld,v.vertex).xyz;
                TRANSFER_VERTEX_TO_FRAGMENT(o);return o;
            }
            // Soft mask that only fires on saturated teal apron cloth.
            // Chromaticity is computed after normalizing by the channel max,
            // so classification is independent of luminance (Linear Color Space
            // decoding darkens values but preserves normalized ratios).
            // Both green and blue clearly above red. Skin (r>g), brown hair,
            // white shirt and grey shoes fall outside the mask and stay put.
            float TealMask(float3 c)
            {
                float mx=max(c.r,max(c.g,c.b));
                float safe=max(mx,.0001);
                float3 normalized=c/safe;
                float tealness=min(normalized.g-normalized.r,normalized.b-normalized.r);
                float saturation=(mx-min(c.r,min(c.g,c.b)))/safe;
                return smoothstep(.12,.35,tealness)*smoothstep(.18,.40,saturation);
            }
            fixed4 frag(v2f i):SV_Target
            {
                fixed3 tex=tex2D(_MainTex,i.uv).rgb;
                float mask=TealMask(tex);
                // Recolour by hue replacement, not a flat fill: scale the navy
                // by the texel's own luminance so cloth folds and shading stay.
                float lum=dot(tex,float3(.299,.587,.114));
                float navyLum=max(dot(_ApronNavy.rgb,float3(.299,.587,.114)),.001);
                float3 navy=_ApronNavy.rgb*(lum/navyLum);
                fixed3 albedo=lerp(tex,saturate(navy),mask)*_Color.rgb;
                float3 n=normalize(i.normal),l=normalize(UnityWorldSpaceLightDir(i.world));
                UNITY_LIGHT_ATTENUATION(atten,i,i.world);
                float lightAmount=saturate(dot(n,l)*.5+.5)*atten;
                float band=smoothstep(_RampThreshold-_RampSoftness,_RampThreshold+_RampSoftness,lightAmount);
                float3 palette=lerp(_ShadeTint.rgb,_LightTint.rgb,band);
                float3 mainColour=lerp(float3(1,1,1),saturate(_LightColor0.rgb),.20);
                return fixed4(albedo*palette*mainColour+_EmissionColor.rgb,1);
            }
            ENDCG
        }
        UsePass "Legacy Shaders/VertexLit/SHADOWCASTER"
    }
    Fallback "Diffuse"
}
