/// －－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－ーーーーーーーーーーーー
/// <summary>
/// 概要 : Outfit（配置先）に対してインポート済みのConstraintデータから
///        Empty,VRCConstraintを再生成し、
///        Sources/Targetを別衣装のアーマチュア（armatureRoot）へ差し替える。
/// </summary>
/// <remarks>
/// 詳細 : ConstraintDataの一覧を受け取り、親パスをEnsurePathで作成、
///        各ノードにVRCRotationConstraintを追加または再利用して数値パラメータを復元する。
///        SourcesとTargetはoutfitのarmatureから再解決して上書きする。
/// 依存関係 : SDK = VRC.SDK3.Dynamics.Constraint.Components.VRCRotationConstraint型
/// 　　　　   独自 = ConstraintData構造。
/// 最終更新 : 2026-01-18
/// </remarks>
/// －－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－ーーーーーーーーーーーー

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;
//using VRC.SDK3.Dynamics.Constraint; ←これなに？


namespace ConstrainTA.Editor.Backend
{
    public static class ConstraintBuilder
    {
        /// 解決レベル
        /// armatureの一致度を測る指標
        /// ①Humanoidマッピング
        /// ②フルパスキーマッピング 一時廃止
        /// ③キーマッピング
        /// 2025/01/17 フルパスマッピングを一時廃止　∵humanoidエラー出てるとキーマッピングしか意味をなしていない...
        /// ->【課題】フルパス実装

        // 正規化辞書（外部モジュールへ委譲）
        // コピーやミューテーションを避けるため、BoneMaps の検索ヘルパーを直接利用します。

        //デバッグ用 Humanoidマッピングを無効化する。
        public static bool DebugSkipHumanoidLookup { get; set; }
        public static bool DebugSkipFullPathLookup { get; set; }

        //名前一致
        private sealed class NormalizedPathIndex
        {
            public readonly Dictionary<string, Transform> Map = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<string> Ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private static readonly Dictionary<int, NormalizedPathIndex> NormalizedPathIndexCache = new Dictionary<int, NormalizedPathIndex>();

        public static void Build(
            GameObject outfitRoot,
            Transform armatureRoot,
            List<ConstraintData> dataList)
        {
            Build(outfitRoot, armatureRoot, dataList, keepConstraintsDisabledAfterBuild: false);
        }

        public static void Build(
            GameObject outfitRoot,
            List<ConstraintData> dataList,
            bool keepConstraintsDisabledAfterBuild)
        {
            if (!TryDetectHumanoidArmatureRoot(outfitRoot, out var armatureRoot, out var reason))
            {
                Debug.LogError($"[ConstraintBuilder] Humanoid armature not found under '{(outfitRoot ? outfitRoot.name : "(null)")}'. {reason}");
                return;
            }

            Build(outfitRoot, armatureRoot, dataList, keepConstraintsDisabledAfterBuild);
        }

        public static void Build(
            GameObject outfitRoot,
            Transform armatureRoot,
            List<ConstraintData> dataList,
            bool keepConstraintsDisabledAfterBuild)
        {
            if (outfitRoot == null || armatureRoot == null || dataList == null) return;

            Debug.Log($"[ConstraintBuilder] Build start: outfit='{outfitRoot.name}', armature='{armatureRoot.name}', count={dataList.Count}");

            foreach (var d in dataList)
            {
                var resolvedArmature = ResolveArmatureRoot(outfitRoot.transform, armatureRoot, d);
                Transform parent;
                var node = ResolveConstraintNode(outfitRoot.transform, resolvedArmature, d, out parent);
                if (node == null)
                {
                    Debug.LogWarning($"[ConstraintBuilder] Failed to resolve node for '{d.emptyName}' type='{d.constraintType}' parent='{d.parentName}' armPath='{d.constraintPathFromArmature}'");
                    continue;
                }

                var constraint = ResolveOrAddConstraintComponent(node.gameObject, d.constraintType);
                if (constraint == null)
                {
                    Debug.LogWarning($"[ConstraintBuilder] Failed to add constraint component for '{d.emptyName}' type='{d.constraintType}'");
                    continue;
                }

                // JSON をまるごと適用（Sources/Target は後で上書き）
                Undo.RecordObject(constraint, "Apply Constraint Json");
                EditorJsonUtility.FromJsonOverwrite(d.constraintJson, constraint);

                // 手動ワークフローに合わせる処理:
                // - 作者はコンポーネントのチェックを外すのではなく「Is Active」だけをオフにする
                // - その後 Sources/Target を再マップする
                // - 最後に Activate を押す
                // なのでここでは Behaviour.enabled は触らず VRCConstraintBase.IsActive のみ切り替える。
                SdkUtils.SetConstraintIsActive(constraint, false);

                // 2025/01/02 ここしんどい, 01/03 API経由に変えた -> うまく行けてそう
                var constraintBase = constraint as VRCConstraintBase;
                Undo.RecordObject(constraint, "Update Constraint Sources/Target");

                // 重要:
                // Sources をクリアして再作成してはダメ。
                // VRCParentConstraint はソース要素内に各ソース固有のオフセット（ParentPositionOffset / ParentRotationOffset）を保持している。
                // クリアして再追加するとそれらが失われ、Enable 時にリグが破綻する可能性がある。
                if (constraintBase != null)
                {
                    RemapSourcesPreservingPerSourceData(constraintBase, outfitRoot.transform, resolvedArmature, d.parentName, d.sources);
                }

                var target = ResolveTransformWithFallback(outfitRoot.transform, resolvedArmature, d.targetPathFromArmature, d.parentName, d.targetName);
                SetTargetTransform(constraint, target);

                // 手動ワークフローに合わせる処理:
                // Sources/Target を再マップした後（IsActive がオフの間に）、インスペクタの「Activate」ボタンを押す。
                // ここではそれと同じ編集側の Activate 処理を CustomEditor 経由で呼び出すよう試みる。
                var activatedOk = SdkUtils.TryInvokeInspectorActivate(constraint);

                // Activate 後について:
                // - 一部のインスペクタ実装は Activate の一環で IsActive を切り替えることがある。
                // - Activate が実行されていないのに強制的に有効化するとリグが破綻する恐れがある。
                // そのため要求があれば強制で OFF にするが、それ以外は Activate が作った状態を尊重する。
                if (!activatedOk)
                {
                    // Activate が実行できなかった場合、有効化するとリグが破綻する可能性がある。
                    // そのため無効のままにして、ユーザーに診断または手動で Activate を実行してもらう。
                    SdkUtils.SetConstraintIsActive(constraint, false);
                    Debug.LogWarning($"[ConstraintBuilder] Activate failed for '{d.emptyName}' type='{constraint.GetType().Name}'. Keeping IsActive OFF.");
                }
                else if (keepConstraintsDisabledAfterBuild)
                {
                    // 必要に応じて、手動で Activate/検証してから有効化できるように制約を無効のまま保持します。
                    SdkUtils.SetConstraintIsActive(constraint, false);
                }

                EditorUtility.SetDirty(constraint);
                Debug.Log($"[ConstraintBuilder] Completed constraint '{d.emptyName}' type='{d.constraintType}' node='{GetPath(node)}' target='{(target ? target.name : "(null)")}'");
            }

            // 構造変更後、SDK の内部状態を一貫させるためにグループをリフレッシュします。
            SdkUtils.TrySdkRefreshGroups(outfitRoot.GetComponentsInChildren<VRCConstraintBase>(true));

            Debug.Log("[ConstraintBuilder] Build complete");
        }

