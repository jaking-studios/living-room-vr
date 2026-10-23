// Living Room VR - LitTransparent
Shader "LRVR/LitTransparent"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _EmissionMap ("Emission Map", 2D) = "white" {}
        _EmissionColor ("Emission", Color) = (0,0,0,0)
        _LightMap ("Baked Lightmap (UV2)", 2D) = "gray" {}
        _LMScale ("Lightmap Scale", Float) = 1
        _Highlight ("Highlight", Float) = 0
        _Exposure ("Exposure", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex, _EmissionMap, _LightMap;
            float4 _MainTex_ST, _Color, _EmissionColor;
            float _LMScale, _Highlight, _Exposure;
            float4 _LRAmbient;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; float3 n : TEXCOORD2; };
            v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; o.uv2 = v.uv2; o.n = UnityObjectToWorldNormal(v.normal); return o; }
            float3 aces (float3 x) { return saturate((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14)); }
            fixed4 frag (v2f i) : SV_Target
            {
                float4 al = tex2D(_MainTex, i.uv) * _Color;
                float3 n = normalize(i.n);
                float3 amb = _LRAmbient.rgb * (0.72 + 0.28 * n.y) + _LRAmbient.rgb * 0.25 * saturate(dot(n, normalize(float3(0.3, 0.8, 0.5))));
                float3 c = al.rgb * amb + tex2D(_EmissionMap, i.uv).rgb * _EmissionColor.rgb;
                c += float3(1.0, 0.8, 0.3) * _Highlight;
                return float4(aces(c * _Exposure), al.a);
            }
            ENDCG
        }
    }
}
