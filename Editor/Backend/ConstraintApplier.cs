using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using VRC.Dynamics;

namespace ConstrainTA.Editor.Backend
{
    internal static class ConstraintApplier
    {
        public static void RemapSourcesPreservingPerSourceData(
            VRCConstraintBase constraintBase,
            Transform outfitRoot,
            Transform armatureRoot,
            string parentPath,
            List<ConstraintData.SourceName> desiredSources)
        {
            if (constraintBase == null) return;
            desiredSources ??= new List<ConstraintData.SourceName>();

            var list = constraintBase.Sources;

            while (list.Count > desiredSources.Count)
            {
                list.RemoveAt(list.Count - 1);
            }
            while (list.Count < desiredSources.Count)
            {
                list.Add(new VRCConstraintSource(null, 0f));
            }

            for (int i = 0; i < desiredSources.Count; i++)
            {
                var s = desiredSources[i];
                var resolved = ResolveTransformWithFallback(outfitRoot, armatureRoot, s.pathFromArmature, parentPath, s.name);

                if (resolved == null)
                {
                    Debug.LogWarning($"[ConstraintApplier] Source not found: name='{s.name}' path='{s.pathFromArmature}' parent='{parentPath}' armature='{(armatureRoot ? armatureRoot.name : "(null)")}'");
                }

                var src = list[i];
                try
                {
                    src.SourceTransform = resolved;
                    src.Weight = resolved != null ? s.weight : 0f;
                }
                catch
                {
                    src = new VRCConstraintSource(resolved, resolved != null ? s.weight : 0f);
                }
                list[i] = src;
            }

            try
            {
                constraintBase.Sources = list;
            }
            catch
            {
            }
        }

        public static Component ResolveOrAddConstraintComponent(GameObject go, string constraintType)
        {
            if (go == null || string.IsNullOrEmpty(constraintType)) return null;

            var type = Type.GetType(constraintType);
            if (type == null)
            {
                var lastDot = constraintType.IndexOf(',');
                var shortName = lastDot >= 0 ? constraintType.Substring(0, lastDot) : constraintType;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        var t = asm.GetType(shortName, throwOnError: false);
                        if (t != null) { type = t; break; }
                    }
                    catch { }
                }
            }

            if (type == null) return null;

            var existing = go.GetComponent(type);
            if (existing != null) return existing;

            return Undo.AddComponent(go, type);
        }

        public static void SetTargetTransform(Component constraint, Transform target)
        {
            if (constraint == null) return;

            var prop = constraint.GetType().GetProperty("TargetTransform", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(constraint, target);
                return;
            }

            var field = constraint.GetType().GetField("TargetTransform", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field != null)
            {
                field.SetValue(constraint, target);
            }
        }

        private static Transform ResolveTransformWithFallback(Transform outfitRoot, Transform armatureRoot, string pathFromArmature, string parentPath, string name)
        {
            if (armatureRoot == null) return null;

            if (!string.IsNullOrEmpty(pathFromArmature))
            {
                var t = PathUtils.FindBySegments(armatureRoot, PathUtils.SplitPathSegments(pathFromArmature));
                if (t != null) return t;
            }

            if (!string.IsNullOrEmpty(name))
            {
                var byName = ResolveArmatureTransformByName(armatureRoot, name);
                if (byName != null) return byName;
            }

            return null;
        }

        private static Transform ResolveArmatureTransformByName(Transform armatureRoot, string name)
        {
            if (armatureRoot == null || string.IsNullOrEmpty(name)) return null;

            var key = BoneMaps.NormalizeKey(name);
            var lookupKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { key };

            if (BoneMaps.TryGetCanonical(key, out var canonical)) lookupKeys.Add(canonical);

            var tokenTry = BoneMaps.TryNormalizeBoneKey(name);
            if (!string.IsNullOrEmpty(tokenTry)) lookupKeys.Add(BoneMaps.NormalizeKey(tokenTry));

            foreach (var t in armatureRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                var tkey = BoneMaps.NormalizeKey(t.name);
                if (lookupKeys.Contains(tkey)) return t;
            }

            return null;
        }
    }
}
