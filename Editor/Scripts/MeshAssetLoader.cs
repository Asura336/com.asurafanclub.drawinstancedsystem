using UnityEditor;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;
using static Com.Rendering.Editor.InternalTools;

namespace Com.Rendering.Editor
{
    internal class MeshAssetLoader
    {
        public const string opaqueMaterialPath = EditorConstants.packagePath + "/Editor/Materials/BRP_Instanced_InstancedSimpleDiffuse.mat";
        public const string transparentMaterialPath = EditorConstants.packagePath + "/Editor/Materials/BRP_Instanced_InstancedSimpleTransparent.mat";

        public static Material MatchInstancedMaterial(Material src)
        {
            bool transparentMat = src.renderQueue > (int)RenderQueue.AlphaTest - 1;
            string matAssetPath = transparentMat
                ? transparentMaterialPath
                : opaqueMaterialPath;
            var matAsset = AssetDatabase.LoadAssetAtPath<Material>(matAssetPath);
            Assert.IsTrue(matAsset, matAssetPath);

            Material o = new Material(matAsset)
            {
                enableInstancing = false,  // using procedural instancing
                mainTexture = src.mainTexture,
                mainTextureScale = src.mainTextureScale,
                mainTextureOffset = src.mainTextureOffset,
            };
            CopyProperties(src, o, transparentMat);
            return o;
        }
    }
}