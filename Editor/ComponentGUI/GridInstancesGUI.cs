using System.Linq;
using UnityEditor;
using UnityEngine;
using static UnityEditor.EditorGUILayout;

namespace Com.Rendering.Editor
{
    [CustomEditor(typeof(GridInstances))]
    [CanEditMultipleObjects]
    public class GridInstancesGUI : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();
            Space(10);
            if (GUILayout.Button("Ӧ�øĶ�"))
            {
                foreach (var t in targets.OfType<BaseGridInstances>())
                {
                    t.ApplyMatricesAndBounds();
                }
            }
            serializedObject.ApplyModifiedProperties();
        }
    }
}