Shader "XBX2Unity/Map Vertex Blend"
{
    Properties
    {
        _MainTex ("Base/Layer A", 2D) = "white" {}
        _BlendTex ("Overlay/Layer B", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _EnableBlend ("Enable Preview Blend", Float) = 0
        _BlendChannel ("Mask Channel RGBA", Range(0,3)) = 0
        _MaskSource ("Mask: 0=vBlend 1=Color 2=Base RGBA 3=Overlay RGBA 4=Constant", Range(0,4)) = 0
        _OverlayUVSet ("Overlay UV: 0=UV0 1=UV1", Range(0,1)) = 0
        _InvertMask ("Invert Mask", Range(0,1)) = 0
        _DebugView ("Debug: 0=Result 1=Raw Mask 2=Weight 3=Base 4=Overlay 5=vBlendRGB", Range(0,5)) = 0
        _ConstantWeight ("Constant Blend Weight", Range(0,1)) = 0.5
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
    float _MaskSource;
    float _OverlayUVSet;
    float _InvertMask;
    float _DebugView;
    float _ConstantWeight;
    float _BlendThreshold;
    float _BlendSoftness;
    float _UseVertexTint;

    struct appdata_map
    {
        float4 vertex : POSITION;
        float2 uv0 : TEXCOORD0;
        float2 uv1 : TEXCOORD1;
        // MXMD DataType::Blend (14) -> Mesh.uv3 (TEXCOORD2).
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
        o.baseUV = TRANSFORM_TEX(v.uv0, _MainTex);
        float2 secondUv = _OverlayUVSet > 0.5 ? v.uv1 : v.uv0;
        o.blendUV = TRANSFORM_TEX(secondUv, _BlendTex);
        o.blendMask = v.blendMask;
        o.color = v.color;
        return o;
    }

    float SelectChannel(float4 value, float channel)
    {
        if (channel < 0.5) return value.r;
        if (channel < 1.5) return value.g;
        if (channel < 2.5) return value.b;
        return value.a;
    }

    fixed4 frag_map(v2f_map i) : SV_Target
    {
        fixed4 baseColor = tex2D(_MainTex, i.baseUV);
        fixed4 overlayColor = tex2D(_BlendTex, i.blendUV);
        float rawMask = SelectChannel(i.blendMask, _BlendChannel);
        if (_MaskSource > 0.5 && _MaskSource < 1.5)
            rawMask = SelectChannel(i.color, _BlendChannel);
        else if (_MaskSource > 1.5 && _MaskSource < 2.5)
            rawMask = SelectChannel(baseColor, _BlendChannel);
        else if (_MaskSource > 2.5 && _MaskSource < 3.5)
            rawMask = SelectChannel(overlayColor, _BlendChannel);
        else if (_MaskSource > 3.5)
            rawMask = _ConstantWeight;

        rawMask = saturate(rawMask);
        if (_InvertMask > 0.5) rawMask = 1.0 - rawMask;

        float width = max(0.001, _BlendSoftness);
        float blend = smoothstep(_BlendThreshold - width,
                                 _BlendThreshold + width, rawMask);

        // The debug views are deliberately available even when the blend
        // texture cannot be resolved, to identify missing/flat source masks.
        if (_DebugView > 0.5 && _DebugView < 1.5)
            return fixed4(rawMask, rawMask, rawMask, 1);
        if (_DebugView > 1.5 && _DebugView < 2.5)
            return fixed4(blend, blend, blend, 1);
        if (_DebugView > 2.5 && _DebugView < 3.5)
            return fixed4(baseColor.rgb, 1);
        if (_DebugView > 3.5 && _DebugView < 4.5)
            return fixed4(overlayColor.rgb, 1);
        if (_DebugView > 4.5)
            return fixed4(saturate(i.blendMask.rgb), 1);

        fixed4 result = baseColor;
        if (_EnableBlend > 0.5)
            result = lerp(baseColor, overlayColor, saturate(blend));

        float4 tint = _UseVertexTint > 0.5 ? i.color : float4(1,1,1,1);
        return result * _Color * tint;
    }
    ENDCG

    // Preview shader only, not a reconstruction of Monolith Soft's
    // Wii U material passes or terrain edge blending pipeline.
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
