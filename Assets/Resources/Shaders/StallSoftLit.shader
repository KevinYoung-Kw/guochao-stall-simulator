Shader "Guochao/StallSoftLit" {
 Properties { _MainTex("Albedo",2D)="white"{} _Color("Colour",Color)=(1,1,1,1) _Gloss("Soft shine",Range(0,1))=.15 _EmissionColor("Emission",Color)=(0,0,0,1) }
 SubShader { Tags {"RenderType"="Opaque"} LOD 200
 Pass { Tags {"LightMode"="ForwardBase"}
 CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile_fwdbase
 #include "UnityCG.cginc"
 #include "Lighting.cginc"
 #include "AutoLight.cginc"
 sampler2D _MainTex; float4 _MainTex_ST; fixed4 _Color,_EmissionColor; float _Gloss;
 struct a {float4 vertex:POSITION;float3 normal:NORMAL;float2 uv:TEXCOORD0;};
 struct v {float4 pos:SV_POSITION;float2 uv:TEXCOORD0;float3 normal:TEXCOORD1;float3 world:TEXCOORD2;LIGHTING_COORDS(3,4)};
 v vert(a p){v o;o.pos=UnityObjectToClipPos(p.vertex);o.uv=TRANSFORM_TEX(p.uv,_MainTex);o.normal=UnityObjectToWorldNormal(p.normal);o.world=mul(unity_ObjectToWorld,p.vertex).xyz;TRANSFER_VERTEX_TO_FRAGMENT(o);return o;}
 fixed4 frag(v i):SV_Target {float3 n=normalize(i.normal),l=normalize(UnityWorldSpaceLightDir(i.world));UNITY_LIGHT_ATTENUATION(atten,i,i.world);
 float diffuse=saturate(dot(n,l));float3 base=tex2D(_MainTex,i.uv).rgb*_Color.rgb;
 float3 ambient=lerp(float3(.26,.30,.32),float3(.49,.51,.53),n.y*.5+.5);
 float3 h=normalize(l+normalize(UnityWorldSpaceViewDir(i.world)));float spec=pow(saturate(dot(n,h)),32)*_Gloss*.2*atten;
 return fixed4(base*(ambient+_LightColor0.rgb*diffuse*atten*.72)+spec+_EmissionColor.rgb,1);}
 ENDCG }
 UsePass "Legacy Shaders/VertexLit/SHADOWCASTER"
 } Fallback "Diffuse"
}
