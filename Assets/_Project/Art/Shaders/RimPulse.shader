// 11/13 팀장: 방어력 감소 "걸려 있는 동안" A안 — 파티클 대신 모델 가장자리(테두리)가 색으로 은은하게 빛남.
// 모델을 한 번 더 그리되 가장자리만 더해 그림 (빛 더하기, 깊이 안 씀). 세기는 StatusRim이 천천히 깜빡이게 바꿈.
Shader "Fantasia/RimPulse"
{
    Properties
    {
        _RimColor ("Rim Color", Color) = (0.45, 0.65, 1, 1)
        _Power ("Rim Power", Float) = 2.6
        _Strength ("Strength", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        ZTest LEqual
        Cull Back
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _RimColor;
            float _Power, _Strength;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 v : TEXCOORD1; };

            v2f vert (appdata i)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(i.vertex);
                o.n = UnityObjectToWorldNormal(i.normal);
                o.v = WorldSpaceViewDir(i.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float rim = pow(1.0 - saturate(dot(normalize(i.n), normalize(i.v))), _Power);
                return _RimColor * rim * _Strength;
            }
            ENDCG
        }
    }
    Fallback Off
}
