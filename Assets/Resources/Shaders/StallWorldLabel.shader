Shader "Guochao/WorldLabel" {
 Properties { _MainTex("Font",2D)="white"{} }
 SubShader {Tags {"Queue"="Transparent" "RenderType"="Transparent"} Blend SrcAlpha OneMinusSrcAlpha ZWrite Off ZTest LEqual Cull Off
 Pass {CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "UnityCG.cginc"
 sampler2D _MainTex;
 struct a {float4 vertex:POSITION;float2 uv:TEXCOORD0;fixed4 colour:COLOR;};
 struct v {float4 pos:SV_POSITION;float2 uv:TEXCOORD0;fixed4 colour:COLOR;};
 v vert(a p){v o;o.pos=UnityObjectToClipPos(p.vertex);o.uv=p.uv;o.colour=p.colour;return o;}
 fixed4 frag(v i):SV_Target{return fixed4(i.colour.rgb,i.colour.a*tex2D(_MainTex,i.uv).a);}
 ENDCG}
 }
}
