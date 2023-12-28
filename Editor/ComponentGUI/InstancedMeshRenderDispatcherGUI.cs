using System.Linq;
using UnityEditor;
using UnityEngine;
using static UnityEditor.EditorGUILayout;

namespace Com.Rendering.Editor
{
    [CustomEditor(typeof(InstancedMeshRenderDispatcher))]
    [CanEditMultipleObjects]
    public class InstancedMeshRenderDispatcherGUI : UnityEditor.Editor
    {
        static class LocalStyle
        {
            public static readonly GUIContent dispatcherName =
                new GUIContent("调度器的名称");
            //public static readonly GUIContent instanceMesh;
            //public static readonly GUIContent instanceMaterial;
            public static readonly GUIContent renderType =
                new GUIContent("RenderType", "着色器 RenderType 对应的内容，留空用着色器已有的");
            public static readonly GUIContent defaultRenderSystemCapacity =
                new GUIContent("缺省批次数", "缺省分配的缓冲区分段数目");
            public static readonly GUIContent defaultRenderSystemLargeCapacity =
                new GUIContent("大缓冲区批次数", "为大缓冲区准备的缺省分段数目");
            public static readonly GUIContent largeCapacityBatchSizeLimit =
                 new GUIContent("大缓冲区实例数", "每批次数目大于此值时将使用另外的缺省分段数目");

            //public static readonly GUIContent shadowCastingMode;
            //public static readonly GUIContent recieveShadows;
            //public static readonly GUIContent layer;
            //public static readonly GUIContent reflectionProbeUsage;
            //public static readonly GUIContent lightProbeProxyVolume;
            //public static readonly GUIContent lightProbeUsage;
        }

        bool foldout_primFields = true;
        SerializedProperty dispatcherName;
        SerializedProperty instanceMesh;
        SerializedProperty instanceMaterial;
        SerializedProperty renderType;
        SerializedProperty defaultRenderSystemCapacity;
        SerializedProperty defaultRenderSystemLargeCapacity;
        SerializedProperty largeCapacityBatchSizeLimit;

        bool foldout_editableRenderFields = true;
        SerializedProperty shadowCastingMode;
        SerializedProperty recieveShadows;
        SerializedProperty layer;
        SerializedProperty reflectionProbeUsage;
        SerializedProperty lightProbeProxyVolume;
        SerializedProperty lightProbeUsage;

        private void OnEnable()
        {
            dispatcherName = serializedObject.FindProperty("dispatcherName");
            instanceMesh = serializedObject.FindProperty("instanceMesh");
            instanceMaterial = serializedObject.FindProperty("instanceMaterial");
            renderType = serializedObject.FindProperty("renderType");
            defaultRenderSystemCapacity = serializedObject.FindProperty("defaultRenderSystemCapacity");
            defaultRenderSystemLargeCapacity = serializedObject.FindProperty("defaultRenderSystemLargeCapacity");
            largeCapacityBatchSizeLimit = serializedObject.FindProperty("largeCapacityBatchSizeLimit");

            shadowCastingMode = serializedObject.FindProperty("shadowCastingMode");
            recieveShadows = serializedObject.FindProperty("recieveShadows");
            layer = serializedObject.FindProperty("layer");
            reflectionProbeUsage = serializedObject.FindProperty("reflectionProbeUsage");
            lightProbeProxyVolume = serializedObject.FindProperty("lightProbeProxyVolume");
            lightProbeUsage = serializedObject.FindProperty("lightProbeUsage");
        }

        public override void OnInspectorGUI()
        {
            //base.OnInspectorGUI();
            serializedObject.Update();

            LabelField("最好在预制体初始化这些字段，视为运行期常量。");
            LabelField("以下所有字段会影响此调度器关联的所有绘制实例符号。");
            if (GUILayout.Button("重启场景中的组件（影响整个场景）"))
            {
                InstancedMeshRenderDispatcher.RestartGlobal();
            }

            Space(10);

            foldout_primFields = BeginFoldoutHeaderGroup(foldout_primFields,
                "决定组件使用的资源");
            if (foldout_primFields)
            {
                LabelField("需要重启场景的组件或者重新载入场景才能看到改动");
                PropertyField(dispatcherName, LocalStyle.dispatcherName);
                PropertyField(instanceMesh);
                PropertyField(instanceMaterial);
                PropertyField(renderType, LocalStyle.renderType);
                PropertyField(defaultRenderSystemCapacity, LocalStyle.defaultRenderSystemCapacity);
                GUILayout.Label("缓冲区长度总是 2 次幂，支持的缓冲区上限是 32768");
                GUILayout.Label("大缓冲区内存需求更大，建议专门配置");
                PropertyField(largeCapacityBatchSizeLimit, LocalStyle.largeCapacityBatchSizeLimit);
                PropertyField(defaultRenderSystemLargeCapacity, LocalStyle.defaultRenderSystemLargeCapacity);
            }
            EndFoldoutHeaderGroup();

            Space(10);

            foldout_editableRenderFields = BeginFoldoutHeaderGroup(foldout_editableRenderFields,
                "渲染相关的选项");
            if (foldout_editableRenderFields)
            {
                PropertyField(shadowCastingMode);
                PropertyField(recieveShadows);
                PropertyField(layer);
                PropertyField(reflectionProbeUsage);
                PropertyField(lightProbeProxyVolume);
                PropertyField(lightProbeUsage);
                if (GUILayout.Button("立即应用渲染相关的选项"))
                {
                    foreach (var t in targets.OfType<InstancedMeshRenderDispatcher>())
                    {
                        t.SetCommonFields();
                    }
                }
            }

            Space(10);
            if (GUILayout.Button("目前的缓冲区内存消耗"))
            {
                PrintUsedNativeMemory();
            }

            serializedObject.ApplyModifiedProperties();
        }

        [MenuItem("绘制实例系统/显示目前的缓冲区内存消耗")]
        static void PrintUsedNativeMemory()
        {
            const double _mb = 1024 * 1024;
            const double _kb = 1024;

            long usedMemory = InstancedMeshRenderDispatcher.GetNativeUsedMemory();
            if (usedMemory > _mb)
            {
                Debug.Log($"绘制实例系统非托管内存：{usedMemory / _mb:0.###} MB ({usedMemory} bytes)");
            }
            else if (usedMemory > _kb)
            {
                Debug.Log($"绘制实例系统非托管内存：{usedMemory / _kb:0.###} KB ({usedMemory} bytes)");
            }
            else
            {
                Debug.Log($"绘制实例系统非托管内存：{usedMemory} bytes");
            }
        }
    }
}