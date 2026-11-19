Shader "Hidden/Guochao/InkPaper"
{
    Properties { _MainTex ("Scene",2D)="white"{} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex,_CameraDepthNormalsTexture;
            float4 _MainTex_TexelSize;
            float _InkWidth,_PaperAmount;
            fixed4 _InkColour;
            void SampleDN(float2 uv,out float depth,out float3 normal)
            {DecodeDepthNormal(tex2D(_CameraDepthNormalsTexture,uv),depth,normal);}
            float Edge(float2 uv,float2 offset,float depth,float3 normal)
            {
                float d;float3 n;SampleDN(uv+offset,d,n);
                float depthEdge=smoothstep(.09,.16,abs(d-depth)*_ProjectionParams.z);
                float normalEdge=smoothstep(.62,.92,length(n-normal));
                return max(depthEdge,normalEdge);
            }
            fixed4 frag(v2f_img i):SV_Target
            {
                float2 uv=i.uv;float2 duv=uv;
                #if UNITY_UV_STARTS_AT_TOP
                if(_MainTex_TexelSize.y<0)duv.y=1-duv.y;
                #endif
                float depth;float3 normal;SampleDN(duv,depth,normal);
                float2 px=abs(_MainTex_TexelSize.xy)*_InkWidth;
                float edge=max(max(Edge(duv,float2(px.x,0),depth,normal),Edge(duv,float2(-px.x,0),depth,normal)),
                               max(Edge(duv,float2(0,px.y),depth,normal),Edge(duv,float2(0,-px.y),depth,normal)));
                float3 colour=tex2D(_MainTex,uv).rgb;
                // Ink can only darken: replacing deep shadows with a brighter fixed hue creates grey wireframes.
                colour=lerp(colour,min(colour,_InkColour.rgb),edge*.28);
                // Stable in screen coordinates: subtle paper grain, never temporal sparkle.
                float2 cell=floor(uv/abs(_MainTex_TexelSize.xy));
                float grain=frac(sin(dot(cell,float2(12.9898,78.233)))*43758.5453)-.5;
                colour*=1+grain*_PaperAmount;
                return fixed4(colour,1);
            }
            ENDCG
        }
    }
    Fallback Off
}
