/// －－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－ーーーーーーーーーーーー
/// <summary>
/// 概要 : 指定した Import Root 配下に存在する VRCRotationConstraint を走査し、
///        軽量な ConstraintData リストへ変換して返す（名前と weight を保存）。
/// </summary>
/// <remarks>
/// 詳細 : 各 VRCRotationConstraint の配置階層（親パス）と空のノード名、Target の名前、
///        数値パラメータを JSON として保存する。Sources は Transform 名と weight のみ保存。
/// 依存関係 : VRC.SDK3.Dynamics.Constraint.Components.VRCRotationConstraint 型を参照する。
/// 最終更新 : 2026-01-03
/// </remarks>
/// －－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－ーーーーーーーーーーーー
/// 
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;
using ConstraintData = ConstrainTA.Editor.Backend.ConstraintData;

namespace ConstrainTA.Editor.Backend
{
    public static class ConstraintImporter
    {
        /// －－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－ーーーーーーーーーーー
        /// <summary>
        /// 関数名 : Import
        /// 概要 : importRoot 以下の VRCRotationConstraint を検索し、ConstraintData のリストを作成して返す。
        /// </summary>
        /// <remarks>
        /// 依存関係 : VRC SDK の VRCRotationConstraint
        /// 備考 : Sources は Transform 参照ではなく名前と weight のみを保存する（後で別のアーマチュアへ再解決するため）。
        /// </remarks>
        /// －－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－ーーーーーーーーーーー
        public static List<ConstraintData> Import(GameObject importRoot)
        {
            return Import(importRoot, null);
        }

        public static List<ConstraintData> Import(GameObject importRoot, Transform armatureRootOverride)
        {
            var list = new List<ConstraintData>();
            if (importRoot == null) return list;

            var constraints = importRoot.GetComponentsInChildren<VRCConstraintBase>(true);

            foreach (var c in constraints)
            {
                var armatureRoot = ResolveArmatureRootOverride(importRoot.transform, c, armatureRootOverride);
                if (armatureRoot == null)
                    armatureRoot = DetectArmatureRoot(importRoot.transform, c);
                var armatureRootPath = PathUtils.GetRelativePath(importRoot.transform, armatureRoot, includeSelf: true, includeRoot: false);
                var constraintPathFromArmature = PathUtils.GetRelativePath(armatureRoot, c.transform, includeSelf: true, includeRoot: false);

                var d = new ConstraintData
                {
                    emptyName = c.gameObject.name,
                    parentName = c.transform.parent != null ? PathUtils.GetRelativePath(importRoot.transform, c.transform.parent, includeSelf: true, includeRoot: false) : string.Empty,
                    targetName = c.TargetTransform ? c.TargetTransform.name : string.Empty,
                    constraintJson = EditorJsonUtility.ToJson(c),
                    armatureRootName = armatureRoot ? armatureRoot.name : string.Empty,
                    armatureRootPath = armatureRootPath,
                    constraintPathFromArmature = constraintPathFromArmature,
                    targetPathFromArmature = PathUtils.GetRelativePath(armatureRoot, c.TargetTransform, includeSelf: true, includeRoot: false),
                    constraintType = c.GetType().AssemblyQualifiedName
                };

                d.sources.Clear();
                var sources = c.Sources;
                for (int i = 0; i < sources.Count; i++)
                {
                    var s = sources[i];
                    if (s.SourceTransform == null) continue;

                    d.sources.Add(new ConstraintData.SourceName
                    {
                        name = s.SourceTransform.name,
                        weight = s.Weight,
                        pathFromArmature = PathUtils.GetRelativePath(armatureRoot, s.SourceTransform, includeSelf: true, includeRoot: false)
                    });
                }

                list.Add(d);
            }

            return list;
        }

        private static Transform ResolveArmatureRootOverride(Transform importRoot, VRCConstraintBase constraint, Transform overrideRoot)
        {
            if (importRoot == null || constraint == null || overrideRoot == null) return null;
            if (!overrideRoot.IsChildOf(importRoot)) return null;

            if (constraint.TargetTransform != null && constraint.TargetTransform.IsChildOf(overrideRoot)) return overrideRoot;
            foreach (var s in constraint.Sources)
                if (s.SourceTransform != null && s.SourceTransform.IsChildOf(overrideRoot)) return overrideRoot;

            // 制約自身の Transform が override の下にある場合は最後の手段としてそれを許容する
            if (constraint.transform != null && constraint.transform.IsChildOf(overrideRoot)) return overrideRoot;

            return null;
        }

        /// －－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－ーーーーーーーーーーー
        /// <summary>
        /// 関数名 : GetRelativeParent
        /// 概要 : 指定した Transform の親階層を root からの相対パスとして返す。
        /// </summary>
        /// <remarks>
        /// 依存関係 : なし
        /// 備考 : root 自身の名前は含めない（root を「配置先で指定するコンテナ」として扱い、コピー元と同じ相対階層を再現するため）。
        /// </remarks>
        /// －－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－ーーーーーーーーーーー
        // Path related helpers delegated to PathUtils module.

        private static Transform DetectArmatureRoot(Transform importRoot, VRCConstraintBase constraint)
        {
            if (importRoot == null || constraint == null) return null;

            var candidates = new List<Transform>();
            if (constraint.TargetTransform != null) candidates.Add(constraint.TargetTransform);
            foreach (var s in constraint.Sources)
                if (s.SourceTransform != null) candidates.Add(s.SourceTransform);

            // 最後の手段として制約自身の Transform も候補に含める
            candidates.Add(constraint.transform);

            foreach (var t in candidates)
            {
                var top = FindTopUnderRoot(importRoot, t);
                if (top != null) return top;
            }

            return null;
        }

        private static Transform FindTopUnderRoot(Transform root, Transform t)
        {
            if (root == null || t == null) return null;
            if (!t.IsChildOf(root)) return null;

            var cur = t;
            Transform candidate = null;
            while (cur != null && cur != root)
            {
                candidate = cur;
                if (cur.parent == root) break;
                cur = cur.parent;
            }

            return candidate;
        }

        // 数値バックアップは EditorJsonUtility に集約したため廃止
    }
}
