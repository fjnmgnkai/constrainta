/// -----------------------------------------------------------------------------
/// <summary>
/// 概要 : パス/階層処理に関する共通ユーティリティを提供するモジュール。
///        このファイルはモジュール化のために `ConstraintBuilder` から分離された。
///        - パス分割/結合/正規化（ヒエラルキー名に関する最低限の調整）
///        - Transform の探索/EnsurePath 等の共通処理
///        目的 : 他のバックエンドコードから再利用可能にして責務を分離する。
/// </summary>
/// <remarks>
/// - 実装は軽量に保ち、依存先は最小限（UnityEngine / UnityEditor / System）に留める。
/// - ボーン名のトークン化や正規化（語彙レベル）は `BoneMaps` に委譲する設計にする。
/// 最終更新 : 2026-01-18
/// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace ConstrainTA.Editor.Backend
{
    public static class PathUtils
    {
        // NormalizeName は階層名のトリミング等の最低限の正規化を行う。
        public static string NormalizeName(string name)
        {
            return string.IsNullOrEmpty(name) ? string.Empty : name.TrimEnd('.');
        }

        public static bool NameEquals(string a, string b)
        {
            if (a == null || b == null) return false;
            return string.Equals(NormalizeName(a), NormalizeName(b), StringComparison.OrdinalIgnoreCase);
        }

        public static string GetPath(Transform t)
        {
            if (t == null) return "(null)";
            var stack = new Stack<string>();
            PushAncestors(t, stack, null);
            return string.Join("/", stack.ToArray());
        }

        public static List<string> SplitPathSegments(string path)
        {
            if (string.IsNullOrEmpty(path)) return new List<string>();
            return new List<string>(path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries));
        }

        public static List<string> StripLeading(IReadOnlyList<string> segments, string leading)
        {
            if (segments == null || segments.Count == 0 || string.IsNullOrEmpty(leading))
                return new List<string>();

            if (NameEquals(segments[0], leading))
            {
                var list = new List<string>(segments.Count - 1);
                for (int i = 1; i < segments.Count; i++) list.Add(segments[i]);
                return list;
            }

            return new List<string>(segments as IEnumerable<string> ?? Array.Empty<string>());
        }

        public static List<string> GetParentSegments(IReadOnlyList<string> segments)
        {
            var result = new List<string>();
            if (segments == null) return result;
            for (int i = 0; i < segments.Count - 1; i++) result.Add(segments[i]);
            return result;
        }

        public static Transform EnsurePath(Transform root, IReadOnlyList<string> segments)
        {
            if (segments == null || segments.Count == 0) return root;

            var cur = root;
            var start = 0;
            start = GetStartIndex(segments, root?.name);

            for (int i = start; i < segments.Count; i++)
            {
                var seg = segments[i];
                var c = FindChildIgnoreCase(cur, seg);
                if (c == null)
                {
                    var go = new GameObject(seg);
                    Undo.RegisterCreatedObjectUndo(go, "Create Path");
                    go.transform.SetParent(cur, false);
                    c = go.transform;
                }
                cur = c;
            }

            return cur;
        }

        public static Transform FindByName(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name)) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (NameEquals(t.name, name)) return t;
            return null;
        }

        public static Transform FindChildIgnoreCase(Transform parent, string name)
        {
            if (parent == null || string.IsNullOrEmpty(name)) return null;
            for (int i = 0; i < parent.childCount; i++)
            {
                var c = parent.GetChild(i);
                if (NameEquals(c.name, name)) return c;
            }
            return null;
        }

        public static Transform FindBySegments(Transform root, IReadOnlyList<string> segments)
        {
            if (root == null || segments == null || segments.Count == 0) return null;

            var cur = root;
            var start = GetStartIndex(segments, root?.name);

            for (int i = start; i < segments.Count; i++)
            {
                cur = FindChildIgnoreCase(cur, segments[i]);
                if (cur == null) return null;
            }
            return cur;
        }

        public static string GetRelativePath(Transform root, Transform target, bool includeSelf, bool includeRoot)
        {
            if (root == null || target == null) return string.Empty;
            if (target == root) return includeRoot ? root.name : string.Empty;
            if (!target.IsChildOf(root)) return string.Empty;

            var stack = new Stack<string>();
            var cur = includeSelf ? target : target.parent;
            PushAncestors(cur, stack, root);

            if (includeRoot && cur != null && cur == root)
                stack.Push(root.name);

            return string.Join("/", stack.ToArray());
        }

        // ヘルパー: 先祖の名前をスタックに積んでいく。`stopExclusive`は排他的停止点。nullならルートまで辿る。
        private static void PushAncestors(Transform t, Stack<string> stack, Transform stopExclusive)
        {
            var cur = t;
            while (cur != null && cur != stopExclusive)
            {
                stack.Push(cur.name);
                cur = cur.parent;
            }
        }

        // ヘルパー: 先頭にルート名が含まれる可能性があるため、セグメント処理の開始インデックスを決定する。
        private static int GetStartIndex(IReadOnlyList<string> segments, string rootName)
        {
            if (segments == null || segments.Count == 0 || string.IsNullOrEmpty(rootName)) return 0;
            return NameEquals(segments[0], rootName) ? 1 : 0;
        }
    }
}
