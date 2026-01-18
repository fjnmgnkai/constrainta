// 診断ユーティリティ（`CONSTRAINTA_DIAGNOSTICS` が定義されている場合のみ有効。通常の配布ビルドには含めない）。
#if CONSTRAINTA_DIAGNOSTICS

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;

namespace ConstrainTA.Editor.Backend
{
    public static class ConstraintDiagnostics
    {
        public readonly struct ActivateReport
        {
            public readonly int Total;
            public readonly int Succeeded;
            public readonly int Failed;
            public readonly string FailedTypesSummary;

            public ActivateReport(int total, int succeeded, int failed, string failedTypesSummary)
            {
                Total = total;
                Succeeded = succeeded;
                Failed = failed;
                FailedTypesSummary = failedTypesSummary;
            }
        }

        public static ActivateReport LastActivateReport { get; private set; }

        public static int SetAllConstraintsActive(GameObject root, bool active)
        {
            if (root == null) return 0;

            var constraints = root.GetComponentsInChildren<VRCConstraintBase>(true);
            if (constraints == null || constraints.Length == 0) return 0;

            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(active ? "Enable Constraints" : "Disable Constraints");

            var changed = 0;
            foreach (var c in constraints)
            {
                if (c == null) continue;
                Undo.RecordObject(c, active ? "Enable Constraint" : "Disable Constraint");
                SdkUtils.SetConstraintIsActive(c, active);
                EditorUtility.SetDirty(c);
                changed++;
            }

            Undo.CollapseUndoOperations(group);
            Debug.Log($"[ConstrainTA] Set constraints active={active}: {changed} components under '{root.name}'");
            return changed;
        }

        public static void LogConstraintBindings(GameObject root)
        {
            if (root == null) return;
            var constraints = root.GetComponentsInChildren<VRCConstraintBase>(true);
            Debug.Log($"[ConstrainTA] Constraint bindings under '{root.name}': {constraints.Length}");

            foreach (var c in constraints)
            {
                if (c == null) continue;

                var typeName = c.GetType().Name;
                var active = SdkUtils.GetConstraintIsActive(c);
                var target = c.TargetTransform;

                var sources = c.Sources;
                var srcParts = new List<string>();
                for (int i = 0; i < sources.Count; i++)
                {
                    var s = sources[i];
                    var st = s.SourceTransform;
                    srcParts.Add(st != null
                        ? $"{i}:{PathUtils.GetPath(st)} (w={s.Weight})"
                        : $"{i}:(null) (w={s.Weight})");
                }

                Debug.Log($"[ConstrainTA] {typeName} active={active} self={PathUtils.GetPath(c.transform)} target={(target ? PathUtils.GetPath(target) : "(null)")} sources=[{string.Join(" | ", srcParts)}]");
            }
        }

        public static int ActivateAllConstraints(GameObject root)
        {
            if (root == null) return 0;

            var constraints = root.GetComponentsInChildren<VRCConstraintBase>(true);
            if (constraints == null || constraints.Length == 0) return 0;

            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Activate Constraints");

            var activated = 0;
            var failed = 0;
            var failedTypes = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var c in constraints)
            {
                if (c == null) continue;
                // ここで同じ計算を再実装するのではなく、インスペクタ側の Activate を呼び出すようにします。
                Undo.RecordObject(c, "Activate Constraint");
                var ok = false;
                try
                {
                    ok = SdkUtils.TryInvokeInspectorActivate(c);
                }
                catch { }

                if (!ok)
                {
                    failed++;
                    var typeName = c.GetType().Name;
                    failedTypes.TryGetValue(typeName, out var n);
                    failedTypes[typeName] = n + 1;
                    Debug.LogWarning($"[ConstrainTA] Activate failed: type={typeName} self={PathUtils.GetPath(c.transform)}");
                    continue;
                }
                EditorUtility.SetDirty(c);
                activated++;
            }

            Undo.CollapseUndoOperations(group);
            var failedSummary = failedTypes.Count == 0
                ? string.Empty
                : string.Join(", ", failedTypes.Select(kv => $"{kv.Key}x{kv.Value}"));
            LastActivateReport = new ActivateReport(constraints.Length, activated, failed, failedSummary);
            Debug.Log($"[ConstrainTA] Activated constraints: {activated}/{constraints.Length} under '{root.name}' (failed={failed}{(string.IsNullOrEmpty(failedSummary) ? "" : $" types=[{failedSummary}]")})");
            return activated;
        }

        private static bool InvokeInspectorActivate(Component constraint)
        {
            if (constraint == null) return false;

            try
            {
                if (constraint is VRCConstraintBase cb)
                    SdkUtils.TrySdkRefreshGroups(new[] { cb });
            }
            catch { }

            var editor = UnityEditor.Editor.CreateEditor(constraint);
            if (editor == null) return false;

            try
            {
                // インスペクタ経由の有効化処理は SdkUtils に委譲します。
                return SdkUtils.TryInvokeInspectorActivate(constraint);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(editor);
            }
        }

        private static bool TryInvokeComponentActivate(Component constraint)
        {
            return SdkUtils.TryInvokeComponentActivate(constraint);
        }

        private static void TrySdkRefreshGroups(VRCConstraintBase[] constraints)
        {
            SdkUtils.TrySdkRefreshGroups(constraints);
        }

        private static Type FindType(string fullName)
        {
            // 内部では SdkUtils.FindType に委譲しているため、互換性のためにスタブを残します。
            return null;
        }

        private static bool GetConstraintIsActive(Component constraint)
        {
            return SdkUtils.GetConstraintIsActive(constraint);
        }

        private static void SetConstraintIsActive(Component constraint, bool active)
        {
            SdkUtils.SetConstraintIsActive(constraint, active);
        }

        // パス整形は PathUtils.GetPath に委譲しています。
    }
}

#endif
