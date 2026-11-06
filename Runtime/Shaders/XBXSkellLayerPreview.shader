Shader "XBX2Unity/Skell Layer Preview"
{
    Properties
    {
        _MainTex ("MXMD Base Color", 2D) = "white" {}
        _Color ("MXMD Material Color", Color) = (1,1,1,1)
        _BumpMap ("BC5 Normal Candidate", 2D) = "bump" {}
        _SpecGlossMap ("SPM Candidate", 2D) = "black" {}
        _PaintMask ("Companion RGBA: R height bump / G metallic / B glow / A color mask", 2D) = "black" {}
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
        _UseObservedRedBump ("Use Red as Height/Bump", Float) = 0
        _BumpStrength ("Red Bump Strength", Range(0,4)) = 1
        _BumpMaskGain ("Red Height Mask Gain", Range(0.1,8)) = 1
        _UseObservedGreenMetallic ("Use Green as Metallic Mask", Float) = 0
        _MetallicStrength ("Metallic Strength (G mask)", Range(0,1)) = 1
        _MetallicMaskGain ("Green Metallic Mask Gain", Range(0.1,8)) = 1
        _MetallicSmoothness ("Metallic Smoothness (estimated)", Range(0,1)) = 0.6
        _MetallicHighlightStrength ("Metallic Highlight Strength", Range(0,2)) = 1
        _UseObservedAlphaColor ("Use Alpha as Color/Tint Mask", Float) = 0
        _AlphaTintColor ("Custom Color (A mask)", Color) = (1,1,1,1)
        _AlphaColorStrength ("Color Tint Strength", Range(0,1)) = 1
        _AlphaMaskGain ("Alpha Color Mask Gain", Range(0.1,8)) = 1
        _UseObservedBlueGlow ("Use observed B for glow", Float) = 0
        [HDR] _GlowColor ("Glow Color (B mask)", Color) = (0.2,0.6,1,1)
        _GlowIntensity ("Glow Intensity (preview)", Range(0,8)) = 1
        _GlowMaskGain ("Blue Mask Gain", Range(0.1,8)) = 1
        _UseVertexColor ("Use Source Vertex Color", Float) = 0
        _ViewMode ("Source View: Composite, sources, R bump, G metal, B glow, A color", Range(0,17)) = 0
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
    float _UseObservedRedBump, _BumpStrength, _BumpMaskGain;
    float _UseObservedGreenMetallic, _MetallicStrength, _MetallicMaskGain;
    float _MetallicSmoothness, _MetallicHighlightStrength;
    float _UseObservedAlphaColor, _AlphaColorStrength, _AlphaMaskGain;
    float4 _AlphaTintColor;
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
    // Convert the single-channel RED height map into a screen-space normal
    // perturbation. A single scalar cannot encode a tangent-space XYZ normal.
    // Uses geometric derivatives, so it does NOT need imported tangent vectors.
    // Source _RGB texture is already bound as linear data by the CAMDO importer.
    float3 NormalFromRedHeight(float3 worldPos, float3 worldNormal,
        float redHeight, float strength)
    {
        float3 N = normalize(worldNormal);
        float3 dpdx = ddx(worldPos);
        float3 dpdy = ddy(worldPos);
        float dhdx = ddx(redHeight);
        float dhdy = ddy(redHeight);
        float3 r1 = cross(dpdy, N);
        float3 r2 = cross(N, dpdx);
        float determinant = dot(dpdx, r1);
        if (abs(determinant) < 1e-7)
            return N;
        float3 surfaceGradient = (dhdx * r1 + dhdy * r2) *
            (sign(determinant) / abs(determinant));
        return normalize(N - surfaceGradient * max(strength, 0.0));
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
        // User-selected packed channel interpretation:
        // R = bump/height, G = metallic, B = emission, A = color/tint mask.
        // BC1 alpha may be ONE BIT (or entirely opaque), not a color value.
        // Alpha therefore selects the supplied _AlphaTintColor; it does not
        // encode RGB by itself. Smoothness is an independent user parameter.
        float redHeight = saturate(maskCandidate.r * _BumpMaskGain);
        float metalCoverage = saturate(maskCandidate.g * _MetallicMaskGain);
        float glowCoverage = saturate(maskCandidate.b * _GlowMaskGain);
        float colorCoverage = saturate(maskCandidate.a * _AlphaMaskGain);
        float3 bumpNormal = NormalFromRedHeight(
            i.worldPosition, i.worldNormal, redHeight, _BumpStrength);
        if (_ViewMode > 10.5 && _ViewMode < 11.5)
            return float4(maskCandidate.g,maskCandidate.g,maskCandidate.g,1);
        if (_ViewMode > 11.5 && _ViewMode < 12.5)
            return float4(maskCandidate.b,maskCandidate.b,maskCandidate.b,1);
        if (_ViewMode > 12.5 && _ViewMode < 13.5)
            return float4(0,maskCandidate.g,maskCandidate.b,1);
        // Modes 14/15 remain raw Red/combined RGB previews. Add Alpha tint
        // mask and perturbed normal previews as modes 16/17.
        if (_ViewMode > 13.5 && _ViewMode < 14.5)
            return float4(maskCandidate.r,maskCandidate.r,maskCandidate.r,1);
        if (_ViewMode > 14.5 && _ViewMode < 15.5)
            return float4(maskCandidate.r,maskCandidate.g,maskCandidate.b,1);
        if (_ViewMode > 15.5 && _ViewMode < 16.5)
            return float4(colorCoverage, colorCoverage, colorCoverage, 1);
        if (_ViewMode > 16.5)
            return float4(bumpNormal * 0.5 + 0.5, 1);

        float4 resultColor = primary * _Color;
        if (_UseVertexColor > 0.5)
            resultColor *= i.vertexColor;
        if (_UsePaintMask > 0.5 &&
            _UseObservedRedBump < 0.5 &&
            _UseObservedGreenMetallic < 0.5 &&
            _UseObservedBlueGlow < 0.5 && _UseObservedAlphaColor < 0.5 &&
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
        // A is a scalar color mask, not an encoded RGB color.
        if (_UseObservedAlphaColor > 0.5)
            resultColor.rgb *= lerp(float3(1,1,1), _AlphaTintColor.rgb,
                colorCoverage * saturate(_AlphaColorStrength));
        // R is a scalar HEIGHT channel. Its slope perturbs normals; adding a
        // small fixed diagnostic light delta makes this visible even if G
        // metallic is OFF. BC5 normal preview remains a separate control.
        if (_UseObservedRedBump > 0.5)
        {
            float3 previewLight = normalize(float3(0.45, 0.82, 0.35));
            float originalLight = saturate(dot(normalize(i.worldNormal), previewLight));
            float perturbedLight = saturate(dot(bumpNormal, previewLight));
            resultColor.rgb *= max(0.15,
                1.0 + 0.85 * (perturbedLight - originalLight));
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
        // GREEN controls metallic reflectivity. The bump-perturbed normal
        // from RED is reused in specular lighting when bump is enabled.
        // A fixed diagnostic environment stands in for real reflection probes.
        if (_UseObservedGreenMetallic > 0.5)
        {
            float metallic = metalCoverage * saturate(_MetallicStrength);
            if (metallic > 0.001)
            {
                float3 N = _UseObservedRedBump > 0.5 ?
                    bumpNormal : normalize(i.worldNormal);
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
                // A color tint was applied BEFORE G metallic reflection,
                // while B glow emission is applied AFTER metallic response.
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