        /// <summary>
        /// クローンしたルートでプレビューを作成します（非破壊）。
        /// クローンは `HideFlags.DontSave` としてマークされ、ディスクに保存されません。
        /// </summary>
        public static GameObject BuildPreview(GameObject outfitRoot, Transform armatureRoot, List<ConstraintData> dataList)
        {
            if (outfitRoot == null || dataList == null) return null;

            var previewRoot = UnityEngine.Object.Instantiate(outfitRoot);
            previewRoot.name = $"{outfitRoot.name}_ConstrainTA_Preview";
            previewRoot.hideFlags = HideFlags.DontSave;

            var previewArmature = ResolveArmatureInClone(outfitRoot.transform, armatureRoot, previewRoot.transform);
            if (previewArmature == null)
            {
                TryDetectHumanoidArmatureRoot(previewRoot, out previewArmature, out _);
            }

            if (previewArmature == null)
            {
                Debug.LogWarning($"[ConstraintBuilder] Preview: Humanoid armature not found under '{previewRoot.name}'.");
                return previewRoot;
            }

            Build(previewRoot, previewArmature, dataList, keepConstraintsDisabledAfterBuild: false);
            return previewRoot;
        }

        /// <summary>
        /// 既に存在するクローン上でプレビューを作成します（非破壊）。
        /// PreviewScene でクローンが既に作成され移動されている状況向けです。
        /// </summary>
        public static GameObject BuildPreviewOnClone(GameObject previewRoot, GameObject originalOutfitRoot, Transform originalArmatureRoot, List<ConstraintData> dataList)
        {
            if (previewRoot == null || originalOutfitRoot == null || dataList == null) return previewRoot;

            var previewArmature = ResolveArmatureInClone(originalOutfitRoot.transform, originalArmatureRoot, previewRoot.transform);
            if (previewArmature == null)
            {
                TryDetectHumanoidArmatureRoot(previewRoot, out previewArmature, out _);
            }

            if (previewArmature == null)
            {
                Debug.LogWarning($"[ConstraintBuilder] Preview: Humanoid armature not found under '{previewRoot.name}'.");
                return previewRoot;
            }

            Build(previewRoot, previewArmature, dataList, keepConstraintsDisabledAfterBuild: false);
            return previewRoot;
        }

        public static bool TryDetectHumanoidArmatureRoot(GameObject root, out Transform armatureRoot, out string reason)
        {
            armatureRoot = null;
            reason = string.Empty;
            if (root == null)
            {
                reason = "root is null";
                return false;
            }

            var animators = root.GetComponentsInChildren<Animator>(true);
            if (animators == null || animators.Length == 0)
            {
                reason = "Animator not found";
                return false;
            }

            Animator best = null;
            var bestScore = -1;
            foreach (var a in animators)
            {
                if (a == null) continue;
                if (a.avatar == null || !a.avatar.isHuman) continue;
                var score = CountHumanoidBones(a);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = a;
                }
            }

