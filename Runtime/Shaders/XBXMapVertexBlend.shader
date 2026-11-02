Shader "XBX2Unity/Map Vertex Blend"
{
    Properties
    {
        _MainTex ("Base Texture (from CAMDO material slot)", 2D) = "white" {}
        _BlendTex ("Optional Second Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _EnableBlend ("Blend Enabled", Float) = 0
        _BlendChannel ("vBlend Channel 0=R 1=G 2=B 3=A", Range(0,3)) = 0
        _BlendThreshold ("Blend Threshold", Range(0,1)) = 0.5
        _BlendSoftness ("Blend Transition Width", Range(0.001,0.5)) = 0.12
        _UseVertexTint ("Use Vertex Color as Tint", Float) = 0
    }

    CGINCLUDE
    #include "UnityCG.cginc"
    sampler2D _MainTex;
    sampler2D _BlendTex;
    float4 _MainTex_ST;
    float4 _BlendTex_ST;
    float4 _Color;
    float _EnableBlend;
    float _BlendChannel;
    float _BlendThreshold;
    float _BlendSoftness;
    float _UseVertexTint;

    struct appdata_map
    {
        float4 vertex : POSITION;
        float2 uv : TEXCOORD0;
        // Wii U MXMD vBlend (attribute type 14) stored in Mesh.uv3.
        float4 blendMask : TEXCOORD2;
        float4 color : COLOR;
    };

    struct v2f_map
    {
        float4 pos : SV_POSITION;
        float2 baseUV : TEXCOORD0;
        float2 blendUV : TEXCOORD1;
        float4 blendMask : TEXCOORD2;
        float4 color : COLOR;
    };

    v2f_map vert_map(appdata_map v)
    {
        v2f_map o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.baseUV = TRANSFORM_TEX(v.uv, _MainTex);
        o.blendUV = TRANSFORM_TEX(v.uv, _BlendTex);
        o.blendMask = v.blendMask;
        o.color = v.color;
        return o;
    }

    fixed4 frag_map(v2f_map i) : SV_Target
    {
        fixed4 baseColor = tex2D(_MainTex, i.baseUV);
        if (_EnableBlend > 0.5)
        {
            float mask = _BlendChannel < 0.5 ? i.blendMask.x :
                (_BlendChannel < 1.5 ? i.blendMask.y :
                (_BlendChannel < 2.5 ? i.blendMask.z : i.blendMask.w));
            float soft = max(0.001, _BlendSoftness);
            float weight = smoothstep(_BlendThreshold - soft, _BlendThreshold + soft, mask);
            fixed4 second = tex2D(_BlendTex, i.blendUV);
            baseColor = lerp(baseColor, second, saturate(weight));
        }

        float4 tint = _UseVertexTint > 0.5 ? i.color : float4(1,1,1,1);
        return baseColor * _Color * tint;
    }
    ENDCG

    // Unity 2022 URP preview. This is a simple unlit diagnostic shader:
    // XCX-specific layer compositing, lightmaps and PBR are not reconstructed.
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Back ZWrite On
        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }
            CGPROGRAM
            #pragma vertex vert_map
            #pragma fragment frag_map
            #pragma target 3.0
            ENDCG
        }
    }

    // Unity Built-in Render Pipeline fallback.
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Back ZWrite On
        Pass
        {
            Name "ForwardBase"
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert_map
            #pragma fragment frag_map
            #pragma target 3.0
            ENDCG
        }
    }
    FallBack "Unlit/Texture"
}
