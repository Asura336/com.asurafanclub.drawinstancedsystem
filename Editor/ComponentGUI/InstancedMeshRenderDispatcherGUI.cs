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
        bool foldout_primFields = true;
        SerializedProperty dispatcherName;
        SerializedProperty instanceMesh;
        SerializedProperty instanceMaterial;
        SerializedProperty renderType;
        SerializedProperty defaultRenderSystemCapacity;

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
                PropertyField(dispatcherName, new GUIContent("调度器的名称"));
                PropertyField(instanceMesh);
                PropertyField(instanceMaterial);
                PropertyField(renderType, new GUIContent("着色器 RenderType 对应的内容，留空用着色器已有的"));
                PropertyField(defaultRenderSystemCapacity, new GUIContent("缺省分配的缓冲区分段数目"));
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
                if (GUILayout.Button("立即应用这部分字段"))
                {
                    foreach (var t in targets.OfType<InstancedMeshRenderDispatcher>())
                    {
                        t.SetCommonFields();
                    }
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}