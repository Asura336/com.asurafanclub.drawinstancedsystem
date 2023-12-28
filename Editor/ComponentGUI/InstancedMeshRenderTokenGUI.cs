using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Com.Rendering.Editor
{
    [CustomEditor(typeof(InstancedMeshRenderToken))]
    [CanEditMultipleObjects]
    internal class InstancedMeshRenderTokenGUI : UnityEditor.Editor
    {
        class LocalStyle
        {
            public static readonly GUIContent dispatcherName =
                 new GUIContent("调度器的名称");
            public static readonly GUIContent color =
                 new GUIContent("此批次的颜色");
            //public static readonly GUIContent localBounds;
            public static readonly GUIContent transformStatic =
                 new GUIContent("停止自动更新位置");
            public static readonly GUIContent batchSize =
                 new GUIContent("预期分配的变换矩阵缓冲区长度");
            //public static readonly GUIContent count;
            //public static readonly GUIContent virtualBatchIndex;
        }

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
            serializedObject.Update();

            // 字段...
            EditorGUILayout.PropertyField(dispatcherName, LocalStyle.dispatcherName);
            var dispatcher = InstancedMeshRenderDispatcher.FindInstanceOrNothing(dispatcherName.stringValue);
            if (dispatcher != null)
            {
                GUI.enabled = false;
                EditorGUILayout.ObjectField(new GUIContent("Mesh"),
                    dispatcher.InstancedMesh, typeof(Mesh), true);
                EditorGUILayout.ObjectField(new GUIContent("Material"),
                    dispatcher.InstancedMaterial, typeof(Material), true);
                GUI.enabled = true;

                if (GUILayout.Button("选择调度器"))
                {
                    Selection.activeObject = dispatcher.gameObject;
                }
            }
            else
            {
                GUILayout.Label($"<color=yellow>场景中没有调度器 \"{dispatcherName.stringValue}\"</color>",
                    style_richText);
            }
            EditorGUILayout.PropertyField(color, LocalStyle.color);
            GUILayout.Label("本地包围盒需要包住此批次的所有实例");
            EditorGUILayout.PropertyField(localBounds);
            EditorGUILayout.PropertyField(batchSize, LocalStyle.batchSize);

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

            if (GUILayout.Button("应用改动"))
            {
                foreach (var t in targets.OfType<InstancedMeshRenderToken>())
                {
                    t.CheckDispatch();
                }
            }

            EditorGUILayout.PropertyField(transformStatic, LocalStyle.transformStatic);
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

            LabelLine("此批次绘制的实例数：", count.intValue.ToString());
            LabelLine("此批次的 id：", virtualBatchIndex.intValue.ToString());

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