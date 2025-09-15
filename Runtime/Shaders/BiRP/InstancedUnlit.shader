Shader "BRP/InstancedUnlit"
{
    Properties
    {
        [MainColor] _Color ("Color", Color) = (1,1,1,1)
        [MainTexture] _MainTex ("Texture", 2D) = "white" {}
        
        [Enum(UnityEngine.Rendering.CullMode)] _CullMode ("CullMode", float) = 2
        [Enum(Off, 0, On, 1)] _ZWriteMode ("ZWriteMode", float) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTestMode ("ZTestMode", Float) = 4
        [Enum(UnityEngine.Rendering.BlendOp)] _BlendOp ("BlendOp", Float) = 0
        // fade = SrcAlpha OneMinusSrcAlpha; transparent = One OneMinusSrcAlpha
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("SrcBlend", Float) = 1  // One
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("DstBlend", Float) = 10  // OneMinusSrcAlpha

        [Toggle(INSTANCED_COLOR)] _tagInstancedColor("use color buffer", Int) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Cull [_CullMode]
        ZWrite [_ZWriteMode]
        ZTest [_ZTestMode]
        Blend [_SrcBlend] [_DstBlend]
        BlendOp [_BlendOp]

        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling procedural:ConfigureProcedural
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog
            #pragma multi_compile _ PROCEDURAL_INSTANCING_ON
            #pragma shader_feature INSTANCED_COLOR

            #include "UnityCG.cginc"
            #include "UnityShaderVariables.cginc"

            #if SHADER_TARGET >= 35 && (defined(SHADER_API_D3D11) || defined(SHADER_API_GLES3) || defined(SHADER_API_GLCORE) || defined(SHADER_API_XBOXONE) || defined(SHADER_API_PSSL) || defined(SHADER_API_SWITCH) || defined(SHADER_API_VULKAN) || (defined(SHADER_API_METAL) && defined(UNITY_COMPILER_HLSLCC)))
                #define SUPPORT_STRUCTUREDBUFFER
            #endif
    
            #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) && defined(SUPPORT_STRUCTUREDBUFFER)
                #define ENABLE_INSTANCING
            #endif
            
            #include "InstancedInput.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            sampler2D _MainTex;
            cbuffer UnityPerMaterial
            {
                float4 _MainTex_ST;
                half4 _Color;
                half _CutOff;
            }

#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) && defined(INSTANCED_COLOR)
    StructuredBuffer<half4> _BaseColors;
    #define _BaseColor _BaseColors[unity_InstanceID]
#else
    #define _BaseColor _Color
#endif

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
            
                // sample the texture
                fixed4 col = tex2D(_MainTex, i.uv);
                col *= _BaseColor;
                // apply fog
                UNITY_APPLY_FOG(i.fogCoord, col);
                
                return col;
            }
            ENDCG
        }
    }
}
