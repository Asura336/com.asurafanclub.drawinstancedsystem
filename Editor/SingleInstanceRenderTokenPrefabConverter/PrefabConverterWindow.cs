using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using static Com.Rendering.Editor.InternalTools;
using static UnityEngine.GUILayout;

namespace Com.Rendering.Editor
{
    public class PrefabConverterWindow : EditorWindow
    {
        [MenuItem("Tools/Prefab/为预制体应用绘制实例符号")]
        public static void ShowExample()
        {
            PrefabConverterWindow wnd = GetWindow<PrefabConverterWindow>();
            wnd.titleContent = new GUIContent("为预制体应用绘制实例符号");
            var skin = AssetDatabase.LoadAssetAtPath<GUISkin>(guiSkinPath);
            if (skin)
            {
                wnd.guiSkin = skin;
            }
        }


        const string guiSkinPath = EditorConstants.packagePath + "/Editor/GUI/PrefabConverterWindow.guiskin";

        static readonly GUIContent notification_srcIsNothing =
            new GUIContent("选择一个预制体");
        static readonly GUIContent notification_meshMatPairIsEmpty =
            new GUIContent("预制体下没有检索到可用的网格材质对");

        // global options
        static bool saveOutputDependenciesToSubFolder = false;

        // fields
        GameObject src;
        string outputPath;

        // caches
        readonly Dictionary<Mesh_MaterialPair, string> pairToDispatcherName = new Dictionary<Mesh_MaterialPair, string>();
        Mesh_MaterialPair[] pairKeyCollection;
        string[] pairKeyToStringCache;
        string[] pairValueCollection;

        // ui states
        GUISkin guiSkin;
        bool searchButtonClicked = false;
        Vector2 scrollPosition;


        private void OnEnable()
        {
            searchButtonClicked = false;
        }

        private void OnDisable()
        {
            src = null;
            pairToDispatcherName.Clear();
            pairKeyCollection = null;
            pairKeyToStringCache = null;
            pairValueCollection = null;
            outputPath = null;
        }

        void OnGUI()
        {
            GUI.skin = guiSkin;
            BeginVertical();
            {
                Label("检索预制体上的网格和网格渲染器，生成使用实例绘制符号的新预制体和对应的绘制实例调度器。" +
                    "不支持有多个子网格的网格实例");

                Space(10);
                src = EditorGUILayout.ObjectField(src, typeof(GameObject), true) as GameObject;
                if (!src)
                {
                    ShowNotification(notification_srcIsNothing);
                }
                else
                {
                    if (Button("检索/重新检索预制体"))
                    {
                        SearchPrefab();
                        outputPath = src.AssetFolderPathOrNothing();
                        searchButtonClicked = true;
                    }
                    if (pairKeyCollection is null)
                    {
                        if (searchButtonClicked)
                        {
                            ShowNotification(notification_meshMatPairIsEmpty);
                        }
                    }
                    else
                    {
                        Space(10);
                        Label("以下若干行，左侧为收集到的网格和材质对，不完全兼容的材质会<color=cyan>标记</color>出来。" +
                            "右侧是预定生成的调度器名称，在输出之前有机会修改调度器名称。" +
                            "如果不需要替换某些部分，将右侧的调度器名称置为空即可在输出时跳过。");
                        scrollPosition = BeginScrollView(scrollPosition, ExpandWidth(false));
                        int length = pairKeyCollection.Length;
                        for (int i = 0; i < length; i++)
                        {
                            BeginHorizontal();
                            var (_, material) = pairKeyCollection[i];
                            bool fitToInstancedMat = IsMaterialHasNecessarypProperties(material);
                            string itemName = fitToInstancedMat
                                ? pairKeyToStringCache[i]
                                : $"<color=cyan>{pairKeyToStringCache[i]}</color>";
                            Label(itemName);
                            pairValueCollection[i] = TextField(pairValueCollection[i]);
                            EndHorizontal();
                        }
                        EndScrollView();
                        Space(10);
                        Label("确认输出目录：");
                        outputPath = TextField(outputPath);
                        if (Button("从选择的项目获取目录"))
                        {
                            var selects = Selection.GetFiltered<UnityEngine.Object>(SelectionMode.Assets);
                            outputPath = selects.Length == 0
                                ? "Assets"
                                : selects[0].AssetFolderPathOrNothing();
                        }
                        Label($"将输出到 \"{outputPath}\" 下");

                        Space(10);
                        saveOutputDependenciesToSubFolder = Toggle(saveOutputDependenciesToSubFolder,
                            "将生成的调度器和材质等放在下级目录");
                        if (!string.IsNullOrEmpty(outputPath))
                        {
                            if (Button("输出"))
                            {
                                OutputProcess();
                            }
                        }
                    }
                }
            }
            EndVertical();
        }

        void SearchPrefab()
        {
            pairToDispatcherName.Clear();
            pairKeyCollection = null;
            pairKeyToStringCache = null;
            pairValueCollection = null;
            foreach (var (meshF, meshR) in SearchMeshRenderers(src))
            {
                var mesh = meshF.sharedMesh;
                var mat = meshR.sharedMaterial;
                // 赋予一个初始名字
                pairToDispatcherName[(mesh, mat)] = $"{src.name}_{mesh.name}_{mat.name}";
            }
            if (pairToDispatcherName.Count != 0)
            {
                pairKeyCollection = pairToDispatcherName.Keys.ToArray();
                pairKeyToStringCache = pairKeyCollection.Select(x => x.ToString()).ToArray();
                pairValueCollection = pairToDispatcherName.Values.ToArray();
            }
        }

