Shader "XBX2Unity/Skell Layer Preview"
{
    Properties
    {
        _MainTex ("MXMD Base Color", 2D) = "white" {}
        _Color ("MXMD Material Color", Color) = (1,1,1,1)
        _BumpMap ("BC5 Normal Candidate", 2D) = "bump" {}
        _SpecGlossMap ("SPM Candidate", 2D) = "black" {}
        _PaintMask ("RGB Companion: R metallic / G body / B glow", 2D) = "black" {}
        _PaintColor1 ("Experimental Paint 1", Color) = (1,1,1,1)
        _PaintColor2 ("Experimental Paint 2", Color) = (1,1,1,1)
        _PaintColor3 ("Experimental Paint 3", Color) = (1,1,1,1)
        _PaintColor4 ("Experimental Paint 4", Color) = (1,1,1,1)
        _PaintStrength ("Paint Mask Effect (off by default)", Range(0,1)) = 0
        _NormalStrength ("Normal Detail Preview", Range(0,2)) = 0.5
        _SpecularStrength ("Specular Detail Preview", Range(0,2)) = 0.5
        _UseNormalMap ("Has Normal Candidate", Float) = 0
        _UseSpecularMap ("Has Specular Candidate", Float) = 0
        _UsePaintMask ("Legacy 4-zone RGBA experiment (unverified)", Float) = 0
        _UseObservedRedMetallic ("Use observed R for metallic", Float) = 0
        _MetallicStrength ("Metallic Strength (R mask)", Range(0,1)) = 1
        _MetallicMaskGain ("Red Metallic Mask Gain", Range(0.1,8)) = 1
        _MetallicSmoothness ("Metallic Smoothness (estimated)", Range(0,1)) = 0.6
        _MetallicHighlightStrength ("Metallic Highlight Strength", Range(0,2)) = 1
        _UseObservedGreenBody ("Use observed G for body tint", Float) = 0
        _BodyPaintColor ("Body Color (G mask)", Color) = (1,1,1,1)
        _BodyMaskStrength ("Body Tint Strength", Range(0,1)) = 1
        _BodyMaskGain ("Green Mask Gain", Range(0.1,8)) = 1
        _UseObservedBlueGlow ("Use observed B for glow", Float) = 0
        [HDR] _GlowColor ("Glow Color (B mask)", Color) = (0.2,0.6,1,1)
        _GlowIntensity ("Glow Intensity (preview)", Range(0,8)) = 1
        _GlowMaskGain ("Blue Mask Gain", Range(0.1,8)) = 1
        _UseVertexColor ("Use Source Vertex Color", Float) = 0
        _ViewMode ("Source View: Composite, source, RGB, R metallic, G body, B glow", Range(0,15)) = 0
        _MaskChannel ("RGB Candidate Channel (0=R,1=G,2=B,3=A)", Range(0,3)) = 0
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
    float _UseVertexColor, _ViewMode, _MaskChannel;
    float _UseObservedRedMetallic, _MetallicStrength, _MetallicMaskGain;
    float _MetallicSmoothness, _MetallicHighlightStrength;
    float _UseObservedGreenBody, _BodyMaskStrength, _BodyMaskGain;
    float4 _BodyPaintColor;
    float _UseObservedBlueGlow, _GlowIntensity, _GlowMaskGain;
    float4 _GlowColor;

    struct appdata_xbx
    {
        float4 vertex : POSITION;
        float3 normal : NORMAL;
        float2 uv : TEXCOORD0;
        float4 color : COLOR;
    };
    struct v2f_xbx
    {
        float4 position : SV_POSITION;
        float2 uv : TEXCOORD0;
        float4 vertexColor : COLOR;
        float3 worldNormal : TEXCOORD1;
        float3 worldPosition : TEXCOORD2;
    };
    v2f_xbx vert_xbx(appdata_xbx v)
    {
        v2f_xbx o;
        o.position = UnityObjectToClipPos(v.vertex);
        o.uv = TRANSFORM_TEX(v.uv, _MainTex);
        o.vertexColor = v.color;
        o.worldNormal = UnityObjectToWorldNormal(v.normal);
        o.worldPosition = mul(unity_ObjectToWorld, v.vertex).xyz;
        return o;
    }
    float4 frag_xbx(v2f_xbx i) : SV_Target
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
        if (_ViewMode > 3.5 && _ViewMode < 4.5)
            return fixed4(maskCandidate.rgb, 1);
        float maskChannelValue = _MaskChannel < 0.5 ? maskCandidate.r :
            _MaskChannel < 1.5 ? maskCandidate.g :
            _MaskChannel < 2.5 ? maskCandidate.b : maskCandidate.a;
        if (_ViewMode > 4.5 && _ViewMode < 5.5)
            return fixed4(maskChannelValue,maskChannelValue,maskChannelValue,1);
        if (_ViewMode > 5.5 && _ViewMode < 6.5)
        {
            float binaryValue = step(0.5,maskChannelValue);
            return fixed4(binaryValue,binaryValue,binaryValue,1);
        }
        if (_ViewMode > 6.5 && _ViewMode < 7.5)
            return fixed4(i.vertexColor.rgb,1);
        if (_ViewMode > 7.5 && _ViewMode < 8.5)
        {
            float strongest = max(maskCandidate.r,max(maskCandidate.g,maskCandidate.b));
            return fixed4(maskCandidate.rgb / max(strongest,0.0001),1);
        }
        if (_ViewMode > 8.5 && _ViewMode < 9.5)
            return fixed4(abs(primary.rgb-maskCandidate.rgb),1);
        if (_ViewMode > 9.5 && _ViewMode < 10.5)
            return fixed4(maskCandidate.a,maskCandidate.a,maskCandidate.a,1);
        // Observed _RGB roles from source/game comparison:
        // R = metallic coverage, G = body tint, B = emissive region.
        // The BC1 companion is bound as linear mask-data texture, not sRGB.
        // Alpha remains unidentified; do not interpret it as smoothness.
        float metalCoverage = saturate(maskCandidate.r * _MetallicMaskGain);
        float bodyCoverage = saturate(maskCandidate.g * _BodyMaskGain);
        float glowCoverage = saturate(maskCandidate.b * _GlowMaskGain);
        if (_ViewMode > 10.5 && _ViewMode < 11.5)
            return float4(maskCandidate.g,maskCandidate.g,maskCandidate.g,1);
        if (_ViewMode > 11.5 && _ViewMode < 12.5)
            return float4(maskCandidate.b,maskCandidate.b,maskCandidate.b,1);
        if (_ViewMode > 12.5 && _ViewMode < 13.5)
            return float4(0,maskCandidate.g,maskCandidate.b,1);
        // New debug modes 14/15; existing modes 0..13 keep their meaning.
        if (_ViewMode > 13.5 && _ViewMode < 14.5)
            return float4(maskCandidate.r,maskCandidate.r,maskCandidate.r,1);
        if (_ViewMode > 14.5)
            return float4(maskCandidate.r,maskCandidate.g,maskCandidate.b,1);

        float4 resultColor = primary * _Color;
        if (_UseVertexColor > 0.5)
            resultColor *= i.vertexColor;
        if (_UsePaintMask > 0.5 &&
            _UseObservedRedMetallic < 0.5 &&
            _UseObservedGreenBody < 0.5 && _UseObservedBlueGlow < 0.5 &&
            _PaintStrength > 0.001)
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
        // Source-observed channel mapping is independent of the abandoned
        // assumption that RGBA holds four smooth paint zones.
        if (_UseObservedGreenBody > 0.5)
            resultColor.rgb *= lerp(float3(1,1,1), _BodyPaintColor.rgb,
                bodyCoverage * saturate(_BodyMaskStrength));
        if (_UseNormalMap > 0.5)
        {
            float detail = saturate(dot(
                float3(normalXY, normalZ), normalize(float3(0.4,0.35,0.85))));
            resultColor.rgb *= lerp(1.0, 0.6 + 0.4 * detail,
                saturate(_NormalStrength));
        }
        if (_UseSpecularMap > 0.5)
            resultColor.rgb += specEncoded.rgb * _SpecularStrength * 0.15;
        // Metallic response preview: the red channel determines HOW MUCH of
        // the base color is used as a conductor's reflection (0=dielectric,
        // 1=metal). A camera-dependent GGX-like highlight + environment
        // gradient provides a visual metallic indication in both Built-in
        // and URP, without assuming the original game's BRDF/roughness.
        // This is NOT a full PBR replacement or a scene-lighted reflection.
        if (_UseObservedRedMetallic > 0.5)
        {
            float metallic = metalCoverage * saturate(_MetallicStrength);
            if (metallic > 0.001)
            {
                float3 N = normalize(i.worldNormal);
                float3 V = normalize(_WorldSpaceCameraPos.xyz - i.worldPosition);
                float3 L = normalize(float3(0.45, 0.82, 0.35)); // diagnostic key
                float3 H = normalize(V + L);
                float NdotV = max(saturate(dot(N, V)), 0.02);
                float NdotL = saturate(dot(N, L));
                float NdotH = saturate(dot(N, H));
                float roughness = max(0.08, 1.0 - saturate(_MetallicSmoothness));
                float alpha = roughness * roughness;
                float alpha2 = alpha * alpha;
                float d = NdotH * NdotH * (alpha2 - 1.0) + 1.0;
                float distribution = alpha2 / max(3.14159265 * d * d, 0.0001);
                float k = (roughness + 1.0) * (roughness + 1.0) * 0.125;
                float geometry = (NdotV / (NdotV * (1.0 - k) + k)) *
                    (NdotL / (NdotL * (1.0 - k) + k));
                float highlight = min(
                    distribution * geometry * NdotL /
                    max(4.0 * NdotV * NdotL, 0.005), 2.0);
                float3 reflectionVector = reflect(-V, N);
                float hemisphere = saturate(reflectionVector.y * 0.5 + 0.5);
                float ambientReflection = lerp(0.25, 0.75, hemisphere);
                float3 metalF0 = saturate(resultColor.rgb);
                float3 fresnel = metalF0 + (1.0 - metalF0) *
                    pow(1.0 - NdotV, 5.0);
                float3 metallicResponse = fresnel *
                    (ambientReflection +
                     highlight * max(_MetallicHighlightStrength, 0.0));
                // Blend by R coverage only; the G tint was applied BEFORE
                // this response, while B emission is applied AFTER it.
                resultColor.rgb = lerp(resultColor.rgb,
                    metallicResponse, metallic);
            }
        }
        // Unlit approximate emission: a visible bright channel overlay,
        // NOT authentic Xenoblade lighting and not guaranteed bloom.
        if (_UseObservedBlueGlow > 0.5)
            resultColor.rgb += _GlowColor.rgb * glowCoverage *
                max(_GlowIntensity, 0.0);
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
