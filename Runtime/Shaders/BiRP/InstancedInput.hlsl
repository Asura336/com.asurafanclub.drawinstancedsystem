#ifndef ASURAFANCLUB_INSTANCED_INPUT_INCLUDED
#define ASURAFANCLUB_INSTANCED_INPUT_INCLUDED

// buffer for DrawMeshInstancedProcedural
#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
    StructuredBuffer<float4x4> _LocalToWorldBuffer;
    StructuredBuffer<float4x4> _WorldToLocalBuffer;
#endif

void ConfigureProcedural()
{
    #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
        unity_ObjectToWorld = _LocalToWorldBuffer[unity_InstanceID];
        unity_WorldToObject = _WorldToLocalBuffer[unity_InstanceID];
    #endif

    #if SHADERPASS == SHADERPASS_MOTION_VECTORS && defined(SHADERPASS_CS_HLSL)
        unity_MatrixPreviousM = unity_ObjectToWorld;
        unity_MatrixPreviousMI = unity_WorldToObject;
    #endif
}

#endif