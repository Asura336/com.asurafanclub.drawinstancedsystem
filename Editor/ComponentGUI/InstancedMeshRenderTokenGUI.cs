using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Com.Rendering.Editor
{
    [CustomEditor(typeof(InstancedMeshRenderToken))]
    [CanEditMultipleObjects]
    internal class InstancedMeshRenderTokenGUI : UnityEditor.Editor
    {
#pragma warning disable IDE1006 // 命名样式
        static readonly GUILayoutOption notExpandWidth = GUILayout.ExpandWidth(false);
        static readonly GUILayoutOption expandWidth = GUILayout.ExpandWidth(true);

        static GUIStyle s_style_richText;
        public static GUIStyle style_richText => s_style_richText ??= new GUIStyle(GUI.skin.label)
        {
            richText = true,
        };
#pragma warning restore IDE1006 // 命名样式


        SerializedProperty dispatcherName;
        SerializedProperty color;
        SerializedProperty localBounds;
        SerializedProperty transformStatic;

        // before play
        SerializedProperty batchSize;

        // read only
        SerializedProperty count;
        SerializedProperty virtualBatchIndex;

        private void OnEnable()
        {
            dispatcherName = serializedObject.FindProperty("dispatcherName");
            color = serializedObject.FindProperty("color");
            localBounds = serializedObject.FindProperty("localBounds");
            transformStatic = serializedObject.FindProperty("transformStatic");

            batchSize = serializedObject.FindProperty("batchSize");

            count = serializedObject.FindProperty("count");
            virtualBatchIndex = serializedObject.FindProperty("virtualBatchIndex");
        }

        public override void OnInspectorGUI()
        {
            //base.OnInspectorGUI();

            serializedObject.Update();

            // 字段...
            EditorGUILayout.PropertyField(dispatcherName, new GUIContent("调度器的名称"));
            if (InstancedMeshRenderDispatcher.FindInstanceOrNothing(dispatcherName.stringValue) == null)
            {
                GUILayout.Label($"<color=yellow>场景中没有调度器 \"{dispatcherName.stringValue}\"</color>",
                    style_richText);
            }
            EditorGUILayout.PropertyField(color, new GUIContent("此批次的颜色"));
            EditorGUILayout.PropertyField(localBounds, new GUIContent("本地包围盒需要包住此批次的所有实例"));
            EditorGUILayout.PropertyField(transformStatic, new GUIContent("停止自动更新位置"));
            if (transformStatic.hasMultipleDifferentValues
               || transformStatic.boolValue)
            {
                if (GUILayout.Button("立即同步变换"))
                {
                    foreach (var t in targets.OfType<InstancedMeshRenderToken>())
                    {
                        t.UpdateLocalToWorld();
                    }
                }
            }

            EditorGUILayout.PropertyField(batchSize, new GUIContent("预期分配的变换矩阵缓冲区长度"));

            LabelLine("此批次绘制的实例数：", count.intValue.ToString());
            LabelLine("此批次的 id：", virtualBatchIndex.intValue.ToString());


            if (targets.OfType<InstancedMeshRenderToken>().Any(t => !t.IsSingleInstance))
            {
                GUILayout.Space(10);
                if (GUILayout.Button("设置为单实例"))
                {
                    foreach (var t in targets.OfType<InstancedMeshRenderToken>())
                    {
                        t.MakeSingleInstance();
                    }
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        static void LabelLine(string title, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(title, notExpandWidth);
            GUILayout.Label(value);
            GUILayout.EndHorizontal();
        }
    }
}