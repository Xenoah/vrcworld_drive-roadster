Shader "DriveRoadster/CanopyGlassTwoSided" {
Properties { _Color("Tint",Color)=(0.28,0.16,0.09,0.14) _MainTex("Texture",2D)="white"{} _Glossiness("Smoothness",Range(0,1))=0.85 _Metallic("Metallic",Range(0,1))=0.05 }
SubShader { Tags {"Queue"="Transparent" "RenderType"="Transparent"} LOD 200 Cull Off
CGPROGRAM
#pragma surface surf Standard alpha:fade
#pragma target 3.0
sampler2D _MainTex; fixed4 _Color; half _Glossiness; half _Metallic;
struct Input { float2 uv_MainTex; };
void surf(Input IN,inout SurfaceOutputStandard o) { fixed4 c=tex2D(_MainTex,IN.uv_MainTex)*_Color; o.Albedo=c.rgb; o.Alpha=c.a; o.Metallic=_Metallic; o.Smoothness=_Glossiness; }
ENDCG
} FallBack "Transparent/Diffuse"
}
