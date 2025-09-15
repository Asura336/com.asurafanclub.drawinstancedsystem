#ifndef _ASURAFANCLUB_STANDARD_INPUTCORE
    #define _ASURAFANCLUB_STANDARD_INPUTCORE

    //----
    //half4       _Color;
    #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) && defined(INSTANCED_COLOR)
        StructuredBuffer<half4> _BaseColors;
        #define _COLOR _BaseColors[unity_InstanceID]
    #else
        half4 _Color;
        #define _COLOR _Color;
    #endif

    half        _Cutoff;
    #define _ALPHA_CUTOFF _Cutoff

    sampler2D   _MainTex;
    //float4      _MainTex_ST;

    sampler2D   _BumpMap;
    half        _BumpScale;
    #define _TEX_NORMAL(uv) TexNormal(uv)

    half        _Metallic;
    float       _Glossiness;
    sampler2D   _MetallicGlossMap;
    float       _GlossMapScale;
    #define _TEX_METALLIC_GLOSS(uv) TexMetallicGloss(uv)

    // sampler2D   _OcclusionMap;
    // half        _OcclusionStrength;
    // #define _TEX_OCCLUSION(uv) Occlusion(uv)

    half4       _EmissionColor;
    sampler2D   _EmissionMap;
    #define _TEX_EMISSION(uv) TexEmission(uv)
    //----

    inline half3 TexNormal(in float2 uv)
    {
        #if _NORMALMAP
            half3 normal = UnpackNormal(tex2D(_BumpMap, uv));
            normal.xy *= _BumpScale;
            normal.z = sqrt(1.0 - saturate(dot(normal.xy, normal.xy)));
            return normal;
        #else
            return 0;
        #endif
    }

    float2 TexMetallicGloss(in float2 uv)
    {
        half2 mg;
        #if _METALLICGLOSSMAP
            mg = tex2D(_MetallicGlossMap, uv).ra;
            mg.g *= _GlossMapScale;
        #else
            mg.r = _Metallic;
            mg.g = _Glossiness;
        #endif
        return mg;
    }

    // half Occlusion(float2 uv)
    // {
    //     #if (SHADER_TARGET < 30)
    //         // SM20: instruction count limitation
    //         // SM20: simpler occlusion
    //         return tex2D(_OcclusionMap, uv).g;
    //     #else
    //         half occ = tex2D(_OcclusionMap, uv).g;
    //         //return LerpOneTo(occ, _OcclusionStrength);
    //         return (1 - _OcclusionStrength) + occ * _OcclusionStrength;
    //     #endif
    // }

    half3 TexEmission(in float2 uv)
    {
        #if _EMISSION
            return tex2D(_EmissionMap, uv).rgb * _EmissionColor.rgb;
        #else
            return 0;
        #endif
    }

#endif