Shader "BRP/Instanced/InstancedSimpleTransparent"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB) Alpha (A)", 2D) = "white" {}

        [Enum(UnityEngine.Rendering.CullMode)] _CullMode ("CullMode", float) = 2
        [Enum(Off, 0, On, 1)] _ZWriteMode ("ZWriteMode", float) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTestMode ("ZTestMode", Float) = 4
        [Enum(UnityEngine.Rendering.BlendOp)] _BlendOp ("BlendOp", Float) = 0
        // fade = SrcAlpha OneMinusSrcAlpha; transparent = One OneMinusSrcAlpha
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("SrcBlend", Float) = 1  // One
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("DstBlend", Float) = 10  // OneMinusSrcAlpha

        // https://docs.unity3d.com/cn/2020.3/ScriptReference/MaterialPropertyDrawer.html
        [KeywordEnum(None, CutOut, Fade, Transparent)] _Rendering ("Rendering mode", float) = 0
        _AlphaCutoff ("alpha cutoff for shadow", Range(0, 1)) = 0.5

        [Toggle(_NORMALMAP)] _NORMALMAP_ON ("use bump map", float) = 0
        [NoScaleOffset] [Normal] _BumpMap ("Bump map", 2D) = "bump" {}
        _BumpScale ("Scale of Bump map", Range(-10, 10)) = 1

        [Toggle(_METALLICGLOSSMAP)] _METALLICGLOSSMAP_ON ("use MetallicGloss map", float) = 0
        [NoScaleOffset] _MetallicGlossMap ("Metallic (R) Glossiness (A)", 2D) = "white" {}
        _GlossMapScale ("使用金属度-粗糙度贴图时控制平滑度", Range(0, 1)) = 1
        _Metallic ("Metallic", Range(0,1)) = 0.0
        _Glossiness ("Smoothness", Range(0,1)) = 0.5

        [Toggle(_EMISSION)] _EMISSION_ON ("use Emission", float) = 0
        [HDR] _EmissionColor ("Color of emission", Color) = (0, 0, 0, 1)
        [NoScaleOffset] _EmissionMap ("Emission map", 2D) = "white" {}

        [Toggle(INSTANCED_COLOR)] _tagInstancedColor("use color buffer", Int) = 1
    }
    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent"
            "IgnoreProjector"="True"
        }
        LOD 200

        Cull [_CullMode]
        ZWrite [_ZWriteMode]
        ZTest [_ZTestMode]
        Blend [_SrcBlend] [_DstBlend]
        BlendOp [_BlendOp]

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            BlendOp Add
            Blend One Zero
            ZWrite On
            Cull Off

            CGPROGRAM
            #pragma target 4.5
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling procedural:ConfigureProcedural
            #pragma editor_sync_compilation

            #include "UnityShaderVariables.cginc"

            #pragma shader_feature _RENDERING_NONE _RENDERING_CUTOUT _RENDERING_FADE _RENDERING_TRANSPARENT
            #pragma shader_feature INSTANCED_COLOR

            #if SHADER_TARGET >= 35 && (defined(SHADER_API_D3D11) || defined(SHADER_API_GLES3) || defined(SHADER_API_GLCORE) || defined(SHADER_API_XBOXONE) || defined(SHADER_API_PSSL) || defined(SHADER_API_SWITCH) || defined(SHADER_API_VULKAN) || (defined(SHADER_API_METAL) && defined(UNITY_COMPILER_HLSLCC)))
                #define SUPPORT_STRUCTUREDBUFFER
            #endif

            #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) && defined(SUPPORT_STRUCTUREDBUFFER)
                #define ENABLE_INSTANCING
            #endif
            
            #include "SimpleShadow.hlsl"

            #pragma vertex shadowVert
            #pragma fragment shadowFrag

            ENDCG
        }

        CGPROGRAM
        #pragma target 4.5
        #pragma surface surf Standard alpha
        #pragma multi_compile_instancing
        #pragma instancing_options assumeuniformscaling procedural:ConfigureProcedural
        #pragma editor_sync_compilation

        #pragma shader_feature _NORMALMAP
        #pragma shader_feature _METALLICGLOSSMAP
        #pragma shader_feature _EMISSION
        #pragma shader_feature INSTANCED_COLOR

        #include "UnityShaderVariables.cginc"

        #if SHADER_TARGET >= 35 && (defined(SHADER_API_D3D11) || defined(SHADER_API_GLES3) || defined(SHADER_API_GLCORE) || defined(SHADER_API_XBOXONE) || defined(SHADER_API_PSSL) || defined(SHADER_API_SWITCH) || defined(SHADER_API_VULKAN) || (defined(SHADER_API_METAL) && defined(UNITY_COMPILER_HLSLCC)))
            #define SUPPORT_STRUCTUREDBUFFER
        #endif

        #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) && defined(SUPPORT_STRUCTUREDBUFFER)
            #define ENABLE_INSTANCING
        #endif

        #include "DrawProceduralGPU.hlsl"
        #include "CommonSurf.hlsl"

        ENDCG
    }

    Fallback "Legacy Shaders/Transparent/VertexLit"
}
