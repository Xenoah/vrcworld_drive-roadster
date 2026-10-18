Shader "DriveRoadster/AirfieldSurface"
{
    Properties
    {
        _MainTex("Seamless ground",2D)="white"{}
        _Color("Tint",Color)=(1,1,1,1)
        _TileMeters("Tile size in metres",Float)=4
        _Variation("Broad colour variation",Range(0,0.3))=0.12
        _Glossiness("Smoothness",Range(0,1))=0.08
    }
    SubShader
    {
        Tags {"RenderType"="Opaque"}
        LOD 200
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        #pragma multi_compile_instancing
        sampler2D _MainTex;
        fixed4 _Color;
        half _TileMeters,_Variation,_Glossiness;
        struct Input {float3 worldPos;};
        float hash(float2 p){p=frac(p*float2(123.34,456.21));p+=dot(p,p+45.32);return frac(p.x*p.y);}
        float broadNoise(float2 p)
        {
            float2 cell=floor(p),f=frac(p);f=f*f*(3-2*f);
            return lerp(lerp(hash(cell),hash(cell+float2(1,0)),f.x),lerp(hash(cell+float2(0,1)),hash(cell+1),f.x),f.y)*2-1;
        }
        void surf(Input IN,inout SurfaceOutputStandard o)
        {
            float2 uv=IN.worldPos.xz/max(0.25,_TileMeters);
            // A second rotation breaks up recognisable repeats; mirror wrapping has no edge seam.
            fixed3 a=tex2D(_MainTex,uv).rgb;
            fixed3 b=tex2D(_MainTex,float2(-uv.y,uv.x)*0.731+float2(0.37,0.61)).rgb;
            float broad=broadNoise(IN.worldPos.xz*0.018);
            o.Albedo=lerp(a,b,0.28)*_Color.rgb*(1+broad*_Variation);
            o.Metallic=0;o.Smoothness=_Glossiness;o.Alpha=1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