            if (best == null)
            {
                reason = "No humanoid Animator (avatar.isHuman) found";
                return false;
            }

            var bones = GetHumanoidBones(best);
            if (bones.Count == 0)
            {
                reason = $"Humanoid Animator '{best.name}' has no resolvable bone transforms";
                return false;
            }

            // 指定された root の下にある共通の祖先を優先する。
            var lca = FindLowestCommonAncestor(bones, root.transform);
            if (lca == null)
            {
                reason = $"Humanoid bones are not under '{root.name}'";
                return false;
            }

            armatureRoot = lca;
            return true;
        }

        private static int CountHumanoidBones(Animator a)
        {
            if (a == null || a.avatar == null || !a.avatar.isHuman) return 0;
            // 主要なボーンがどれだけ解決できるかでスコアリングする。
            var bones = new[]
            {
                HumanBodyBones.Hips,
                HumanBodyBones.Spine,
                HumanBodyBones.Chest,
                HumanBodyBones.Head,
                HumanBodyBones.LeftUpperArm,
                HumanBodyBones.RightUpperArm,
                HumanBodyBones.LeftUpperLeg,
                HumanBodyBones.RightUpperLeg,
            };

            var score = 0;
            foreach (var b in bones)
            {
                try
                {
                    if (a.GetBoneTransform(b) != null) score++;
                }
                catch { }
            }
            return score;
        }

        private static List<Transform> GetHumanoidBones(Animator a)
        {
            var result = new List<Transform>();
            if (a == null || a.avatar == null || !a.avatar.isHuman) return result;

            // 幅広いボーンセットを使うことで、最小共通祖先(LCA)が単なる四肢ルートではなく実際のスケルトンルートになる。
            var bones = new[]
            {
                HumanBodyBones.Hips,
                HumanBodyBones.Spine,
                HumanBodyBones.Chest,
                HumanBodyBones.UpperChest,
                HumanBodyBones.Neck,
                HumanBodyBones.Head,

                HumanBodyBones.LeftShoulder,
                HumanBodyBones.LeftUpperArm,
                HumanBodyBones.LeftLowerArm,
                HumanBodyBones.LeftHand,
                HumanBodyBones.RightShoulder,
                HumanBodyBones.RightUpperArm,
                HumanBodyBones.RightLowerArm,
                HumanBodyBones.RightHand,

                HumanBodyBones.LeftUpperLeg,
                HumanBodyBones.LeftLowerLeg,
                HumanBodyBones.LeftFoot,
                HumanBodyBones.RightUpperLeg,
                HumanBodyBones.RightLowerLeg,
                HumanBodyBones.RightFoot,
            };

            foreach (var b in bones)
            {
                try
                {
                    var t = a.GetBoneTransform(b);
                    if (t != null) result.Add(t);
                }
                catch { }
            }

            return result;
        }

        private static Transform FindLowestCommonAncestor(List<Transform> nodes, Transform limitRoot)
        {
            if (nodes == null || nodes.Count == 0) return null;

            // ルート->ノードの祖先リストを作成する。探索は limitRoot に制限される。
            var paths = new List<List<Transform>>(nodes.Count);
            foreach (var n in nodes)
            {
                if (n == null) continue;
                if (limitRoot != null && !n.IsChildOf(limitRoot)) return null;

                var path = new List<Transform>();
                var cur = n;
                while (cur != null)
                {
                    path.Add(cur);
                    if (cur == limitRoot) break;
                    cur = cur.parent;
                }

                path.Reverse();
                paths.Add(path);
            }

            if (paths.Count == 0) return null;

            // 全てのパスに対する最長共通プレフィックスを求める。
            var minLen = int.MaxValue;
            foreach (var p in paths) minLen = Math.Min(minLen, p.Count);

            Transform last = null;
            for (int i = 0; i < minLen; i++)
            {
                var candidate = paths[0][i];
                for (int j = 1; j < paths.Count; j++)
                {
                    if (paths[j][i] != candidate) return last;
                }
                last = candidate;
            }

            return last;
        }

        // SDK/リフレクションのヘルパーは実装を集約するため SdkUtils.cs に移動しました。

        public static bool NormalizeConstraint(Component constraint)
        {
            return ConstraintNormalizer.NormalizeConstraint(constraint);
        }

