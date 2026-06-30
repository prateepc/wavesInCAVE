Shader "Custom/AcousticWaveClipper"
{
    Properties
    {
        _Color ("Main Color (Managed by Script)", Color) = (1,1,1,0.5)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100

        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 worldPos : TEXCOORD0;
            };

            float4 _Color;
            float4 _ChamberMin; // Absolute Minimum boundaries (X_Min, Y_Min, Z_Min)
            float4 _ChamberMax; // Absolute Maximum boundaries (X_Max, Y_Max, Z_Max)

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0)).xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Absolute structural boundary clipping check
                if (i.worldPos.x < _ChamberMin.x || i.worldPos.x > _ChamberMax.x ||
                    i.worldPos.y < _ChamberMin.y || i.worldPos.y > _ChamberMax.y ||
                    i.worldPos.z < _ChamberMin.z || i.worldPos.z > _ChamberMax.z)
                {
                    discard; // Cleanly clip any pixel poking outside the visual room surfaces
                }

                return _Color;
            }
            ENDCG
        }
    }
}

