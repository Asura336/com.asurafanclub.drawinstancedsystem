using System.Linq;
using UnityEditor;
using UnityEngine;
using static UnityEditor.EditorGUILayout;

namespace Com.Rendering.Editor
{
    [CustomEditor(typeof(Grid2DInstances))]
    [CanEditMultipleObjects]
    public class Grid2DInstancesGUI : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();
            Space(10);
            if (GUILayout.Button("应用改动"))
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