        private static Transform GetTargetTransform(Component constraint)
        {
            if (constraint == null) return null;

            var prop = constraint.GetType().GetProperty("TargetTransform", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (prop != null && prop.CanRead)
            {
                try { return prop.GetValue(constraint) as Transform; }
                catch { }
            }

            var field = constraint.GetType().GetField("TargetTransform", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field != null)
            {
                try { return field.GetValue(constraint) as Transform; }
                catch { }
            }

            return null;
        }

        private static bool NormalizePositionConstraint(Component constraint, VRCConstraintBase constraintBase, Transform target)
        {
            var parent = target.parent;

            var sumW = 0f;
            var sum = Vector3.zero;
            var sources = constraintBase.Sources;
            for (int i = 0; i < sources.Count; i++)
            {
                var s = sources[i];
                if (s.SourceTransform == null) continue;
                if (s.Weight <= 0f) continue;

                var p = parent ? parent.InverseTransformPoint(s.SourceTransform.position) : s.SourceTransform.position;
                sum += p * s.Weight;
                sumW += s.Weight;
            }

            if (sumW <= 0f) return false;

            var avg = sum / sumW;
            var targetPos = parent ? target.localPosition : target.position;
            var offset = targetPos - avg;

            SetVector3PropertyOrField(constraint, "PositionAtRest", targetPos);
            SetVector3PropertyOrField(constraint, "PositionOffset", offset);
            return true;
        }

        private static bool NormalizeRotationConstraint(Component constraint, VRCConstraintBase constraintBase, Transform target)
        {
            var parent = target.parent;

            // ソース回転を target.localRotation と同じ空間でブレンドする
            Quaternion blended = Quaternion.identity;
            var total = 0f;
            var sources = constraintBase.Sources;
            for (int i = 0; i < sources.Count; i++)
            {
                var s = sources[i];
                if (s.SourceTransform == null) continue;
                if (s.Weight <= 0f) continue;

                var r = parent
                    ? Quaternion.Inverse(parent.rotation) * s.SourceTransform.rotation
                    : s.SourceTransform.rotation;

                if (total <= 0f)
                {
                    blended = r;
                    total = s.Weight;
                }
                else
                {
                    var t = s.Weight / (total + s.Weight);
                    blended = Quaternion.Slerp(blended, r, t);
                    total += s.Weight;
                }
            }

            if (total <= 0f) return false;

            var targetRot = target.localRotation;
            var offsetQ = Quaternion.Inverse(blended) * targetRot;
            var offsetEuler = offsetQ.eulerAngles;

            SetVector3PropertyOrField(constraint, "RotationAtRest", targetRot.eulerAngles);
            SetVector3PropertyOrField(constraint, "RotationOffset", offsetEuler);
            return true;
        }

        private static bool NormalizeParentConstraint(VRCConstraintBase constraintBase, Transform target)
        {
            var sources = constraintBase.Sources;
            if (sources.Count == 0) return false;

            // Parent 制約はソース毎のオフセットを保持する仕様である。
            // 各ソースが単独で現在のターゲット姿勢を再現するようにオフセットを計算する。
            for (int i = 0; i < sources.Count; i++)
            {
                var s = sources[i];
                var st = s.SourceTransform;
                if (st == null) continue;

                var posOffset = st.InverseTransformPoint(target.position);
                var rotOffsetQ = Quaternion.Inverse(st.rotation) * target.rotation;
                var rotOffsetEuler = rotOffsetQ.eulerAngles;

                // VRCConstraintSource は struct のため、コピーを変更してから再代入する必要がある。
                SetVector3OnConstraintSource(ref s, "ParentPositionOffset", posOffset);
                SetVector3OnConstraintSource(ref s, "ParentRotationOffset", rotOffsetEuler);
                sources[i] = s;
            }

            try { constraintBase.Sources = sources; } catch { }
            return true;
        }

        private static void SetVector3PropertyOrField(Component obj, string memberName, Vector3 value)
        {
            if (obj == null || string.IsNullOrEmpty(memberName)) return;
            var type = obj.GetType();

            var prop = type.GetProperty(memberName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (prop != null && prop.CanWrite && prop.PropertyType == typeof(Vector3))
            {
                try { prop.SetValue(obj, value); } catch { }
                return;
            }

            var field = type.GetField(memberName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field != null && field.FieldType == typeof(Vector3))
            {
                try { field.SetValue(obj, value); } catch { }
            }
        }

        private static void SetVector3OnConstraintSource(ref VRCConstraintSource src, string memberName, Vector3 value)
        {
            if (string.IsNullOrEmpty(memberName)) return;
            var type = src.GetType();

            var prop = type.GetProperty(memberName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (prop != null && prop.CanWrite && prop.PropertyType == typeof(Vector3))
            {
                try
                {
                    object boxed = src;
                    prop.SetValue(boxed, value);
                    src = (VRCConstraintSource)boxed;
                }
                catch { }
                return;
            }

            var field = type.GetField(memberName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field != null && field.FieldType == typeof(Vector3))
            {
                try
                {
                    object boxed = src;
                    field.SetValue(boxed, value);
                    src = (VRCConstraintSource)boxed;
                }
                catch { }
            }
        }

        private static Transform ResolveConstraintNode(Transform outfitRoot, Transform armatureRoot, ConstraintData d, out Transform parent)
        {
            return ConstraintResolver.ResolveConstraintNode(outfitRoot, armatureRoot, d, out parent);
        }

        private static Transform CreateNode(Transform parent, string name)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create Constraint Node");
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static List<string> GetParentSegments(IReadOnlyList<string> segments)
        {
            return PathUtils.GetParentSegments(segments);
        }

        private static Transform ResolveArmatureTransform(Transform armatureRoot, string pathFromArmature, string fallbackName)
        {
            return ConstraintResolver.ResolveArmatureTransform(armatureRoot, pathFromArmature, fallbackName);
        }

        private static Transform ResolveTransformWithFallback(Transform outfitRoot, Transform armatureRoot, string pathFromArmature, string parentPath, string name)
        {
            return ConstraintResolver.ResolveTransformWithFallback(outfitRoot, armatureRoot, pathFromArmature, parentPath, name, DebugSkipHumanoidLookup, DebugSkipFullPathLookup);
        }

        // エディタプレビュー向けの公開ラッパー: ビルド時と同じ解決ロジックを公開する。
        // これによりプレビューでも同一の照合ルール（Humanoid, フルパス, 正規化パス）を使える。
        public static Transform ResolveTransformForPreview(Transform outfitRoot, Transform armatureRoot, string pathFromArmature, string parentPath, string name)
        {
            return ResolveTransformWithFallback(outfitRoot, armatureRoot, pathFromArmature, parentPath, name);
        }

        private static void RemapSourcesPreservingPerSourceData(
            VRCConstraintBase constraintBase,
            Transform outfitRoot,
            Transform armatureRoot,
            string parentPath,
            List<ConstraintData.SourceName> desiredSources)
        {
            ConstraintApplier.RemapSourcesPreservingPerSourceData(constraintBase, outfitRoot, armatureRoot, parentPath, desiredSources);
        }

        private static Transform ResolveHumanoidTransform(Transform armatureRoot, string name)
        {
            return ConstraintResolver.ResolveHumanoidTransform(armatureRoot, name);
        }

        private static Dictionary<string, HumanBodyBones> BuildCanonicalMap()
        {
            // キーは NormalizeKey で正規化されるため、エイリアス表とヒューマノイド検索で同一形式を共有できる。
            var map = new Dictionary<string, HumanBodyBones>(StringComparer.OrdinalIgnoreCase);

            void Add(string canonicalKey, HumanBodyBones bone)
            {
                map[BoneMaps.NormalizeKey(canonicalKey)] = bone;
            }

            Add("hips", HumanBodyBones.Hips);
            Add("spine", HumanBodyBones.Spine);
            Add("chest", HumanBodyBones.Chest);
            Add("upper_chest", HumanBodyBones.UpperChest);
            Add("neck", HumanBodyBones.Neck);
            Add("head", HumanBodyBones.Head);
            Add("left_eye", HumanBodyBones.LeftEye);
            Add("right_eye", HumanBodyBones.RightEye);
            Add("left_shoulder", HumanBodyBones.LeftShoulder);
            Add("left_arm", HumanBodyBones.LeftUpperArm);
            Add("left_forearm", HumanBodyBones.LeftLowerArm);
            Add("left_hand", HumanBodyBones.LeftHand);
            Add("right_shoulder", HumanBodyBones.RightShoulder);
            Add("right_arm", HumanBodyBones.RightUpperArm);
            Add("right_forearm", HumanBodyBones.RightLowerArm);
            Add("right_hand", HumanBodyBones.RightHand);
            Add("left_thigh", HumanBodyBones.LeftUpperLeg);
            Add("left_calf", HumanBodyBones.LeftLowerLeg);
            Add("left_foot", HumanBodyBones.LeftFoot);
            Add("left_toe", HumanBodyBones.LeftToes);
            Add("right_thigh", HumanBodyBones.RightUpperLeg);
            Add("right_calf", HumanBodyBones.RightLowerLeg);
            Add("right_foot", HumanBodyBones.RightFoot);
            Add("right_toe", HumanBodyBones.RightToes);
            Add("left_thumb_proximal", HumanBodyBones.LeftThumbProximal);
            Add("left_thumb_intermediate", HumanBodyBones.LeftThumbIntermediate);
            Add("left_thumb_distal", HumanBodyBones.LeftThumbDistal);
            Add("left_index_proximal", HumanBodyBones.LeftIndexProximal);
            Add("left_index_intermediate", HumanBodyBones.LeftIndexIntermediate);
            Add("left_index_distal", HumanBodyBones.LeftIndexDistal);
            Add("left_middle_proximal", HumanBodyBones.LeftMiddleProximal);
            Add("left_middle_intermediate", HumanBodyBones.LeftMiddleIntermediate);
            Add("left_middle_distal", HumanBodyBones.LeftMiddleDistal);
            Add("left_ring_proximal", HumanBodyBones.LeftRingProximal);
            Add("left_ring_intermediate", HumanBodyBones.LeftRingIntermediate);
            Add("left_ring_distal", HumanBodyBones.LeftRingDistal);
            Add("left_pinky_proximal", HumanBodyBones.LeftLittleProximal);
            Add("left_pinky_intermediate", HumanBodyBones.LeftLittleIntermediate);
            Add("left_pinky_distal", HumanBodyBones.LeftLittleDistal);
            Add("right_thumb_proximal", HumanBodyBones.RightThumbProximal);
            Add("right_thumb_intermediate", HumanBodyBones.RightThumbIntermediate);
            Add("right_thumb_distal", HumanBodyBones.RightThumbDistal);
            Add("right_index_proximal", HumanBodyBones.RightIndexProximal);
            Add("right_index_intermediate", HumanBodyBones.RightIndexIntermediate);
            Add("right_index_distal", HumanBodyBones.RightIndexDistal);
            Add("right_middle_proximal", HumanBodyBones.RightMiddleProximal);
            Add("right_middle_intermediate", HumanBodyBones.RightMiddleIntermediate);
            Add("right_middle_distal", HumanBodyBones.RightMiddleDistal);
            Add("right_ring_proximal", HumanBodyBones.RightRingProximal);
            Add("right_ring_intermediate", HumanBodyBones.RightRingIntermediate);
            Add("right_ring_distal", HumanBodyBones.RightRingDistal);
            Add("right_pinky_proximal", HumanBodyBones.RightLittleProximal);
            Add("right_pinky_intermediate", HumanBodyBones.RightLittleIntermediate);
            Add("right_pinky_distal", HumanBodyBones.RightLittleDistal);

            return map;
        }

        private static Dictionary<string, string> BuildAliasMap()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            void Add(string canonical, params string[] aliases)
            {
                var key = BoneMaps.NormalizeKey(canonical);
                foreach (var a in aliases)
                {
                    var k = BoneMaps.NormalizeKey(a);
                    if (!map.ContainsKey(k)) map[k] = key;
                }
                // 自分自身も登録
                if (!map.ContainsKey(key)) map[key] = key;
            }

            Add("hips", "Hips", "hips");
            Add("spine", "Spine", "spine");
            Add("chest", "Chest", "chest");
            Add("upper_chest", "UpperChest", "Upper Chest", "upperchest", "UpperChest");
            Add("neck", "Neck", "neck");
            Add("head", "Head", "head");

            Add("left_eye", "LeftEye", "Eye_L", "Eye.L", "eye.L", "eye_L");
            Add("right_eye", "RightEye", "Eye_R", "Eye.R", "eye.R", "eye_R");

            Add("left_shoulder", "Shoulder_L", "Shoulder.L", "Shoulder_L", "shoulder.L", "sholder_L", "Shoulder.l");
            Add("left_arm", "UpperArm_L", "Upper_arm.L", "Upperarm_L", "upper_arm.L", "UpperArm.l", "Upper_Arm_L");
            Add("left_forearm", "LowerArm_L", "Lower_arm.L", "Lowerarm_L", "lower_arm.L", "LowerArm.l", "Lower_Arm_L");
            Add("left_hand", "Hand_L", "Hand.L", "Left Hand", "hand.L", "Hand.l", "Hand_L");

            Add("right_shoulder", "Shoulder_R", "Shoulder.R", "Shoulder_R", "shoulder.R", "sholder_R", "Shoulder.r");
            Add("right_arm", "UpperArm_R", "Upper_arm.R", "Upperarm_R", "upper_arm.R", "UpperArm.r", "Upper_Arm_R");
            Add("right_forearm", "LowerArm_R", "Lower_arm.R", "Lowerarm_R", "lower_arm.R", "LowerArm.r", "Lower_Arm_R");
            Add("right_hand", "Hand_R", "Hand.R", "Right Hand", "hand.R", "Hand.r", "Hand_R");

            Add("left_thigh", "UpperLeg_L", "Upper_leg.L", "Upperleg_L", "upper_leg.L", "UpperLeg.l", "Upper_Leg_L", "UpperLeg.L");
            Add("left_calf", "LowerLeg_L", "Lower_leg.L", "Lowerleg_L", "lower_leg.L", "LowerLeg.l", "Lower_Leg_L", "LowerLeg.L");
            Add("left_foot", "Foot_L", "Foot.L", "foot.L", "Foot.l", "Foot_L", "Foot.L");
            Add("left_toe", "Toe_L", "Toe.L", "Toes.L", "toe.L", "Toes_L", "Toes.L");

            Add("right_thigh", "UpperLeg_R", "Upper_leg.R", "Upperleg_R", "upper_leg.R", "UpperLeg.r", "Upper_Leg_R", "UpperLeg.R");
            Add("right_calf", "LowerLeg_R", "Lower_leg.R", "Lowerleg_R", "lower_leg.R", "LowerLeg.r", "Lower_Leg_R", "LowerLeg.R");
            Add("right_foot", "Foot_R", "Foot.R", "foot.R", "Foot.r", "Foot_R", "Foot.R");
            Add("right_toe", "Toe_R", "Toe.R", "Toes.R", "toe.R", "Toes_R", "Toes.R");

            // 指（左）
            Add("left_thumb_proximal", "Thumb1_L", "Thumb Proximal.L", "Thumb.proximal.L", "Thumb Proximal_L", "thumb.proximal.L", "ThumbProximal_L", "Thumb1.l");
            Add("left_thumb_intermediate", "Thumb2_L", "Thumb Intermediate.L", "Thumb.intermediate.L", "Thumb Intermediate_L", "thumb.intermediate.L", "ThumbIntermediate_L", "Thumb2.l");
            Add("left_thumb_distal", "Thumb3_L", "Thumb Distal.L", "Thumb.distal.L", "Thumb Distal_L", "thumb.distal.L", "ThumbDistal_L", "Thumb3.l");
            Add("left_index_proximal", "Index1_L", "Index Proximal.L", "Index.proximal.L", "Index Proximal_L", "index.proximal.L", "IndexProximal_L", "Index1.l");
            Add("left_index_intermediate", "Index2_L", "Index Intermediate.L", "Index.intermediate.L", "Index Intermediate_L", "index.intermediate.L", "IndexIntermediate_L", "Index2.l");
            Add("left_index_distal", "Index3_L", "Index Distal.L", "Index.distal.L", "Index Distal_L", "index.distal.L", "IndexDistal_L", "Index3.l");
            Add("left_middle_proximal", "Middle1_L", "Middle Proximal.L", "Middle.proximal.L", "Middle Proximal_L", "middle.proximal.L", "MiddleProximal_L", "Middle1.l");
            Add("left_middle_intermediate", "Middle2_L", "Middle Intermediate.L", "Middle.intermediate.L", "Middle Intermediate_L", "middle.intermediate.L", "MiddleIntermediate_L", "Middle2.l");
            Add("left_middle_distal", "Middle3_L", "Middle Distal.L", "Middle.distal.L", "Middle Distal_L", "middle.distal.L", "MiddleDistal_L", "Middle3.l");
            Add("left_ring_proximal", "Ring1_L", "Ring Proximal.L", "Ring.proximal.L", "Ring Proximal_L", "ring.proximal.L", "RingProximal_L", "Ring1.l");
            Add("left_ring_intermediate", "Ring2_L", "Ring Intermediate.L", "Ring.intermediate.L", "Ring Intermediate_L", "ring.intermediate.L", "RingIntermediate_L", "Ring2.l");
            Add("left_ring_distal", "Ring3_L", "Ring Distal.L", "Ring.distal.L", "Ring Distal_L", "ring.distal.L", "RingDistal_L", "Ring3.l");
            Add("left_pinky_proximal", "Pinky1_L", "Little Proximal.L", "Little.proximal.L", "Little Proximal_L", "little.proximal.L", "LittleProximal_L", "Little1.l");
            Add("left_pinky_intermediate", "Pinky2_L", "Little Intermediate.L", "Little.intermediate.L", "Little Intermediate_L", "little.intermediate.L", "LittleIntermediate_L", "Little2.l");
            Add("left_pinky_distal", "Pinky3_L", "Little Distal.L", "Little.distal.L", "Little Distal_L", "little.distal.L", "LittleDistal_L", "Little3.l");

            // 指（右）
            Add("right_thumb_proximal", "Thumb1_R", "Thumb Proximal.R", "Thumb.proximal.R", "Thumb Proximal_R", "thumb.proximal.R", "ThumbProximal_R", "Thumb1.r");
            Add("right_thumb_intermediate", "Thumb2_R", "Thumb Intermediate.R", "Thumb.intermediate.R", "Thumb Intermediate_R", "thumb.intermediate.R", "ThumbIntermediate_R", "Thumb2.r");
            Add("right_thumb_distal", "Thumb3_R", "Thumb Distal.R", "Thumb.distal.R", "Thumb Distal_R", "thumb.distal.R", "ThumbDistal_R", "Thumb3.r");
            Add("right_index_proximal", "Index1_R", "Index Proximal.R", "Index.proximal.R", "Index Proximal_R", "index.proximal.R", "IndexProximal_R", "Index1.r");
            Add("right_index_intermediate", "Index2_R", "Index Intermediate.R", "Index.intermediate.R", "Index Intermediate_R", "index.intermediate.R", "IndexIntermediate_R", "Index2.r");
            Add("right_index_distal", "Index3_R", "Index Distal.R", "Index.distal.R", "Index Distal_R", "index.distal.R", "IndexDistal_R", "Index3.r");
            Add("right_middle_proximal", "Middle1_R", "Middle Proximal.R", "Middle.proximal.R", "Middle Proximal_R", "middle.proximal.R", "MiddleProximal_R", "Middle1.r");
            Add("right_middle_intermediate", "Middle2_R", "Middle Intermediate.R", "Middle.intermediate.R", "Middle Intermediate_R", "middle.intermediate.R", "MiddleIntermediate_R", "Middle2.r");
            Add("right_middle_distal", "Middle3_R", "Middle Distal.R", "Middle.distal.R", "Middle Distal_R", "middle.distal.R", "MiddleDistal_R", "Middle3.r");
            Add("right_ring_proximal", "Ring1_R", "Ring Proximal.R", "Ring.proximal.R", "Ring Proximal_R", "ring.proximal.R", "RingProximal_R", "Ring1.r");
            Add("right_ring_intermediate", "Ring2_R", "Ring Intermediate.R", "Ring.intermediate.R", "Ring Intermediate_R", "ring.intermediate.R", "RingIntermediate_R", "Ring2.r");
            Add("right_ring_distal", "Ring3_R", "Ring Distal.R", "Ring.distal.R", "Ring Distal_R", "ring.distal.R", "RingDistal_R", "Ring3.r");
            Add("right_pinky_proximal", "Pinky1_R", "Little Proximal.R", "Little.proximal.R", "Little Proximal_R", "little.proximal.R", "LittleProximal_R", "Little1.r");
            Add("right_pinky_intermediate", "Pinky2_R", "Little Intermediate.R", "Little.intermediate.R", "Little Intermediate_R", "little.intermediate.R", "LittleIntermediate_R", "Little2.r");
            Add("right_pinky_distal", "Pinky3_R", "Little Distal.R", "Little.distal.R", "Little Distal_R", "little.distal.R", "LittleDistal_R", "Little3.r");

            // 目の末端 / 付加要素（ヒューマノイドマッピングはないが、名前検索のためのエイリアスを登録）
            Add("left_eye_end", "Eye.L_end", "eye.L_end", "LeftEye_end", "LeftEye_end");
            Add("right_eye_end", "Eye.R_end", "eye.R_end", "RightEye_end", "RightEye_end");
            Add("left_toe_end", "Toes.L_end", "Toe.L_end", "Toes_END", "Toes.L_end");
            Add("right_toe_end", "Toes.R_end", "Toe.R_end", "Toes_END.001", "Toes.R_end");

            return map;
        }

        // ローカルの NormalizeKey 実装の代わりに `BoneMaps.NormalizeKey` を使用する。

        private static Transform ResolveArmatureTransformNormalized(Transform armatureRoot, string pathFromArmature)
        {
            return ConstraintResolver.ResolveArmatureTransformNormalized(armatureRoot, pathFromArmature);
        }

        // 正規化パスインデックスの構築は `ConstraintResolver` 内部で実装されています。

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

        // Path/階層ヘルパー群は `PathUtils` に分離しました。
        // 正規化・トークン化のヘルパーは `BoneMaps` に移動しました。

        private static Transform ResolveArmatureRoot(Transform outfitRoot, Transform userArmatureRoot, ConstraintData d)
        {
            return ConstraintResolver.ResolveArmatureRoot(outfitRoot, userArmatureRoot, d);
        }

        /// －－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－ー
        /// <summary>
        /// 関数名 : EnsurePath
        /// 概要 : スラッシュ区切りのパスを root 配下に再現し、最後の Transform を返す。
        /// </summary>
        /// <remarks>
        /// 依存関係 : なし
        /// 備考 : 既存の Transform が存在すれば再利用する。
        /// </remarks>
        /// －－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－ー
        private static Transform EnsurePath(Transform root, IReadOnlyList<string> segments)
        {
            return PathUtils.EnsurePath(root, segments);
        }

        /// －－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－ー
        /// <summary>
        /// 関数名 : FindByName
        /// 概要 : root 以下の全ての Transform を走査して name と一致する最初の Transform を返す。
        /// </summary>
        /// <remarks>
        /// 依存関係 : なし
        /// 備考 : 同名の複数ノードがある場合は最初に見つかったものを返す点に注意。
        /// </remarks>
        /// －－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－ー
        private static Transform FindByName(Transform root, string name)
        {
            return PathUtils.FindByName(root, name);
        }

        private static Transform ResolveArmatureInClone(Transform originalRoot, Transform originalArmature, Transform cloneRoot)
        {
            return ConstraintResolver.ResolveArmatureInClone(originalRoot, originalArmature, cloneRoot);
        }

        private static string GetRelativePath(Transform root, Transform target, bool includeSelf, bool includeRoot)
        {
            return PathUtils.GetRelativePath(root, target, includeSelf, includeRoot);
        }

        // 名前を無視して大文字小文字を区別せずに直下の子を検索する
        private static Transform FindChildIgnoreCase(Transform parent, string name)
        {
            return PathUtils.FindChildIgnoreCase(parent, name);
        }

        // ルート配下で "a/b/c" のようなパスを、大文字小文字を無視してセグメント単位で検索する
        private static Transform FindBySegments(Transform root, IReadOnlyList<string> segments)
        {
            return PathUtils.FindBySegments(root, segments);
        }

        private static List<string> SplitPathSegments(string path)
        {
            return PathUtils.SplitPathSegments(path);
        }

        private static List<string> StripLeading(IReadOnlyList<string> segments, string leading)
        {
            return PathUtils.StripLeading(segments, leading);
        }

        private static string GetPath(Transform t)
        {
            return PathUtils.GetPath(t);
        }

        private static bool NameEquals(string a, string b)
        {
            return PathUtils.NameEquals(a, b);
        }

        private static string NormalizeName(string name)
        {
            return PathUtils.NormalizeName(name);
        }

        private static Transform ResolveArmatureTransformBySuffix(Transform armatureRoot, string pathFromArmature)
        {
            return ConstraintResolver.ResolveArmatureTransformBySuffix(armatureRoot, pathFromArmature);
        }

        private static Transform ResolveArmatureTransformByName(Transform armatureRoot, string name)
        {
            return ConstraintResolver.ResolveArmatureTransformByName(armatureRoot, name);
        }


        private static Component ResolveOrAddConstraintComponent(GameObject go, string constraintType)
        {
            return ConstraintApplier.ResolveOrAddConstraintComponent(go, constraintType);
        }

        private static void SetTargetTransform(Component constraint, Transform target)
        {
            ConstraintApplier.SetTargetTransform(constraint, target);
        }

        private static bool GetConstraintIsActive(Component constraint)
        {
            return SdkUtils.GetConstraintIsActive(constraint);
        }

        private static void SetConstraintIsActive(Component constraint, bool active)
        {
            SdkUtils.SetConstraintIsActive(constraint, active);
        }

        /* 未使用: SimplifyTypeName
         * アセンブリ修飾名から短い型名を取り出すヘルパー。
         * UI 側で使われている同名のコピーが存在するため、ここは未参照である。
         * 実装を残したまま重複を避けるためコメントアウトしている。
         */
        /*
        private static string SimplifyTypeName(string assemblyQualifiedName)
        {
            if (string.IsNullOrEmpty(assemblyQualifiedName)) return "(unknown)";
            var comma = assemblyQualifiedName.IndexOf(',');
            var typeName = comma >= 0 ? assemblyQualifiedName.Substring(0, comma) : assemblyQualifiedName;
            var lastDot = typeName.LastIndexOf('.');
            return lastDot >= 0 ? typeName.Substring(lastDot + 1) : typeName;
        }
        */
    }
}
