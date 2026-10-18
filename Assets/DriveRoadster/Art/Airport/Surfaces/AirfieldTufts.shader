Shader "DriveRoadster/AirfieldTufts"
{
    Properties {_Color("Tint",Color)=(1,1,1,1) _Wind("Wind",Range(0,0.12))=0.035}
    SubShader
    {
        Tags {"RenderType"="Opaque" "Queue"="Geometry"}
        Cull Off
        Pass
        {
            Tags {"LightMode"="ForwardBase"}
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            struct appdata {float4 vertex:POSITION;float3 normal:NORMAL;float2 uv:TEXCOORD0;fixed4 color:COLOR;UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct v2f {float4 pos:SV_POSITION;fixed3 color:COLOR;UNITY_FOG_COORDS(0) UNITY_VERTEX_OUTPUT_STEREO};
            fixed4 _Color;float _Wind;
            v2f vert(appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);v2f o;UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 wp=mul(unity_ObjectToWorld,v.vertex).xyz;
                float wind=sin(_Time.y*1.4+wp.x*0.35+wp.z*0.21)*_Wind*v.uv.y*v.uv.y;
                v.vertex.xz+=float2(wind,wind*0.45);
                // Distance scaling softens LOD changes without transparent overdraw.
                float fade=1-smoothstep(90,150,distance(wp,_WorldSpaceCameraPos));
                v.vertex.y*=fade;
                o.pos=UnityObjectToClipPos(v.vertex);
                float3 n=UnityObjectToWorldNormal(v.normal);
                half light=0.28+0.72*abs(dot(n,normalize(_WorldSpaceLightPos0.xyz)));
                o.color=v.color.rgb*_Color.rgb*(ShadeSH9(float4(0,1,0,1))+_LightColor0.rgb*light);
                UNITY_TRANSFER_FOG(o,o.pos);return o;
            }
            fixed4 frag(v2f i):SV_Target {UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);fixed4 c=fixed4(i.color,1);UNITY_APPLY_FOG(i.fogCoord,c);return c;}
            ENDCG
        }
    }
    FallBack Off
}
