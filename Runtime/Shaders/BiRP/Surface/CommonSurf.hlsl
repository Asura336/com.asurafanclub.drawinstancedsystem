#ifndef _ASURAFANCLUB_STANDARD_COMMON_SURFACE
    #define _ASURAFANCLUB_STANDARD_COMMON_SURFACE

    #include "InputCore.hlsl"

    struct Input
    {
        float2 uv_MainTex;
    };

    void surf(Input IN, inout SurfaceOutputStandard o)
    {
        half4 c = tex2D(_MainTex, IN.uv_MainTex) * _COLOR;
        o.Albedo = c.rgb;
        o.Alpha = c.a;

        #if defined(_RENDERING_CUTOUT)
            clip(c.a - _ALPHA_CUTOFF);
        #endif

        #if _NORMALMAP
            o.Normal = _TEX_NORMAL(IN.uv_MainTex);
        #endif

        float2 metallicGloss = _TEX_METALLIC_GLOSS(IN.uv_MainTex);
        o.Metallic = metallicGloss.x;
        o.Smoothness = metallicGloss.y;

        #if _EMISSION
            o.Emission = _TEX_EMISSION(IN.uv_MainTex);
        #endif
    }

#endif