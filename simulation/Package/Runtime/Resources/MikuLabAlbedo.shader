Shader "MikuPhysicsLab/Albedo"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Cull ("Culling", Float) = 0
        _InspectionLighting ("Inspection lighting", Float) = 0
        _Cutoff ("Alpha cutoff", Range(0, 1)) = .35
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull [_Cull]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float3 normal : NORMAL; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; float3 normal : TEXCOORD1; };
            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _InspectionLighting;
            float _Cutoff;
            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 color = tex2D(_MainTex, i.uv) * _Color;
                clip(color.a - _Cutoff);
                color.rgb *= lerp(1, .72 + .28 * abs(dot(normalize(i.normal), normalize(float3(.3,.8,.5)))), _InspectionLighting);
                return color;
            }
            ENDCG
        }
    }
}
