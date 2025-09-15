#ifndef _ASURAFANCLUB_INSTANCED_PROCEDURAL
    #define _ASURAFANCLUB_INSTANCED_PROCEDURAL

    // DrawMeshInstancedProcedural
    #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
        StructuredBuffer<float4x4> _LocalToWorldBuffer;
        StructuredBuffer<float4x4> _WorldToLocalBuffer;
    #endif

    //// https://answers.unity.com/questions/218333/shader-inversefloat4x4-function.html
    //inline float4x4 inverse(in float4x4 input)
    //{
    //    #define minor(a,b,c) determinant(float3x3(input.a, input.b, input.c))
    //    //determinant(float3x3(input._22_23_23, input._32_33_34, input._42_43_44))
    //    
    //    float4x4 cofactors = float4x4(
    //    minor(_22_23_24, _32_33_34, _42_43_44),
    //    -minor(_21_23_24, _31_33_34, _41_43_44),
    //    minor(_21_22_24, _31_32_34, _41_42_44),
    //    -minor(_21_22_23, _31_32_33, _41_42_43),
    //    
    //    -minor(_12_13_14, _32_33_34, _42_43_44),
    //    minor(_11_13_14, _31_33_34, _41_43_44),
    //    -minor(_11_12_14, _31_32_34, _41_42_44),
    //    minor(_11_12_13, _31_32_33, _41_42_43),
    //    
    //    minor(_12_13_14, _22_23_24, _42_43_44),
    //    -minor(_11_13_14, _21_23_24, _41_43_44),
    //    minor(_11_12_14, _21_22_24, _41_42_44),
    //    -minor(_11_12_13, _21_22_23, _41_42_43),
    //    
    //    -minor(_12_13_14, _22_23_24, _32_33_34),
    //    minor(_11_13_14, _21_23_24, _31_33_34),
    //    -minor(_11_12_14, _21_22_24, _31_32_34),
    //    minor(_11_12_13, _21_22_23, _31_32_33)
    //    );
    //    #undef minor
    //    return transpose(cofactors) / determinant(input);
    //}

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