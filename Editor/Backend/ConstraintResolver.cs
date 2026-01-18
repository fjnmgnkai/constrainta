using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace ConstrainTA.Editor.Backend
{
    internal static class ConstraintResolver
    {
        // 正規化パスインデックスの型とキャッシュ
        private sealed class NormalizedPathIndex
        {
            public readonly Dictionary<string, Transform> Map = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<string> Ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private static readonly Dictionary<int, NormalizedPathIndex> NormalizedPathIndexCache = new Dictionary<int, NormalizedPathIndex>();

        public static Transform ResolveConstraintNode(Transform outfitRoot, Transform armatureRoot, ConstraintData d, out Transform parent)
        {
            parent = null;
            Transform node = null;

            var parentPath = PathUtils.SplitPathSegments(d.parentName);

            if (outfitRoot != null && parentPath.Count > 0)
                parentPath = PathUtils.StripLeading(parentPath, outfitRoot.name);

            var parentNoRc = PathUtils.StripLeading(parentPath, "RotationConstraint");

            parent = PathUtils.FindBySegments(outfitRoot, parentPath)
                     ?? PathUtils.FindBySegments(outfitRoot, parentNoRc)
                     ?? PathUtils.EnsurePath(outfitRoot, parentPath);

            node = PathUtils.FindChildIgnoreCase(parent, d.emptyName) ?? CreateNode(parent, d.emptyName);
            return node;
        }

        private static Transform CreateNode(Transform parent, string name)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create Constraint Node");
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        public static Transform ResolveArmatureRoot(Transform outfitRoot, Transform userArmatureRoot, ConstraintData d)
        {
            if (userArmatureRoot != null) return userArmatureRoot;

            var recordedPath = PathUtils.SplitPathSegments(d.armatureRootPath);
            if (outfitRoot != null && recordedPath.Count > 0)
            {
                var found = PathUtils.FindBySegments(outfitRoot, recordedPath);
                if (found != null) return found;
            }

            if (outfitRoot != null && !string.IsNullOrEmpty(d.armatureRootName))
            {
                var byName = PathUtils.FindByName(outfitRoot, d.armatureRootName);
                if (byName != null) return byName;
            }

            return null;
        }

        public static Transform ResolveArmatureInClone(Transform originalRoot, Transform originalArmature, Transform cloneRoot)
        {
            if (originalRoot == null || originalArmature == null || cloneRoot == null) return null;
            if (!originalArmature.IsChildOf(originalRoot)) return null;
            if (originalArmature == originalRoot) return cloneRoot;

            var path = PathUtils.GetRelativePath(originalRoot, originalArmature, includeSelf: true, includeRoot: false);
            var segments = PathUtils.SplitPathSegments(path);
            if (segments.Count == 0) return cloneRoot;
            return PathUtils.FindBySegments(cloneRoot, segments);
        }

        public static Transform ResolveArmatureTransform(Transform armatureRoot, string pathFromArmature, string fallbackName)
        {
            Transform resolved = null;
            if (armatureRoot != null)
            {
                var segments = PathUtils.SplitPathSegments(pathFromArmature);
                if (segments.Count > 0)
                    resolved = PathUtils.FindBySegments(armatureRoot, segments);
                if (resolved == null && !string.IsNullOrEmpty(fallbackName))
                    resolved = PathUtils.FindByName(armatureRoot, fallbackName);
            }

            return resolved;
        }

        public static Transform ResolveTransformWithFallback(Transform outfitRoot, Transform armatureRoot, string pathFromArmature, string parentPath, string name, bool debugSkipHumanoid = false, bool debugSkipFullPath = false)
        {
            Transform t = null;

            if (!debugSkipHumanoid)
            {
                t = ResolveHumanoidTransform(armatureRoot, name);
                if (t != null) return t;
            }

            if (armatureRoot != null && !string.IsNullOrEmpty(name))
            {
                var byName = ResolveArmatureTransformByName(armatureRoot, name);
                if (byName != null) return byName;
            }

            if (!debugSkipFullPath)
            {
                t = ResolveArmatureTransform(armatureRoot, pathFromArmature, string.Empty);
                if (t != null) return t;

                t = ResolveArmatureTransformNormalized(armatureRoot, pathFromArmature);
                if (t != null) return t;

                t = ResolveArmatureTransformBySuffix(armatureRoot, pathFromArmature);
                if (t != null) return t;
            }

            return null;
        }

        public static Transform ResolveArmatureTransformBySuffix(Transform armatureRoot, string pathFromArmature)
        {
            if (armatureRoot == null || string.IsNullOrEmpty(pathFromArmature)) return null;
            var targetSegs = PathUtils.SplitPathSegments(pathFromArmature);
            if (targetSegs == null || targetSegs.Count == 0) return null;

            var normTarget = new List<string>(targetSegs.Count);
            foreach (var s in targetSegs) normTarget.Add(PathUtils.NormalizeName(s));

            foreach (var t in armatureRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                var rel = PathUtils.GetRelativePath(armatureRoot, t, includeSelf: true, includeRoot: false);
                if (string.IsNullOrEmpty(rel)) continue;
                var segs = PathUtils.SplitPathSegments(rel);
                if (segs.Count < normTarget.Count) continue;

                var match = true;
                for (int i = 0; i < normTarget.Count; i++)
                {
                    var a = PathUtils.NormalizeName(segs[segs.Count - normTarget.Count + i]);
                    var b = normTarget[i];
                    if (!string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
                    {
                        match = false;
                        break;
                    }
                }

                if (match) return t;
            }

            return null;
        }

        public static Transform ResolveArmatureTransformByName(Transform armatureRoot, string name)
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

        public static Transform ResolveArmatureTransformNormalized(Transform armatureRoot, string pathFromArmature)
        {
            if (armatureRoot == null || string.IsNullOrEmpty(pathFromArmature)) return null;
            var segments = PathUtils.SplitPathSegments(pathFromArmature);
            if (segments.Count == 0) return null;

            var key = NormalizePathKey(segments);
            if (string.IsNullOrEmpty(key)) return null;

            var index = GetOrBuildNormalizedPathIndex(armatureRoot);
            if (index == null) return null;
            if (index.Ambiguous.Contains(key)) return null;
            return index.Map.TryGetValue(key, out var t) ? t : null;
        }

        private static NormalizedPathIndex GetOrBuildNormalizedPathIndex(Transform armatureRoot)
        {
            if (armatureRoot == null) return null;
            var id = armatureRoot.GetInstanceID();
            if (NormalizedPathIndexCache.TryGetValue(id, out var cached) && cached != null) return cached;

            var index = new NormalizedPathIndex();
            foreach (var t in armatureRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                var rel = PathUtils.GetRelativePath(armatureRoot, t, includeSelf: true, includeRoot: false);
                if (string.IsNullOrEmpty(rel)) continue;
                var segs = PathUtils.SplitPathSegments(rel);
                if (segs.Count == 0) continue;
                var key = NormalizePathKey(segs);
                if (string.IsNullOrEmpty(key)) continue;

                if (index.Ambiguous.Contains(key)) continue;
                if (index.Map.TryGetValue(key, out var existing))
                {
                    if (existing != t)
                    {
                        index.Map.Remove(key);
                        index.Ambiguous.Add(key);
                    }
                    continue;
                }

                index.Map[key] = t;
            }

            NormalizedPathIndexCache[id] = index;
            return index;
        }

        private static string NormalizePathKey(IReadOnlyList<string> segments)
        {
            if (segments == null || segments.Count == 0) return string.Empty;
            var normalized = new List<string>(segments.Count);
            for (int i = 0; i < segments.Count; i++)
            {
                var seg = NormalizeSegmentKey(segments[i]);
                if (string.IsNullOrEmpty(seg)) return string.Empty;
                normalized.Add(seg);
            }
            return string.Join("/", normalized);
        }

        private static string NormalizeSegmentKey(string segment)
        {
            if (string.IsNullOrEmpty(segment)) return string.Empty;
            var normalized = BoneMaps.NormalizeKey(segment);
            if (BoneMaps.TryGetCanonical(normalized, out var canonical))
                return canonical;

            var byTokens = BoneMaps.TryNormalizeBoneKey(segment);
            if (!string.IsNullOrEmpty(byTokens))
                return BoneMaps.NormalizeKey(byTokens);

            return normalized;
        }

        public static Transform ResolveHumanoidTransform(Transform armatureRoot, string name)
        {
            if (armatureRoot == null || string.IsNullOrEmpty(name)) return null;

            var animator = armatureRoot.GetComponentInParent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman) return null;

            var canonical = BoneMaps.NormalizeKey(name);
            if (BoneMaps.TryGetCanonical(canonical, out var canonicalKey))
                canonical = canonicalKey;

            if (!BoneMaps.TryGetBone(canonical, out var bone)) return null;

            var t = animator.GetBoneTransform(bone);
            if (t == null) return null;

            if (!t.IsChildOf(armatureRoot)) return null;
            return t;
        }
    }
}
