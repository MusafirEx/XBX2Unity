Shader "XBX2Unity/Skell Layer Preview"
{
    Properties
    {
        _MainTex ("MXMD Base Color", 2D) = "white" {}
        _Color ("MXMD Material Color", Color) = (1,1,1,1)
        _BumpMap ("BC5 Normal Candidate", 2D) = "bump" {}
        _SpecGlossMap ("SPM Candidate", 2D) = "black" {}
        _PaintMask ("RGB Companion Candidate (experimental mask)", 2D) = "black" {}
        _PaintColor1 ("Experimental Paint 1", Color) = (1,1,1,1)
        _PaintColor2 ("Experimental Paint 2", Color) = (1,1,1,1)
        _PaintColor3 ("Experimental Paint 3", Color) = (1,1,1,1)
        _PaintColor4 ("Experimental Paint 4", Color) = (1,1,1,1)
        _PaintStrength ("Paint Mask Effect (off by default)", Range(0,1)) = 0
        _NormalStrength ("Normal Detail Preview", Range(0,2)) = 0.5
        _SpecularStrength ("Specular Detail Preview", Range(0,2)) = 0.5
        _UseNormalMap ("Has Normal Candidate", Float) = 0
        _UseSpecularMap ("Has Specular Candidate", Float) = 0
        _UsePaintMask ("Interpret RGB as Mask (experimental)", Float) = 0
        _UseVertexColor ("Use Source Vertex Color", Float) = 0
        _ViewMode ("0=Composite 1=Base 2=Normal 3=Specular 4=RGBA Candidate", Range(0,4)) = 0
    }
    CGINCLUDE
    #include "UnityCG.cginc"
    sampler2D _MainTex;
    sampler2D _BumpMap;
    sampler2D _SpecGlossMap;
    sampler2D _PaintMask;
    float4 _MainTex_ST;
    float4 _Color;
    float4 _PaintColor1, _PaintColor2, _PaintColor3, _PaintColor4;
    float _PaintStrength, _NormalStrength, _SpecularStrength;
    float _UseNormalMap, _UseSpecularMap, _UsePaintMask;
    float _UseVertexColor, _ViewMode;

    struct appdata_xbx
    {
        float4 vertex : POSITION;
        float2 uv : TEXCOORD0;
        float4 color : COLOR;
    };
    struct v2f_xbx
    {
        float4 position : SV_POSITION;
        float2 uv : TEXCOORD0;
        float4 vertexColor : COLOR;
    };
    v2f_xbx vert_xbx(appdata_xbx v)
    {
        v2f_xbx o;
        o.position = UnityObjectToClipPos(v.vertex);
        o.uv = TRANSFORM_TEX(v.uv, _MainTex);
        o.vertexColor = v.color;
        return o;
    }
    fixed4 frag_xbx(v2f_xbx i) : SV_Target
    {
        fixed4 primary = tex2D(_MainTex, i.uv);
        fixed4 normalEncoded = tex2D(_BumpMap, i.uv);
        fixed4 specEncoded = tex2D(_SpecGlossMap, i.uv);
        fixed4 maskCandidate = tex2D(_PaintMask, i.uv);
        // BC5 stores two channels of the tangent-space normal. These are
        // shown directly in diagnostics. This preview cannot reproduce
        // Monolith Soft's native BRDF or tangent-space shading exactly.
        float2 normalXY = normalEncoded.rg * 2.0 - 1.0;
        float normalZ = sqrt(saturate(1.0 - dot(normalXY, normalXY)));
        if (_ViewMode > 0.5 && _ViewMode < 1.5)
            return fixed4(primary.rgb, 1);
        if (_ViewMode > 1.5 && _ViewMode < 2.5)
            return fixed4(normalXY * 0.5 + 0.5, normalZ, 1);
        if (_ViewMode > 2.5 && _ViewMode < 3.5)
            return fixed4(specEncoded.rgb, 1);
        if (_ViewMode > 3.5)
            return fixed4(maskCandidate.rgb, 1);

        fixed4 resultColor = primary * _Color;
        if (_UseVertexColor > 0.5)
            resultColor *= i.vertexColor;
        if (_UsePaintMask > 0.5 && _PaintStrength > 0.001)
        {
            // A deliberately EXPLICIT experiment: this assumes RGBA can
            // represent four paint zones, which is NOT yet confirmed.
            float total = dot(maskCandidate, float4(1,1,1,1));
            float3 zoneTint = (
                maskCandidate.r * _PaintColor1.rgb +
                maskCandidate.g * _PaintColor2.rgb +
                maskCandidate.b * _PaintColor3.rgb +
                maskCandidate.a * _PaintColor4.rgb) / max(total, 0.0001);
            resultColor.rgb = lerp(resultColor.rgb,
                resultColor.rgb * zoneTint * 1.5,
                saturate(total) * _PaintStrength);
        }
        if (_UseNormalMap > 0.5)
        {
            float detail = saturate(dot(
                float3(normalXY, normalZ), normalize(float3(0.4,0.35,0.85))));
            resultColor.rgb *= lerp(1.0, 0.6 + 0.4 * detail,
                saturate(_NormalStrength));
        }
        if (_UseSpecularMap > 0.5)
            resultColor.rgb += specEncoded.rgb * _SpecularStrength * 0.15;
        resultColor.a = 1;
        return resultColor;
    }
    ENDCG

    // Unlit/baked diagnostic passes that also work when no source tangents
    // were imported. They are NOT an accurate native lighting shader.
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }
            Cull Back ZWrite On
            CGPROGRAM
            #pragma vertex vert_xbx
            #pragma fragment frag_xbx
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
            #pragma vertex vert_xbx
            #pragma fragment frag_xbx
            #pragma target 3.0
            ENDCG
        }
    }
    FallBack "Unlit/Texture"
}
