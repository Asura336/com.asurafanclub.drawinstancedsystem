#ifndef ASURAFANCLUB_INSTANCED_SHADER_GRAPH
#define ASURAFANCLUB_INSTANCED_SHADER_GRAPH

#pragma editor_sync_compilation
#pragma multi_compile_instancing
#pragma multi_compile _ LOD_FADE_CROSSFADE
#pragma shader_feature_local INSTANCED_COLOR
#pragma instancing_options renderinglayer procedural:ConfigureProcedural
#define UNITY_INSTANCING_PROCEDURAL_FUNC ConfigureProcedural

#if SHADER_TARGET >= 35 && (defined(SHADER_API_D3D11) || defined(SHADER_API_GLES3) || defined(SHADER_API_GLCORE) || defined(SHADER_API_XBOXONE) || defined(SHADER_API_PSSL) || defined(SHADER_API_SWITCH) || defined(SHADER_API_VULKAN) || (defined(SHADER_API_METAL) && defined(UNITY_COMPILER_HLSLCC)))
    #define SUPPORT_STRUCTUREDBUFFER
#endif

// buffer for DrawMeshInstancedProcedural
#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) && defined(SUPPORT_STRUCTUREDBUFFER)
    #define ENABLE_INSTANCING
    StructuredBuffer<float4x4> _LocalToWorldBuffer;
    StructuredBuffer<float4x4> _WorldToLocalBuffer;
    // 如果要构建颜色数组替代材质实例颜色，在这里写然后替代后续的 _BaseColor
    // 反正实例化绘制不兼容 SRP batcher...
    #if defined(INSTANCED_COLOR)
    StructuredBuffer<half4> _BaseColors;
    #define _BaseColor _BaseColors[unity_InstanceID]
    #endif
#endif

void ConfigureProcedural()
{
    #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
        UNITY_MATRIX_M = _LocalToWorldBuffer[unity_InstanceID];
        UNITY_MATRIX_I_M = _WorldToLocalBuffer[unity_InstanceID];
    #endif
}

void PositionJust_float(float3 IN, out float3 OUT)
{
    OUT = IN;
}

#endif  // ASURAFANCLUB_INSTANCED_SHADER_GRAPH