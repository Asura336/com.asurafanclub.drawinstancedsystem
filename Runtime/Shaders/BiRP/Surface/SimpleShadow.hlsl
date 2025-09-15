#ifndef _ASURAFANCLUB_STANDARD_COMMON_SHADOW
    #define _ASURAFANCLUB_STANDARD_COMMON_SHADOW

    #include "UnityCG.cginc"
    #include "DrawProceduralGPU.hlsl"

    //#include "CommonProperty.hlsl"
    #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) && defined(INSTANCED_COLOR)
        StructuredBuffer<half4> _BaseColors;
        #define _COLOR _BaseColors[unity_InstanceID]
    #else
        UNITY_INSTANCING_BUFFER_START(Props)
        UNITY_DEFINE_INSTANCED_PROP(half4, _Color)
        UNITY_INSTANCING_BUFFER_END(Props)
        #define _COLOR UNITY_ACCESS_INSTANCED_PROP(Props, _Color)
    #endif

    #if defined(_RENDERING_CUTOUT)
        float _AlphaCutoff;
        #define _ALPHA_CUTOFF _AlphaCutoff
    #endif

    #if defined(_RENDERING_FADE) || defined(_RENDERING_TRANSPARENT)
        #define SHADOWS_SEMITRANSPARENT 1
    #endif

    #if SHADOWS_SEMITRANSPARENT || defined(_RENDERING_CUTOUT)
        #define SHADOWS_NEED_UV 1
    #endif

    sampler3D _DitherMaskLOD;
    sampler2D _MainTex;
    float4 _MainTex_ST;

    struct appdata
    {
        float4 vertex : POSITION;
        float3 normal : NORMAL;
        float2 texcoord : TEXCOORD0;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct InterpolatorsVertex
    {
        float4 position : SV_POSITION;
        #if SHADOWS_NEED_UV
            float2 uv : TEXCOORD0;
        #endif
        #if defined(SHADOWS_CUBE)
            float3 lightVec : TEXCOORD1;
        #endif
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Interpolators
    {
        #if SHADOWS_SEMITRANSPARENT
            // https://docs.unity3d.com/cn/current/Manual/SL-BuiltinMacros.html
            UNITY_VPOS_TYPE vpos : VPOS;
        #else
            float4 positions : SV_POSITION;
        #endif
        
        #if SHADOWS_NEED_UV
            float2 uv : TEXCOORD0;
        #endif
        #if defined(SHADOWS_CUBE)
            float3 lightVec : TEXCOORD1;
        #endif
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    inline float GetAlpha(in Interpolators i)
    {
        #if SHADOWS_NEED_UV
            return tex2D(_MainTex, i.uv.xy).a * _COLOR.a;
        #else
            return _COLOR.a;
        #endif
    }

    InterpolatorsVertex shadowVert(in appdata i)
    {
        InterpolatorsVertex o;

        UNITY_SETUP_INSTANCE_ID(i);
        UNITY_TRANSFER_INSTANCE_ID(i, o);

        #if defined(SHADOWS_CUBE)
            o.position = UnityObjectToClipPos(i.vertex);
            o.lightVec = mul(unity_ObjectToWorld, o.position).xyz - _LightPositionRange.xyz;
        #else
            o.position = UnityClipSpaceShadowCasterPos(i.vertex, i.normal);
            o.position = UnityApplyLinearShadowBias(o.position);
        #endif

        #if SHADOWS_NEED_UV
            o.uv = TRANSFORM_TEX(i.texcoord, _MainTex);
        #endif

        return o;
    }

    half4 shadowFrag(in Interpolators i) : SV_TARGET
    {
        UNITY_SETUP_INSTANCE_ID(i);

        half alpha = GetAlpha(i);
        #if defined(_RENDERING_CUTOUT)
            clip(alpha - _ALPHA_CUTOFF);
        #endif

        #if SHADOWS_SEMITRANSPARENT
            float dither = tex3D(_DitherMaskLOD, float3(i.vpos.xy * 0.25, alpha * 0.9375)).a;
            clip(dither - 0.0001);
        #endif

        #if defined(SHADOWS_CUBE)
            float depth = length(i.lightVec) + unity_LightShadowBias.x;
            depth *= _LightPositionRange.w;
            return UnityEncodeCubeShadowDepth(depth);
        #else
            return 0;
        #endif
    }

#endif