        void OutputProcess()
        {
            OutputProcess_EntityPrefab();
            OutputProcess_DependencyAssets();
            AssetDatabase.SaveAssets();
        }

        void OutputProcess_EntityPrefab()
        {
            GameObject ins = Instantiate(src);
            if (PrefabUtility.IsAnyPrefabInstanceRoot(src))
            {
                PrefabUtility.UnpackPrefabInstance(ins, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            }

            // 转换...
            try
            {
                // 内容在输出前可能编辑过，先同步数据
                int length = pairKeyCollection.Length;
                for (int i = 0; i < length; i++)
                {
                    pairToDispatcherName[pairKeyCollection[i]] = pairValueCollection[i];
                }

                foreach (var (meshF, meshR) in SearchMeshRenderers(ins))
                {
                    var gObj = meshF.gameObject;
                    var mesh = meshF.sharedMesh;
                    var mat = meshR.sharedMaterial;
                    string dispatcherName = pairToDispatcherName[(mesh, mat)];
                    if (!string.IsNullOrEmpty(dispatcherName))
                    {
                        var token = gObj.AddComponent<InstancedMeshRenderToken>();
                        new InstanceFieldOperator<InstancedMeshRenderToken, Transform>("cachedTransform")
                            .SetValue(token, token.transform);
                        token.DispatcherName = dispatcherName;
                        new InstanceFieldOperator<InstancedMeshRenderToken, int>("batchSize")
                            .SetValue(token, 1);
                        new InstanceFieldOperator<InstancedMeshRenderToken, int>("count")
                            .SetValue(token, 1);
                        token.LocalBounds = mesh.bounds;
                        token.InstanceColor = mat.color;

                        DestroyImmediate(meshF);
                        DestroyImmediate(meshR);
                    }
                }

                // save prefab
                string prefabPath = outputPath + $"/{src.name}_Instanced.prefab";
                prefabPath = AssetDatabase.GenerateUniqueAssetPath(prefabPath);
                PrefabUtility.SaveAsPrefabAsset(ins, prefabPath);
                AssetDatabase.Refresh();
            }
            catch
            {
                throw;
            }
            finally
            {
                DestroyImmediate(ins);
            }
        }

        void OutputProcess_DependencyAssets()
        {
            // 保存材质和调度器
            string dependenciesPath = outputPath;
            if (saveOutputDependenciesToSubFolder)
            {
                string folderGUID = AssetDatabase.CreateFolder(outputPath, $"{src.name}_Dispatchers");
                dependenciesPath = AssetDatabase.GUIDToAssetPath(folderGUID);
            }

            var dispatchersGObj = new GameObject($"{src.name}_Dispatchers");
            var dispatchersTransform = dispatchersGObj.transform;
            int length = pairKeyCollection.Length;
            for (int i = 0; i < length; i++)
            {
                var (mesh, material) = pairKeyCollection[i];
                var dispatcherName = pairValueCollection[i];

                if (!string.IsNullOrEmpty(dispatcherName))
                {
                    // 假定是标准表面着色器，或者其他沿用了内置渲染管线命名规则的材质
                    Material instancedMaterial = MeshAssetLoader.MatchInstancedMaterial(material);
                    instancedMaterial.name = $"{dispatcherName}_Instanced";
                    AssetDatabase.CreateAsset(instancedMaterial, $"{dependenciesPath}/{instancedMaterial.name}.mat");

                    var dispatcherGObj = new GameObject(dispatcherName);
                    dispatcherGObj.transform.SetParent(dispatchersTransform, false);
                    var dispatcher = dispatcherGObj.AddComponent<InstancedMeshRenderDispatcher>();
                    new InstanceFieldOperator<InstancedMeshRenderDispatcher, string>("dispatcherName")
                        .SetValue(dispatcher, dispatcherName);
                    new InstanceFieldOperator<InstancedMeshRenderDispatcher, Mesh>("instanceMesh")
                        .SetValue(dispatcher, mesh);
                    new InstanceFieldOperator<InstancedMeshRenderDispatcher, Material>("instanceMaterial")
                        .SetValue(dispatcher, instancedMaterial);
                }
            }

            // save prefab
            string prefabPath = dependenciesPath + $"/{dispatchersGObj.name}.prefab";
            prefabPath = AssetDatabase.GenerateUniqueAssetPath(prefabPath);
            PrefabUtility.SaveAsPrefabAsset(dispatchersGObj, prefabPath);
            AssetDatabase.Refresh();

            DestroyImmediate(dispatchersGObj);
        }

        static IEnumerable<(MeshFilter meshF, MeshRenderer meshR)> SearchMeshRenderers(GameObject root)
        {
            foreach (GameObject g in root.ItorGameObjects())
            {
                MeshFilter meshF = g.GetComponent<MeshFilter>();
                MeshRenderer meshR = g.GetComponent<MeshRenderer>();
                if (meshF && meshR)
                {
                    if (!meshF.sharedMesh)
                    {
                        Debug.Log($"在 {g.name} 有网格为空的 MeshFilter，跳过。");
                        continue;
                    }
                    if (!meshR.sharedMaterial)
                    {
                        Debug.Log($"在 {g.name} 缺失材质，跳过。");
                        continue;
                    }

                    var mesh = meshF.sharedMesh;
                    if (mesh.subMeshCount != 1)
                    {
                        Debug.Log($"在 {g.name} 有超过 1 的子网格，跳过。");
                        continue;
                    }
                    yield return (meshF, meshR);
                }
            }
        }
    }
}