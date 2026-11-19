// Soft-edged UI surface for the creation-screen 3D portrait. Samples the
// portrait RenderTexture (cleared transparent) and keeps the real model alpha;
// only the outer ~2% rim of the quad fades so the hard RT edge melts into the
// painted rental-room backdrop without cutting into the body.
Shader "Guochao/CreationPortrait"
{
    Properties
    {
        _MainTex ("Portrait", 2D) = "white" {}
        _Warmth ("Warm lift", Color) = (1.04, 1.0, 0.94, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Warmth;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 colour = tex2D(_MainTex, i.uv) * _Warmth;
                // The portrait RT is cleared transparent, so the real model
                // alpha is kept as-is — no body-shaped cutout. Only the outer
                // ~2% of the quad fades so the hard RT rim never shows.
                float edge = min(min(i.uv.x, 1.0 - i.uv.x), min(i.uv.y, 1.0 - i.uv.y));
                float rim = saturate(edge / 0.02);
                colour.a *= rim;
                return colour;
            }
            ENDCG
        }
    }
}
