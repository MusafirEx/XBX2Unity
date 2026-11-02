Shader "XBX2Unity/Skell Color Diagnostic"
{
    Properties
    {
        _MainTex ("Selected source color texture", 2D) = "white" {}
        _Color ("Original MXMD gMatCol RGBA", Color) = (1,1,1,1)
        _UseVertexColor ("Multiply source vColor", Float) = 0
        _DebugView ("0=Composite 1=Texture 2=Vertex 3=gMatCol 4=UV 5=Checker", Range(0,5)) = 0
    }

    CGINCLUDE
    #include "UnityCG.cginc"
    sampler2D _MainTex;
    float4 _MainTex_ST;
    float4 _Color;
    float _UseVertexColor;
    float _DebugView;

    struct appdata_skell
    {
        float4 vertex : POSITION;
        float2 uv : TEXCOORD0;
        float4 color : COLOR;
    };
    struct v2f_skell
    {
        float4 pos : SV_POSITION;
        float2 uv : TEXCOORD0;
        float4 sourceColor : COLOR;
    };
    v2f_skell vert_skell(appdata_skell i)
    {
        v2f_skell o;
        o.pos = UnityObjectToClipPos(i.vertex);
        o.uv = TRANSFORM_TEX(i.uv, _MainTex);
        o.sourceColor = i.color;
        return o;
    }
    fixed4 frag_skell(v2f_skell i) : SV_Target
    {
        fixed4 tex = tex2D(_MainTex, i.uv);
        if (_DebugView > 0.5 && _DebugView < 1.5)
            return fixed4(tex.rgb, 1);
        if (_DebugView > 1.5 && _DebugView < 2.5)
            return fixed4(i.sourceColor.rgb, 1);
        if (_DebugView > 2.5 && _DebugView < 3.5)
            return fixed4(_Color.rgb, 1);
        if (_DebugView > 3.5 && _DebugView < 4.5)
            return fixed4(frac(i.uv.x), frac(i.uv.y), 0, 1);
        if (_DebugView > 4.5)
        {
            float2 gridCoord = i.uv * 12.0;
            float parity = frac((floor(gridCoord.x) + floor(gridCoord.y)) * 0.5) * 2.0;
            float2 cell = frac(gridCoord);
            float gridLineWeight = 1.0 - step(0.055, min(cell.x, cell.y));
            return fixed4(lerp(float3(0.18, 0.18, 0.18),
                float3(0.85, 0.85, 0.85), parity) * (1.0 - gridLineWeight) +
                float3(0.9, 0.2, 0.1) * gridLineWeight, 1.0);
        }
        fixed4 tinted = tex * _Color;
        if (_UseVertexColor > 0.5)
            tinted *= i.sourceColor;
        return tinted;
    }
    ENDCG

    // Unlit diagnostic only. The original Xenoblade X shader passes,
    // metallic paint, AO, reflection, and normal mapping are NOT reproduced.
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }
            Cull Back ZWrite On
            CGPROGRAM
            #pragma vertex vert_skell
            #pragma fragment frag_skell
            #pragma target 3.0
            ENDCG
        }
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            Name "ForwardBase"
            Tags { "LightMode"="ForwardBase" }
            Cull Back ZWrite On
            CGPROGRAM
            #pragma vertex vert_skell
            #pragma fragment frag_skell
            #pragma target 3.0
            ENDCG
        }
    }
    FallBack "Unlit/Texture"
}
