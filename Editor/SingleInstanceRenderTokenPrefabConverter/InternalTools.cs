using System;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;

namespace Com.Rendering.Editor
{
    internal static class InternalTools
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void CombineHash(int* seed, int hashValue)
        {
            // boost combine hash
            // seed ^= hash_value(v) + 0x9e3779b9 + (seed << 6) + (seed >> 2);
            uint useed = *(uint*)seed;
            uint v = *(uint*)&hashValue + 0x9e3779b9 + (useed << 6) + (useed >> 2);
            *seed ^= *(int*)&v;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe int CombineHash(int a, int b)
        {
            int seed = 0;
            CombineHash(&seed, a);
            CombineHash(&seed, b);
            return seed;
        }

        public static IEnumerable<GameObject> ItorGameObjects(this GameObject gameObject)
        {
            yield return gameObject;
            Transform t = gameObject.transform;
            foreach (Transform ct in t)
            {
                GameObject g = ct.gameObject;
                yield return g;
                foreach (GameObject cg in ItorGameObjects(g))
                {
                    yield return cg;
                }
            }
        }

        /// <summary>
        /// https://forum.unity.com/threads/how-to-get-currently-selected-folder-for-putting-new-asset-into.81359
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public static string AssetFolderPathOrNothing(this UnityEngine.Object obj)
        {
            if (obj)
            {
                string clickedPath = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(clickedPath))
                {
                    return null;
                }
                string clickedPathFull = Path.Combine(Directory.GetCurrentDirectory(), clickedPath);
                var fileAttr = File.GetAttributes(clickedPathFull);
                string folderPath = ((fileAttr & FileAttributes.Directory) == FileAttributes.Directory)
                    ? clickedPath
                    : Path.GetDirectoryName(clickedPath);
                if (folderPath.IndexOf('\\') != -1)
                {
                    folderPath = folderPath.Replace('\\', '/');
                }
                if (folderPath[folderPath.Length - 1] == '/')
                {
                    folderPath = folderPath.Substring(0, folderPath.Length - 1);
                }
                return folderPath;
            }
            return null;
        }

        public static Expression<Func<T, V, V>> SetInstanceFieldExpression<T, V>(string fieldName)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            // (self, value) => self.field = value
            var self = Expression.Parameter(typeof(T), "self");
            var value = Expression.Parameter(typeof(V), "value");
            var field = Expression.Field(self, typeof(T).GetField(fieldName, flags));
            var assign = Expression.Assign(field, value);
            return Expression.Lambda<Func<T, V, V>>(assign, self, value);
        }

        public readonly struct InstanceFieldOperator<T, V>
        {
            static readonly Dictionary<string, Func<T, V, V>> setFieldDelegates = new Dictionary<string, Func<T, V, V>>();
            readonly Func<T, V, V> setFieldDelegate;

            public InstanceFieldOperator(string name)
            {
                if (!setFieldDelegates.TryGetValue(name, out setFieldDelegate))
                {
                    setFieldDelegate = SetInstanceFieldExpression<T, V>(name).Compile();
                    setFieldDelegates[name] = setFieldDelegate;
                }
            }

            public V SetValue(T self, V value) => setFieldDelegate(self, value);
        }

        #region 材质属性兼容性检查
        // 法线贴图和金属度-粗糙度贴图参与兼容性检查
        static readonly int key_BumpMap = Shader.PropertyToID("_BumpMap");
        static readonly int key_BumpScale = Shader.PropertyToID("_BumpScale");

        static readonly int key_Metallic = Shader.PropertyToID("_Metallic");
        static readonly int key_Glossiness = Shader.PropertyToID("_Glossiness");
        static readonly int key_MetallicGlossMap = Shader.PropertyToID("_MetallicGlossMap");
        static readonly int key_GlossMapScale = Shader.PropertyToID("_GlossMapScale");

        // 外发光有就设置，没有也不管
        static readonly int key_EmissionColor = Shader.PropertyToID("_EmissionColor");

        // 剔除方向，读写深度等，只写
        static readonly int key_CullMode = Shader.PropertyToID("_CullMode");
        static readonly int key_ZWrite = Shader.PropertyToID("_ZWrite");
        static readonly int key_ZWriteMode = Shader.PropertyToID("_ZWriteMode");
        static readonly int key_ZTestMode = Shader.PropertyToID("_ZTestMode");

        // 半透明混合
        static readonly int key_SrcBlend = Shader.PropertyToID("_SrcBlend");
        static readonly int key_DstBlend = Shader.PropertyToID("_DstBlend");
        static readonly int key_BlendOp = Shader.PropertyToID("_BlendOp");

        static readonly int key_AlphaCutoff = Shader.PropertyToID("_AlphaCutoff");

        static readonly Dictionary<string, string> keyword_ON = new Dictionary<string, string>();
        /// <summary>
        /// 按照着色器的属性声明习惯，映射到关键字对应的开关属性名称
        /// </summary>
        /// <param name="keyword"></param>
        /// <returns></returns>
        static string GetKeyword_ON(string keyword)
        {
            if (!keyword_ON.TryGetValue(keyword, out string value))
            {
                value = keyword + "_ON";
                keyword_ON.Add(keyword, value);
            }
            return value;
        }

        public static bool IsMaterialHasNecessarypProperties(Material material)
        {
            return material.HasProperty(key_BumpMap)
                && material.HasProperty(key_BumpScale)
                && material.HasProperty(key_Metallic)
                && material.HasProperty(key_Glossiness)
                && material.HasProperty(key_MetallicGlossMap);
        }

        public static void CopyProperties(Material src, Material dst, bool transparent)
        {
            SyncKeyword(src, dst, "_NORMALMAP");
            SyncTexture(src, dst, key_BumpMap);
            SyncFloat(src, dst, key_BumpScale);

            SyncFloat(src, dst, key_Metallic);
            SyncFloat(src, dst, key_Glossiness);
            SyncKeyword(src, dst, "_METALLICGLOSSMAP");
            SyncTexture(src, dst, key_MetallicGlossMap);
            SyncFloat(src, dst, key_GlossMapScale);

            SyncKeyword(src, dst, "_EMISSION");
            SyncColor(src, dst, key_EmissionColor);

            SyncFloat(src, dst, key_CullMode);
            SyncFloat(src, dst, key_ZWrite);
            SyncFloat(src, dst, key_ZWriteMode);
            SyncFloat(src, dst, key_ZTestMode);

            if (transparent)
            {
                SyncFloat(src, dst, key_SrcBlend);
                SyncFloat(src, dst, key_DstBlend);
                SyncFloat(src, dst, key_BlendOp);

                SyncFloat(src, dst, key_AlphaCutoff);
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SyncKeyword(Material src, Material dst, string keyword)
        {
            if (src.IsKeywordEnabled(keyword))
            {
                dst.EnableKeyword(keyword);
                dst.SetFloat(GetKeyword_ON(keyword), 1);
            }
            else
            {
                dst.DisableKeyword(keyword);
                dst.SetFloat(GetKeyword_ON(keyword), 0);
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SyncTexture(Material src, Material dst, int propID)
        {
            if (src.HasProperty(propID))
            {
                dst.SetTexture(propID, src.GetTexture(propID));
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SyncFloat(Material src, Material dst, int propID)
        {
            if (src.HasProperty(propID))
            {
                dst.SetFloat(propID, (float)src.GetFloat(propID));
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SyncColor(Material src, Material dst, int propID)
        {
            if (src.HasProperty(propID))
            {
                dst.SetColor(propID, src.GetColor(propID));
            }
        }
        #endregion
    }
}