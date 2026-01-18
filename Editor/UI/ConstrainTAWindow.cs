// ConstrainTAWindow: Constraint のインポートとビルドを行うエディタウィンドウ
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using ConstrainTA.Editor.Backend;
using VRC.Dynamics;

namespace ConstrainTA.Editor.UI
{
    public class ConstrainTAWindow : EditorWindow
    {
        // 診断UIは検証ビルドでのみ有効にする
    #if CONSTRAINTA_DIAGNOSTICS
        private const bool DiagnosticsEnabled = true;
    #else
        private const bool DiagnosticsEnabled = false;
    #endif

        // コピー元の GameObject（インポート元）
        [SerializeField] private GameObject importRoot;
        [SerializeField] private int selectedImportArmatureIndex;

        // インポートステータス（ユーザー向け表示用）
        [SerializeField] private int lastImportedCount;
        [SerializeField] private string lastImportedSourceName;
        [SerializeField] private string lastImportedArmatureName;
        [SerializeField] private double lastImportedTime;
        [SerializeField] private bool hasImportedOnce;
        // 制作物を配置する GameObject（出力先）: 複数指定（最後は自動で空欄が増える）
        [SerializeField] private List<GameObject> outfitRoots = new();

        // outfitRoots と同じ index で保持（Armature候補Popupの選択）
        [SerializeField] private List<int> selectedArmatureIndices = new();

        // root の instanceID -> 候補キャッシュ（毎回再走査しないようにする）
        private readonly Dictionary<int, List<ArmatureCandidate>> armatureCandidatesCache = new();

        // 読み込まれた ConstraintData のリスト
        private List<ConstraintData> imported = new();

        // UI 内でレンダリングするプレビューエントリ（PreviewScene + RenderTexture）
        private readonly List<PreviewEntry> previewEntries = new();
        [SerializeField] private int activePreviewIndex;
        [SerializeField] private List<string> previewWarnings = new();
        // armature 探索に失敗した項目だけを格納する（警告レベルで出すもの）
        [SerializeField] private List<string> armaturePreviewWarnings = new();
        [SerializeField] private Vector2 previewConsoleScroll = Vector2.zero;
        
        [SerializeField] private Vector2 mainScroll = Vector2.zero;
        // セクション表示のトグル
        [SerializeField] private bool showImportSection = true;
        [SerializeField] private bool showBuildSection = true;
        private GUIStyle sectionFoldoutStyle;
        

        // カウント表示（実運用でも役立つ）
        [SerializeField] private bool showImportedCounts = true;

    #if CONSTRAINTA_DIAGNOSTICS
        [SerializeField] private bool keepConstraintsDisabledAfterBuild;
        [SerializeField] private bool showDiagnostics;
        [SerializeField] private bool debugSkipHumanoidLookup;
        [SerializeField] private bool debugSkipFullPathLookup;
        private string lastActionMessage;
        private double lastActionTime;
    #endif

        [MenuItem("Tools/こんすとれいんた～/こんすとれいんた～")]
        // Open: エディタウィンドウを開く（メニューコマンド）
        public static void Open()
        {
            GetWindow<ConstrainTAWindow>("こんすとれいんた～");
        }

        // OnGUI: UIを描画し、インポートとビルドの操作を受け取る
        private void OnGUI()
        {
            EditorGUILayout.Space(6);

            // ウィンドウ全体をスクロールビューで包み、セクションやターゲットが多数でもツールが使えるようにする
            mainScroll = EditorGUILayout.BeginScrollView(mainScroll);

            EditorGUILayout.LabelField("手順: 1)いんぽ～と → 2)びるど", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(6);

            if (sectionFoldoutStyle == null)
            {
                sectionFoldoutStyle = new GUIStyle(EditorStyles.foldout)
                {
                    fontSize = 15,
                    fontStyle = FontStyle.Bold
                };
            }

            showImportSection = EditorGUILayout.Foldout(showImportSection, "1) いんぽ～と！", true, sectionFoldoutStyle);
            if (showImportSection)
            {
                DrawImportSection();
            }
            EditorGUILayout.Space(6);
            // 幅をウィンドウ全体に広げた濃いセパレータ（高さを増して目立たせる）
            var sepRect = GUILayoutUtility.GetRect(1, 12, GUILayout.ExpandWidth(true));
            var fullRect = new Rect(0, sepRect.y, position.width, sepRect.height);
            EditorGUI.DrawRect(fullRect, new Color(0.18f, 0.18f, 0.18f, 1f));
            EditorGUILayout.Space(6);
            showBuildSection = EditorGUILayout.Foldout(showBuildSection, "2) びるど！（配置先へ生成）", true, sectionFoldoutStyle);
            if (showBuildSection)
            {
                DrawBuildSection();
            }
            EditorGUILayout.Space(10);
            EditorGUILayout.Space(10);

#if CONSTRAINTA_DIAGNOSTICS
            if (DiagnosticsEnabled)
            {
                EditorGUILayout.Space(10);
                DrawDiagnosticsSection();
                EditorGUILayout.Space(10);
                DrawLastAction();
            }
#endif

            EditorGUILayout.EndScrollView();
        }

        private void OnDisable()
        {
            ClearPreviewEntries();
        }

        private void ResetImportStatus()
        {
            imported = new List<ConstraintData>();
            lastImportedCount = 0;
            lastImportedSourceName = string.Empty;
            lastImportedArmatureName = string.Empty;
            lastImportedTime = 0;
            hasImportedOnce = false;
            previewWarnings?.Clear();
            armaturePreviewWarnings?.Clear();
        }

        // Popup のラベル内に含まれるパス区切り文字 '/' をそのまま渡すと
        // Unity のメニューが階層化されてしまうため、別文字に置換して
        // ドロップダウン内で一段表示にする。
        private static string SanitizePopupLabel(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            // スラッシュやバックスラッシュはメニュー分割に使われることがあるため
            // 見た目は同じに近い全角スラッシュへ置換する。
            return s.Replace("/", "／").Replace("\\", "＼");
        }

        // 表示用に長いラベルを短縮する。最後のセグメントを優先して残し、
        // 中間を省略して '…／' で示す。ツールチップには元の（サニタイズ済み）文字列を入れる。
        private static string TruncateLabelForDisplay(string original, int maxChars = 48)
        {
            if (string.IsNullOrEmpty(original)) return original;
            var sanitized = SanitizePopupLabel(original);
            if (sanitized.Length <= maxChars) return sanitized;

            // 分割は全角や半角スラッシュ両方に対応
            var segments = sanitized.Split(new[] {'／', '/', '＼', '\\'}, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0) return sanitized.Substring(0, Math.Min(maxChars, sanitized.Length));

            // 常に最後のセグメントは残す。先頭から少し取って…にするか末尾を中心に取る
            var last = segments[segments.Length - 1];
            // 余裕があれば直前のセグメントを1つか2つ含めるよう試みる
            var display = last;
            for (int k = segments.Length - 2; k >= 0; k--)
            {
                var candidate = segments[k] + "／" + display;
                if (candidate.Length + 3 /* for ellipsis */ <= maxChars)
                {
                    display = candidate;
                    continue;
                }
                break;
            }

            if (display.Length + 3 >= sanitized.Length) return sanitized.Substring(0, Math.Min(maxChars, sanitized.Length));
            return "…／" + display;
        }

        private static GUIContent[] BuildPopupContents(IEnumerable<string> labels)
        {
            var list = labels.Select(l => {
                var tooltip = SanitizePopupLabel(l);
                var text = TruncateLabelForDisplay(l);
                return new GUIContent(text, tooltip);
            }).ToArray();
            return list;
        }

        private void DrawImportSection()
        {
            var newImportRoot = (GameObject)EditorGUILayout.ObjectField("コピー元（アバター／衣装）", importRoot, typeof(GameObject), true);
            if (newImportRoot != importRoot)
            {
                importRoot = newImportRoot;
                selectedImportArmatureIndex = 0;
                ResetImportStatus();
            }

            EditorGUILayout.Space(2);
            Transform importArmatureRoot = null;
            bool showArmatureWarning = false;
            if (importRoot != null)
            {
                var candidates = GetArmatureCandidates(importRoot);
                if (candidates == null || candidates.Count == 0)
                {
                    EditorGUILayout.HelpBox("Humanoidが見つかりません。Humanoidが設定されたコピー元を選んでください。", MessageType.Error);
                }
                else
                {
                    selectedImportArmatureIndex = Mathf.Clamp(selectedImportArmatureIndex, 0, candidates.Count - 1);
                    var contents = BuildPopupContents(candidates.Select(a => a.Label));
                    // プルダウンで候補を選択（クリックでドロップダウンが開きます）
                    selectedImportArmatureIndex = EditorGUILayout.Popup(new GUIContent("インポートArmature"), selectedImportArmatureIndex, contents, GUILayout.ExpandWidth(true));
                    importArmatureRoot = candidates[selectedImportArmatureIndex].ArmatureRoot;
                    if (candidates.Count >= 2)
                    {
                        showArmatureWarning = true;
                    }
                }
            }

            // 区切り線
            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            EditorGUILayout.Space(2);

            if (showArmatureWarning)
            {
                EditorGUILayout.HelpBox("Humanoid armature候補が複数検出されています。意図したarmatureを選択してください。", MessageType.Warning);
                EditorGUILayout.Space(2);
            }

            using (new EditorGUI.DisabledScope(importRoot == null))
            {
                if (GUILayout.Button("いんぽ～とを実行！", GUILayout.Height(28)))
                {
                    imported = ConstraintImporter.Import(importRoot, importArmatureRoot);
                    lastImportedCount = imported?.Count ?? 0;
                    lastImportedSourceName = importRoot != null ? importRoot.name : string.Empty;
                    lastImportedArmatureName = importArmatureRoot != null ? importArmatureRoot.name : string.Empty;
                    lastImportedTime = EditorApplication.timeSinceStartup;
                    hasImportedOnce = true;
#if CONSTRAINTA_DIAGNOSTICS
                    if (DiagnosticsEnabled) SetLastAction($"Import: {imported?.Count ?? 0} constraints");
#endif
                }
            }

            EditorGUILayout.Space(6);

            if (!hasImportedOnce)
            {
                EditorGUILayout.HelpBox("コピー元を選んで「いんぽ～とを実行！」を押してください。", MessageType.Info);
                return;
            }

            // 結果表示を独立させて余白を入れる
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            EditorGUILayout.Space(2);
            var src = string.IsNullOrEmpty(lastImportedSourceName) ? "(unknown)" : lastImportedSourceName;
            var count = imported?.Count ?? 0;
            var type = count > 0 ? MessageType.None : MessageType.Warning;
            // 見出しを中央寄せ・大きめ・背景色付きで強調
            var headlineStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(0, 0, 6, 6)
            };
            var bgRect = GUILayoutUtility.GetRect(1, 32, GUILayout.ExpandWidth(true));
            Color prevColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.85f, 1.0f, 0.85f, 1.0f); // 淡い緑
            GUI.Box(bgRect, GUIContent.none);
            GUI.backgroundColor = prevColor;
            GUI.Label(bgRect, "いんぽ～としました！", headlineStyle);
            EditorGUILayout.Space(2);
            EditorGUILayout.HelpBox($"結果: {count} 件  /  コピー元: {src}", type);
            // インポート見出しの下に折りたたみ式でインポート件数を表示する
            if (imported != null && imported.Count > 0)
            {
                EditorGUILayout.Space(4);
                showImportedCounts = EditorGUILayout.Foldout(showImportedCounts, "インポート内容（種類別カウント）", true);
                if (showImportedCounts)
                {
                    EditorGUILayout.Space(4);
                    DrawCounts();
                }
            }
        }

        private void DrawBuildSection()
        {
            EnsureOutfitTargetsList();

            var targets = new List<(GameObject OutfitRoot, List<ArmatureCandidate> Candidates, int SlotIndex)>();

            for (int i = 0; i < outfitRoots.Count; i++)
            {
                var root = outfitRoots[i];

                EditorGUILayout.BeginVertical(GUI.skin.box);
                EditorGUILayout.Space(2);

                EditorGUILayout.BeginHorizontal();
                var label = i == 0 ? "配置先" : $"配置先 {i + 1}";
                var newRoot = (GameObject)EditorGUILayout.ObjectField(label, root, typeof(GameObject), true);

                using (new EditorGUI.DisabledScope(newRoot == null))
                {
                    if (GUILayout.Button("↻", GUILayout.Width(24)))
                    {
                        RefreshArmatureCandidates(newRoot);
                    }
                }

                var isLastSlot = i == outfitRoots.Count - 1;
                var canRemove = !isLastSlot || newRoot != null;
                using (new EditorGUI.DisabledScope(!canRemove))
                {
                    if (GUILayout.Button("−", GUILayout.Width(24)))
                    {
                        RemoveOutfitTargetAt(i);
                        EnsureOutfitTargetsList();
                        GUIUtility.ExitGUI();
                    }
                }

                EditorGUILayout.EndHorizontal();

                if (newRoot != root)
                {
                    outfitRoots[i] = newRoot;
                    selectedArmatureIndices[i] = 0;
                    if (newRoot != null) RefreshArmatureCandidates(newRoot);
                }

                if (newRoot == null)
                {
                    EditorGUILayout.Space(2);
                    EditorGUILayout.EndVertical();
                    continue;
                }

                // Assets 内のプレハブアセットを選択した場合は警告してスキップする（シーンインスタンスではないため）。
                // プレハブアセットは Hierarchy に配置されないため、ビルド処理はシーン内のインスタンスを前提としている。
                // そのためこのスロットでは候補検出をスキップし、ランタイムエラーを回避する。
                if (PrefabUtility.IsPartOfPrefabAsset(newRoot))
                {
                    EditorGUILayout.HelpBox("選択されたオブジェクトはAssets内のプレハブです。Hierarchyに配置してからビルドしてください。", MessageType.Warning);
                    EditorGUILayout.EndVertical();
                    EditorGUILayout.Space(4);
                    continue;
                }

                EditorGUILayout.Space(4);
                var candidates = GetArmatureCandidates(newRoot);
                if (candidates == null || candidates.Count == 0)
                {
                    EditorGUILayout.HelpBox("配置先にHumanoidが見つかりません。Humanoid対応の衣装を選んでください。", MessageType.Error);
                    EditorGUILayout.EndVertical();
                    continue;
                }

                if (candidates.Count >= 2)
                {
                    EditorGUILayout.HelpBox("Armature候補が複数あります。正しい候補を選択してください。", MessageType.Warning);
                }

                selectedArmatureIndices[i] = Mathf.Clamp(selectedArmatureIndices[i], 0, candidates.Count - 1);
                var contents = BuildPopupContents(candidates.Select(a => a.Label));
                // プルダウンで候補を選択（クリックでドロップダウンが開きます）
                selectedArmatureIndices[i] = EditorGUILayout.Popup(new GUIContent("検出したArmature"), selectedArmatureIndices[i], contents, GUILayout.ExpandWidth(true));
                var detectedArmatureRoot = candidates[selectedArmatureIndices[i]].ArmatureRoot;

                targets.Add((newRoot, candidates, i));

                EditorGUILayout.Space(2);
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(4);
            }

#if CONSTRAINTA_DIAGNOSTICS
            if (DiagnosticsEnabled)
            {
                keepConstraintsDisabledAfterBuild = EditorGUILayout.ToggleLeft("ビルド後はIsActiveをOFFのまま（診断用）", keepConstraintsDisabledAfterBuild);
                if (keepConstraintsDisabledAfterBuild)
                {
                    EditorGUILayout.HelpBox("診断用: ビルド直後はOFFのままにします。必要なら手動でActivateしてください。", MessageType.Info);
                }
            }
#endif

            // ビルド実行前にプレビューを表示（ユーザーが確認できるように）
            EditorGUILayout.Space(4);
            DrawPreviewSection();
            EditorGUILayout.Space(4);

            var canBuild = importRoot != null && imported != null && imported.Count > 0 && targets.Count > 0;
            EditorGUILayout.Space(6);
            using (new EditorGUI.DisabledScope(!canBuild))
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("びるど！（全配置先）", GUILayout.Height(30), GUILayout.Width(220)))
                {
#if CONSTRAINTA_DIAGNOSTICS
                    var keepDisabled = DiagnosticsEnabled && keepConstraintsDisabledAfterBuild;
#else
                    const bool keepDisabled = false;
#endif

#if CONSTRAINTA_DIAGNOSTICS
                    ConstraintBuilder.DebugSkipHumanoidLookup = DiagnosticsEnabled && debugSkipHumanoidLookup;
                    ConstraintBuilder.DebugSkipFullPathLookup = DiagnosticsEnabled && debugSkipFullPathLookup;
#else
                    ConstraintBuilder.DebugSkipHumanoidLookup = false;
                    ConstraintBuilder.DebugSkipFullPathLookup = false;
#endif
                    var built = 0;
                    foreach (var t in targets)
                    {
                        if (t.OutfitRoot == null || t.Candidates == null || t.Candidates.Count == 0) continue;

                        var selectedIndex = selectedArmatureIndices[t.SlotIndex];
                        selectedIndex = Mathf.Clamp(selectedIndex, 0, t.Candidates.Count - 1);

                        if (!MaybeWarnAvatarArmatureSelectionOnBuild(t.OutfitRoot, t.Candidates, ref selectedIndex))
                            return; // ビルド全体をキャンセル

                        selectedArmatureIndices[t.SlotIndex] = selectedIndex;
                        var armatureRoot = t.Candidates[selectedIndex].ArmatureRoot;
                        if (armatureRoot == null) continue;

                        ConstraintBuilder.Build(t.OutfitRoot, armatureRoot, imported, keepConstraintsDisabledAfterBuild: keepDisabled);
                        built++;
                    }
#if CONSTRAINTA_DIAGNOSTICS
                    if (DiagnosticsEnabled) SetLastAction($"Build done: {built} targets, {imported.Count} constraints each");
#endif
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }

            // ウィンドウ内プレビューコンソール: プレビューのヘルプボックス下に警告や情報を表示する
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("コンソール", EditorStyles.boldLabel);
            var totalMsgs = (armaturePreviewWarnings?.Count ?? 0) + (previewWarnings?.Count ?? 0);
            var boxHeight = Mathf.Min(220, Math.Max(60, 18 * totalMsgs + 8));
            EditorGUILayout.BeginVertical(GUI.skin.box);
            previewConsoleScroll = EditorGUILayout.BeginScrollView(previewConsoleScroll, GUILayout.Height(boxHeight));
            if (armaturePreviewWarnings != null && armaturePreviewWarnings.Count > 0)
            {
                EditorGUILayout.LabelField("警告一覧:", EditorStyles.boldLabel);
                foreach (var msg in armaturePreviewWarnings)
                {
                    EditorGUILayout.HelpBox(msg, MessageType.Warning);
                }
            }
            if (previewWarnings != null && previewWarnings.Count > 0)
            {
                EditorGUILayout.LabelField("情報一覧:", EditorStyles.boldLabel);
                foreach (var msg in previewWarnings)
                {
                    EditorGUILayout.LabelField("- " + msg, EditorStyles.wordWrappedLabel);
                }
            }
            if (totalMsgs == 0)
            {
                EditorGUILayout.LabelField("（プレビュー出力は空です）", EditorStyles.centeredGreyMiniLabel);
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            if (!canBuild)
            {
                if (importRoot == null)
                {
                    EditorGUILayout.HelpBox("まずコピー元を選び、インポートを実行してください。", MessageType.Info);
                }
                else if (imported == null || imported.Count == 0)
                {
                    EditorGUILayout.HelpBox("まずコピー元をインポートしてください。", MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox("出力先（配置先）を1つ以上指定してください。", MessageType.Info);
                }
            }
        }

        private void DrawPreviewSection()
        {
            //EditorGUILayout.LabelField("2.5) プレビュー（非破壊）", EditorStyles.boldLabel);
            //EditorGUILayout.HelpBox("プレビュー表示は無効化しました。代わりにコンソールへ警告ログを出します。", MessageType.Warning);

            var canPreview = imported != null && imported.Count > 0 && outfitRoots.Any(r => r != null);

            // プレビュー可能条件を満たさない場合、過去の警告は表示しないようにクリアする
            if (!canPreview)
            {
                previewWarnings?.Clear();
                armaturePreviewWarnings?.Clear();
            }

            using (new EditorGUI.DisabledScope(!canPreview))
            {
                if (GUILayout.Button("てすとびるど出力"))
                {
                    ClearPreviewEntries();
                    previewWarnings ??= new List<string>();
                    armaturePreviewWarnings ??= new List<string>();
                    previewWarnings.Clear();
                    armaturePreviewWarnings.Clear();
                    var warned = 0;
                    var warnedArmature = 0;
                    for (int i = 0; i < outfitRoots.Count; i++)
                    {
                        var root = outfitRoots[i];
                        if (root == null) continue;

                        var candidates = GetArmatureCandidates(root);
                        if (candidates == null || candidates.Count == 0)
                        {
                            var msg = $"配置先 '{root.name}' にHumanoidが見つかりません。";
                            // Armature探索失敗は警告扱い
                            Debug.LogWarning($"[ConstrainTA] プレビュー警告: {msg}");
                            armaturePreviewWarnings.Add(msg);
                            warnedArmature++;
                            warned++;
                            continue;
                        }

                        var idx = Mathf.Clamp(selectedArmatureIndices[i], 0, candidates.Count - 1);
                        var armatureRoot = candidates[idx].ArmatureRoot;
                        var armatureName = armatureRoot != null ? armatureRoot.name : "(null)";

                        // 各インポート済み制約のソースがこのアーマチュアで解決されるかをチェックする。
                        foreach (var d in imported)
                        {
                            if (d == null) continue;
                            var missingSources = new List<string>();
                            var resolvedSources = new List<string>();

                            foreach (var s in d.sources)
                            {
                                Transform resolved = null;
                                try
                                {
                                    resolved = ConstraintBuilder.ResolveTransformForPreview(root.transform, armatureRoot, s.pathFromArmature, string.Empty, s.name);
                                }
                                catch
                                {
                                    // 保守的なフォールバック: 完全一致パスをまず試し、その後大文字小文字を無視した名前検索を行う
                                    resolved = null;
                                    if (armatureRoot != null && !string.IsNullOrEmpty(s.pathFromArmature))
                                    {
                                        try { resolved = armatureRoot.Find(s.pathFromArmature); }
                                        catch { resolved = null; }
                                    }
                                    if (resolved == null && armatureRoot != null && !string.IsNullOrEmpty(s.name))
                                    {
                                        var all = armatureRoot.GetComponentsInChildren<Transform>(true);
                                        for (int ai = 0; ai < all.Length; ai++)
                                        {
                                            if (string.Equals(all[ai].name, s.name, StringComparison.OrdinalIgnoreCase))
                                            {
                                                resolved = all[ai];
                                                break;
                                            }
                                        }
                                    }
                                }

                                if (resolved == null)
                                {
                                    missingSources.Add($"{s.name} (path='{s.pathFromArmature}')");
                                }
                                else
                                {
                                    resolvedSources.Add(resolved.name);
                                }
                            }

                            if (missingSources.Count > 0)
                            {
                                var msg = $"配置先='{root.name}': Constraint '{d.emptyName}' の source が見つかりません: {string.Join(", ", missingSources)}";
                                armaturePreviewWarnings.Add(msg);
                                Debug.LogWarning($"[ConstrainTA] プレビュー警告: {msg}");
                                warned++;
                            }
                            else
                            {
                                var info = $"配置先='{root.name}': Constraint '{d.emptyName}' -> sources: {string.Join(", ", resolvedSources)}";
                                previewWarnings.Add(info);
                                Debug.Log($"[ConstrainTA] プレビュー: {info}");
                                warned++;
                            }
                        }
                    }

                    if (warned == 0)
                    {
                        const string msg = "プレビュー対象がありません。";
                        Debug.Log($"[ConstrainTA] プレビュー: {msg}");
                        previewWarnings.Add(msg);
                    }

                    Repaint();
                }
                }
                            EditorGUILayout.LabelField("ウィンドウ内の動的プレビューコンソール: プレビューのヘルプボックス下に警告/情報を表示", EditorStyles.boldLabel);


            /*
            var hasPreviewContent = (previewEntries != null && previewEntries.Count > 0)
                                    || (previewWarnings != null && previewWarnings.Count > 0)
                                    || (armaturePreviewWarnings != null && armaturePreviewWarnings.Count > 0);

            using (new EditorGUI.DisabledScope(!hasPreviewContent))
            {
                if (GUILayout.Button("ぷれびゅ～削除"))
                {
                    ClearPreviewEntries();
                    previewWarnings?.Clear();
                    armaturePreviewWarnings?.Clear();
                    Repaint();
                }
            }
            */

            if ((armaturePreviewWarnings != null && armaturePreviewWarnings.Count > 0) || (previewWarnings != null && previewWarnings.Count > 0))
            {
                if (armaturePreviewWarnings != null && armaturePreviewWarnings.Count > 0)
                {
                    // Armature探索失敗がある場合のみ警告表示
                    EditorGUILayout.HelpBox($"プレビュー警告: {armaturePreviewWarnings.Count} 件（Armature探索失敗）。詳細はコンソールで確認してください。", MessageType.Warning);
                }
                else
                {
                    // その他の情報は注意レベルではなく情報として表示
                    EditorGUILayout.HelpBox($"プレビュー情報: {previewWarnings.Count} 件あります。詳細はコンソールで確認してください。", MessageType.Info);
                }

                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("コンソールで開く", GUILayout.Width(160)))
                {
                    var sb = new System.Text.StringBuilder();
                    if (armaturePreviewWarnings != null && armaturePreviewWarnings.Count > 0)
                    {
                        sb.AppendLine($"[ConstrainTA] プレビュー警告一覧: {armaturePreviewWarnings.Count} 件");
                        foreach (var msg in armaturePreviewWarnings)
                        {
                            sb.AppendLine("- " + msg);
                        }
                        Debug.LogWarning(sb.ToString());
                    }
                    else
                    {
                        sb.AppendLine($"[ConstrainTA] プレビュー情報一覧: {previewWarnings.Count} 件");
                        foreach (var msg in previewWarnings)
                        {
                            sb.AppendLine("- " + msg);
                        }
                        Debug.Log(sb.ToString());
                    }
                    EditorApplication.ExecuteMenuItem("Window/General/Console");
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        private void ClearPreviewEntries()
        {
            for (int i = previewEntries.Count - 1; i >= 0; i--)
            {
                previewEntries[i]?.Dispose();
            }
            previewEntries.Clear();
        }

        private PreviewEntry CreatePreviewEntry(GameObject sourceRoot, Transform sourceArmatureRoot, List<ConstraintData> dataList)
        {
            if (sourceRoot == null) return null;

            var previewScene = EditorSceneManager.NewPreviewScene();
            var previewRoot = Instantiate(sourceRoot);
            previewRoot.name = $"{sourceRoot.name}_ConstrainTA_Preview";
            SetHideFlagsRecursively(previewRoot, HideFlags.HideAndDontSave);
            var previewLayer = GetPreviewLayer();
            SetLayerRecursively(previewRoot, previewLayer);
            SceneManager.MoveGameObjectToScene(previewRoot, previewScene);

            ConstraintBuilder.BuildPreviewOnClone(previewRoot, sourceRoot, sourceArmatureRoot, dataList);
            SetLayerRecursively(previewRoot, previewLayer);

            var cameraGo = new GameObject("ConstrainTA Preview Camera");
            SetHideFlagsRecursively(cameraGo, HideFlags.HideAndDontSave);
            SceneManager.MoveGameObjectToScene(cameraGo, previewScene);
            var camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Color;
            camera.backgroundColor = new Color(0.18f, 0.18f, 0.18f, 1f);
            camera.orthographic = false;
            camera.cullingMask = 1 << previewLayer;

            var lightGo = new GameObject("ConstrainTA Preview Light");
            SetHideFlagsRecursively(lightGo, HideFlags.HideAndDontSave);
            SceneManager.MoveGameObjectToScene(lightGo, previewScene);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            light.cullingMask = 1 << previewLayer;

            var bounds = CalculateBounds(previewRoot);

            var entry = new PreviewEntry(sourceRoot, previewRoot, previewScene, camera, light, bounds);
            ApplyInitialPreviewCamera(entry);
            return entry;
        }

        private void DrawPreviewEntry(PreviewEntry entry, int index)
        {
            if (entry == null || !entry.IsValid) return;

            EditorGUILayout.LabelField($"Preview {index}: {entry.SourceRoot.name}", EditorStyles.boldLabel);

            var rect = GUILayoutUtility.GetRect(10f, 360f, GUILayout.ExpandWidth(true));
            rect.height = Mathf.Max(320f, rect.height);
            EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f, 1f));

            entry.ControlId = GUIUtility.GetControlID(FocusType.Passive, rect);

            HandlePreviewInput(entry, rect);

            if (Event.current.type == EventType.Repaint)
            {
                RenderPreview(entry, rect);
            }

            DrawConstraintOverlay(entry, rect);

            EditorGUILayout.HelpBox($"中心: {VectorToShort(entry.Bounds.center)}  /  サイズ: {VectorToShort(entry.Bounds.size)}", MessageType.None);
        }

        private void DrawPreviewNavigation()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(previewEntries.Count <= 1))
                {
                    if (GUILayout.Button("◀", GUILayout.Width(32)))
                    {
                        activePreviewIndex = (activePreviewIndex - 1 + previewEntries.Count) % previewEntries.Count;
                        Repaint();
                    }
                }

                EditorGUILayout.LabelField($"{activePreviewIndex + 1} / {previewEntries.Count}", GUILayout.Width(70));

                using (new EditorGUI.DisabledScope(previewEntries.Count <= 1))
                {
                    if (GUILayout.Button("▶", GUILayout.Width(32)))
                    {
                        activePreviewIndex = (activePreviewIndex + 1) % previewEntries.Count;
                        Repaint();
                    }
                }

                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Reset View", GUILayout.Width(90)))
                {
                    if (previewEntries.Count > 0)
                        ResetPreviewCamera(previewEntries[activePreviewIndex]);
                    Repaint();
                }

                if (GUILayout.Button("Front/Back", GUILayout.Width(90)))
                {
                    if (previewEntries.Count > 0)
                    {
                        var entry = previewEntries[activePreviewIndex];
                        entry.ForwardSign *= -1f;
                        ApplyInitialPreviewCamera(entry);
                    }
                    Repaint();
                }
            }
        }

        private static void RenderPreview(PreviewEntry entry, Rect rect)
        {
            var width = Mathf.Max(1, Mathf.RoundToInt(rect.width));
            var height = Mathf.Max(1, Mathf.RoundToInt(rect.height));

            if (entry.RenderTexture == null || entry.LastWidth != width || entry.LastHeight != height)
            {
                if (entry.RenderTexture != null)
                {
                    entry.RenderTexture.Release();
                    DestroyImmediate(entry.RenderTexture);
                }
                entry.RenderTexture = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32)
                {
                    name = "ConstrainTA Preview RT",
                    hideFlags = HideFlags.HideAndDontSave
                };
                entry.LastWidth = width;
                entry.LastHeight = height;
            }

            // 右ボタンドラッグはカメラをその場で回転させるのみ（位置移動を伴わない）。
            UpdateCameraTransform(entry);
            entry.Camera.targetTexture = entry.RenderTexture;
            entry.Camera.aspect = width / (float)height;
            entry.Camera.Render();
            entry.Camera.targetTexture = null;

            GUI.DrawTexture(rect, entry.RenderTexture, ScaleMode.ScaleToFit, false);
        }

        private static void DrawConstraintOverlay(PreviewEntry entry, Rect rect)
        {
            if (entry == null || entry.Camera == null || entry.PreviewRoot == null) return;
            var constraints = entry.PreviewRoot.GetComponentsInChildren<VRCConstraintBase>(true);
            if (constraints == null || constraints.Length == 0) return;

            var cam = entry.Camera;
            foreach (var c in constraints)
            {
                if (c == null) continue;
                var pos = c.transform.position;
                var screen = cam.WorldToScreenPoint(pos);
                if (screen.z <= 0f) continue;

                var x = rect.x + (screen.x / cam.pixelWidth) * rect.width;
                var y = rect.y + (1f - (screen.y / cam.pixelHeight)) * rect.height;
                var dot = new Rect(x - 4f, y - 4f, 8f, 8f);
                if (!rect.Overlaps(dot)) continue;
                Handles.BeginGUI();
                Handles.color = new Color(1f, 0.9f, 0.2f, 0.95f);
                Handles.DrawSolidDisc(new Vector3(x, y, 0f), Vector3.forward, 4f);
                Handles.EndGUI();
            }
        }

        private static void ApplyInitialPreviewCamera(PreviewEntry entry)
        {
            if (entry == null || entry.Camera == null || entry.PreviewRoot == null) return;

            var bounds = entry.Bounds;
            var radius = Mathf.Max(0.25f, bounds.extents.magnitude);
            var distance = radius * 2.3f;
            var center = bounds.center;
            var root = entry.PreviewRoot.transform;
            var forward = GetHumanoidForward(root) ?? (root != null ? root.forward : Vector3.forward);
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            forward.Normalize();
            forward *= entry.ForwardSign;

            var eye = center + forward * distance + Vector3.up * (radius * 0.2f);
            entry.Camera.transform.position = eye;
            entry.Camera.transform.LookAt(center);
            entry.Camera.nearClipPlane = Mathf.Max(0.01f, distance - radius * 3f);
            entry.Camera.farClipPlane = distance + radius * 3f;

            entry.Focus = center;
            entry.Distance = distance;
            entry.CameraPosition = eye;
            entry.CameraRotation = entry.Camera.transform.rotation;
            var euler = entry.CameraRotation.eulerAngles;
            entry.Pitch = ClampPitch(ToSignedAngle(euler.x));
            entry.Yaw = ToSignedAngle(euler.y);
        }

        private static Vector3? GetHumanoidForward(Transform root)
        {
            if (root == null) return null;
            var animator = root.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman) return null;
            return animator.transform.forward;
        }

        private void HandlePreviewInput(PreviewEntry entry, Rect rect)
        {
            var e = Event.current;
            if (e == null) return;
            if (!rect.Contains(e.mousePosition) && !entry.IsRmbLook) return;

            var orbitSpeed = 0.4f;
            var panSpeed = 0.0025f;
            var zoomSpeed = 0.12f;
            var isAlt = e.alt;
            var isCtrl = e.control || e.command;
            if (entry != null)
            {
                var scale = Mathf.Max(0.1f, entry.Bounds.extents.magnitude);
                panSpeed *= Mathf.Clamp(scale, 0.3f, 4f);
            }

            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.LeftArrow && previewEntries.Count > 1)
                {
                    activePreviewIndex = (activePreviewIndex - 1 + previewEntries.Count) % previewEntries.Count;
                    e.Use();
                    Repaint();
                    return;
                }
                if (e.keyCode == KeyCode.RightArrow && previewEntries.Count > 1)
                {
                    activePreviewIndex = (activePreviewIndex + 1) % previewEntries.Count;
                    e.Use();
                    Repaint();
                    return;
                }
                if (e.keyCode == KeyCode.F)
                {
                    ResetPreviewCamera(entry);
                    e.Use();
                    Repaint();
                    return;
                }
            }

            if (e.type == EventType.MouseDown && e.button == 1 && !e.alt)
            {
                entry.IsRmbLook = true;
                entry.CameraPosition = entry.Camera != null ? entry.Camera.transform.position : entry.CameraPosition;
                entry.CameraRotation = entry.Camera != null ? entry.Camera.transform.rotation : entry.CameraRotation;
                entry.Distance = Mathf.Max(entry.MinDistance, Vector3.Distance(entry.CameraPosition, entry.Focus));
                entry.LastMousePosition = e.mousePosition;
                entry.LastUpdateTime = EditorApplication.timeSinceStartup;
                GUIUtility.hotControl = entry.ControlId;
                GUIUtility.keyboardControl = entry.ControlId;
                EditorGUIUtility.SetWantsMouseJumping(1);
                e.Use();
                Repaint();
                return;
            }

            if (e.type == EventType.MouseUp && e.button == 1)
            {
                if (entry.IsRmbLook)
                {
                    entry.IsRmbLook = false;
                    var forward = entry.CameraRotation * Vector3.forward;
                    entry.Distance = Mathf.Max(entry.MinDistance, entry.Distance);
                    entry.Focus = entry.CameraPosition + forward * entry.Distance;
                }
                if (GUIUtility.hotControl == entry.ControlId)
                    GUIUtility.hotControl = 0;
                EditorGUIUtility.SetWantsMouseJumping(0);
                e.Use();
                Repaint();
                return;
            }

            if (e.type == EventType.ScrollWheel)
            {
                var delta = e.delta.y;
                entry.Distance = Mathf.Max(entry.MinDistance, entry.Distance * (1f + delta * zoomSpeed));
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseDrag && e.button == 0 && isAlt && !isCtrl)
            {
                var d = e.delta;
                entry.Yaw += d.x * orbitSpeed;
                entry.Pitch -= d.y * orbitSpeed;
                entry.Pitch = Mathf.Clamp(entry.Pitch, -80f, 80f);
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseDrag && e.button == 2)
            {
                var d = e.delta;
                if (e.shift)
                {
                    entry.Focus += Vector3.up * (d.y * panSpeed * entry.Distance);
                }
                else
                {
                    var right = entry.Camera.transform.right;
                    var up = entry.Camera.transform.up;
                    entry.Focus += (-right * d.x + up * d.y) * (entry.Distance * panSpeed);
                }
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseDrag && e.button == 0 && isAlt && isCtrl)
            {
                // Alt + Ctrl + 左ドラッグでパン（SceneView と同様）
                var d = e.delta;
                var right = entry.Camera.transform.right;
                var up = entry.Camera.transform.up;
                entry.Focus += (-right * d.x + up * d.y) * (entry.Distance * panSpeed);
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseDrag && e.button == 1 && isAlt)
            {
                // Alt + 右ドラッグでズーム（SceneView と同様）
                var d = e.delta;
                entry.Distance = Mathf.Max(entry.MinDistance, entry.Distance * (1f + d.y * zoomSpeed * 0.03f));
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseDrag && e.button == 1 && !isAlt)
            {
                if (!entry.IsRmbLook)
                {
                    entry.IsRmbLook = true;
                    entry.CameraPosition = entry.Camera != null ? entry.Camera.transform.position : entry.CameraPosition;
                    entry.CameraRotation = entry.Camera != null ? entry.Camera.transform.rotation : entry.CameraRotation;
                    entry.Distance = Mathf.Max(entry.MinDistance, Vector3.Distance(entry.CameraPosition, entry.Focus));
                    entry.LastUpdateTime = EditorApplication.timeSinceStartup;
                }
                var d = e.delta;
                entry.CameraRotation = Quaternion.AngleAxis(d.x * orbitSpeed, Vector3.up) * entry.CameraRotation;
                var right = entry.CameraRotation * Vector3.right;
                entry.CameraRotation = Quaternion.AngleAxis(d.y * orbitSpeed, right) * entry.CameraRotation;
                var euler = entry.CameraRotation.eulerAngles;
                entry.Pitch = ClampPitch(ToSignedAngle(euler.x));
                entry.Yaw = ToSignedAngle(euler.y);
                entry.CameraRotation = Quaternion.Euler(entry.Pitch, entry.Yaw, 0f);
                entry.LastMousePosition = e.mousePosition;
                e.Use();
                Repaint();
            }
        }

        private static void UpdateCameraTransform(PreviewEntry entry)
        {
            var rotation = entry.IsRmbLook ? entry.CameraRotation : Quaternion.Euler(entry.Pitch, entry.Yaw, 0f);
            if (entry.IsRmbLook)
            {
                entry.Camera.transform.position = entry.CameraPosition;
            }
            else
            {
                var offset = rotation * new Vector3(0f, 0f, -entry.Distance);
                entry.Camera.transform.position = entry.Focus + offset;
                entry.CameraRotation = rotation;
            }
            entry.Camera.transform.rotation = rotation;
        }

        private static void ApplyRmbFlyMove(PreviewEntry entry)
        {
            if (entry == null || !entry.IsRmbLook) return;

            var now = EditorApplication.timeSinceStartup;
            var dt = (float)(now - entry.LastUpdateTime);
            if (dt <= 0f) return;
            entry.LastUpdateTime = now;

            var rotation = entry.CameraRotation;
            var forward = rotation * Vector3.forward;
            var right = rotation * Vector3.right;
            var up = Vector3.up;

            var move = Vector3.zero;
            if (entry.KeyW) move += forward;
            if (entry.KeyS) move -= forward;
            if (entry.KeyD) move += right;
            if (entry.KeyA) move -= right;
            if (entry.KeyE) move += up;
            if (entry.KeyQ) move -= up;

            if (move.sqrMagnitude > 0f)
            {
                var speed = entry.BaseMoveSpeed * (entry.KeyShift ? 4f : 1f);
                entry.CameraPosition += move.normalized * speed * dt;
                entry.Focus = entry.CameraPosition + rotation * Vector3.forward * entry.Distance;
            }
        }

        private static float ToSignedAngle(float angle)
        {
            if (angle > 180f) angle -= 360f;
            return angle;
        }

        private static float ClampPitch(float pitch)
        {
            return Mathf.Clamp(pitch, -80f, 80f);
        }

        private static void ResetPreviewCamera(PreviewEntry entry)
        {
            if (entry == null) return;
            entry.ForwardSign = 1f;
            ApplyInitialPreviewCamera(entry);
        }

        private static Bounds CalculateBounds(GameObject root)
        {
            if (root == null) return new Bounds(Vector3.zero, Vector3.one);
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var has = false;
            var bounds = new Bounds(root.transform.position, Vector3.one * 0.5f);
            foreach (var r in renderers)
            {
                if (r == null) continue;
                if (!has)
                {
                    bounds = r.bounds;
                    has = true;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }
            return bounds;
        }

        private static void SetHideFlagsRecursively(GameObject root, HideFlags flags)
        {
            if (root == null) return;
            root.hideFlags = flags;
            foreach (Transform t in root.transform)
            {
                if (t == null) continue;
                SetHideFlagsRecursively(t.gameObject, flags);
            }
        }

        private static int GetPreviewLayer()
        {
            var named = LayerMask.NameToLayer("ConstrainTA_Preview");
            if (named >= 0) return named;
            return 30;
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            if (root == null) return;
            root.layer = layer;
            foreach (Transform t in root.transform)
            {
                if (t == null) continue;
                SetLayerRecursively(t.gameObject, layer);
            }
        }

        private static string VectorToShort(Vector3 v)
        {
            return $"({v.x:0.###}, {v.y:0.###}, {v.z:0.###})";
        }

        private sealed class PreviewEntry : IDisposable
        {
            public readonly GameObject SourceRoot;
            public readonly GameObject PreviewRoot;
            public readonly Scene PreviewScene;
            public readonly Camera Camera;
            public readonly Light Light;
            public RenderTexture RenderTexture;
            public Bounds Bounds;
            public int LastWidth;
            public int LastHeight;
            public float Yaw;
            public float Pitch;
            public float Distance;
            public float MinDistance;
            public Vector3 Focus;
            public float BaseMoveSpeed;
            public bool IsRmbLook;
            public Vector3 CameraPosition;
            public Quaternion CameraRotation;
            public int ControlId;
            public Vector2 LastMousePosition;
            public double LastUpdateTime;
            public bool KeyW;
            public bool KeyA;
            public bool KeyS;
            public bool KeyD;
            public bool KeyQ;
            public bool KeyE;
            public bool KeyShift;
            public float ForwardSign = 1f;

            public bool IsValid => PreviewScene.IsValid() && PreviewRoot != null && Camera != null;

            public PreviewEntry(GameObject sourceRoot, GameObject previewRoot, Scene previewScene, Camera camera, Light light, Bounds bounds)
            {
                SourceRoot = sourceRoot;
                PreviewRoot = previewRoot;
                PreviewScene = previewScene;
                Camera = camera;
                Light = light;
                Bounds = bounds;

                var radius = Mathf.Max(0.25f, bounds.extents.magnitude);
                Focus = bounds.center;
                Distance = radius * 2.3f;
                MinDistance = radius * 0.35f;
                Yaw = 0f;
                Pitch = 5f;
                BaseMoveSpeed = Mathf.Max(0.2f, radius * 0.9f);
                var rotation = Quaternion.Euler(Pitch, Yaw, 0f);
                CameraPosition = Focus + rotation * new Vector3(0f, 0f, -Distance);
                CameraRotation = rotation;
            }

            public void Dispose()
            {
                if (RenderTexture != null)
                {
                    RenderTexture.Release();
                    DestroyImmediate(RenderTexture);
                    RenderTexture = null;
                }

                if (Camera != null) DestroyImmediate(Camera.gameObject);
                if (Light != null) DestroyImmediate(Light.gameObject);
                if (PreviewRoot != null) DestroyImmediate(PreviewRoot);

                if (PreviewScene.IsValid())
                {
                    EditorSceneManager.ClosePreviewScene(PreviewScene);
                }
            }
        }

        private readonly struct ArmatureCandidate
        {
            public readonly Animator Animator;
            public readonly Transform ArmatureRoot;
            public readonly int Score;
            public readonly string Label;

            public ArmatureCandidate(Animator animator, Transform armatureRoot, int score, string label)
            {
                Animator = animator;
                ArmatureRoot = armatureRoot;
                Score = score;
                Label = label;
            }
        }

        // ビルド時の警告: 複数のヒューマノイドアーマチュア候補が存在し、選択した候補が宛先ルートに非常に近い（浅い深さ）場合、
        // それは衣装内のアーマチュアではなくアバター側のアーマチュアである可能性が高いです。
        // ユーザーがビルドをキャンセルした場合は false を返します。
        private static bool MaybeWarnAvatarArmatureSelectionOnBuild(GameObject destinationRoot, List<ArmatureCandidate> candidates, ref int selectedIndex)
        {
            if (destinationRoot == null || candidates == null || candidates.Count < 2) return true;
            if (selectedIndex < 0 || selectedIndex >= candidates.Count) return true;

            var rootT = destinationRoot.transform;
            var shallowestIndex = 0;
            var deepestIndex = 0;
            var minDepth = int.MaxValue;
            var maxDepth = int.MinValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                var d = GetDepthRelative(rootT, candidates[i].ArmatureRoot);
                if (d < minDepth) { minDepth = d; shallowestIndex = i; }
                if (d > maxDepth) { maxDepth = d; deepestIndex = i; }
            }

            // 選択した候補が最も浅い（ルートに近い）場合のみ警告する。これは多くの場合「アバター側のアーマチュア」に該当する。
            if (selectedIndex != shallowestIndex) return true;

            var selected = candidates[selectedIndex];
            var deepest = candidates[deepestIndex];
            var msg =
                "選択したArmatureが『アバター側のarmature』で、衣装内armatureではない可能性があります。\n" +
                "このままビルドすると Sources/Target がアバターarmatureへ張り替わる可能性があります。\n\n" +
                $"配置先: {destinationRoot.name}\n" +
                $"選択中: {selected.ArmatureRoot.name} ({selected.Label})\n" +
                $"衣装側っぽい候補(最深): {deepest.ArmatureRoot.name} ({deepest.Label})\n\n" +
                "どうしますか？";

            var r = EditorUtility.DisplayDialogComplex(
                "ConstrainTA 警告 (ビルド)",
                msg,
                "覚悟を決める(このままビルドする)",
                "衣装側っぽい候補に切替",
                "キャンセル");

            if (r == 2) return false;
            if (r == 1) selectedIndex = deepestIndex;
            return true;
        }

        private static int GetDepthRelative(Transform root, Transform t)
        {
            if (root == null || t == null) return int.MaxValue;
            if (t == root) return 0;
            if (!t.IsChildOf(root)) return int.MaxValue;
            var depth = 0;
            var cur = t;
            while (cur != null && cur != root)
            {
                depth++;
                cur = cur.parent;
            }
            return depth;
        }

        private void EnsureOutfitTargetsList()
        {
            outfitRoots ??= new List<GameObject>();
            selectedArmatureIndices ??= new List<int>();

            while (selectedArmatureIndices.Count < outfitRoots.Count) selectedArmatureIndices.Add(0);
            while (selectedArmatureIndices.Count > outfitRoots.Count) selectedArmatureIndices.RemoveAt(selectedArmatureIndices.Count - 1);

            if (outfitRoots.Count == 0)
            {
                outfitRoots.Add(null);
                selectedArmatureIndices.Add(0);
                return;
            }

            // 末尾に空のスロットを1つだけ保持する。
            var lastNonNull = -1;
            for (int i = outfitRoots.Count - 1; i >= 0; i--)
            {
                if (outfitRoots[i] != null) { lastNonNull = i; break; }
            }
            var desiredCount = Mathf.Max(1, lastNonNull + 2);
            while (outfitRoots.Count < desiredCount)
            {
                outfitRoots.Add(null);
                selectedArmatureIndices.Add(0);
            }
            while (outfitRoots.Count > desiredCount)
            {
                outfitRoots.RemoveAt(outfitRoots.Count - 1);
                selectedArmatureIndices.RemoveAt(selectedArmatureIndices.Count - 1);
            }
        }

        private void RemoveOutfitTargetAt(int index)
        {
            if (index < 0 || index >= outfitRoots.Count) return;
            var root = outfitRoots[index];
            if (root != null) armatureCandidatesCache.Remove(root.GetInstanceID());
            outfitRoots.RemoveAt(index);
            if (index < selectedArmatureIndices.Count) selectedArmatureIndices.RemoveAt(index);
        }

        private List<ArmatureCandidate> GetArmatureCandidates(GameObject root)
        {
            if (root == null) return null;
            var id = root.GetInstanceID();
            if (!armatureCandidatesCache.TryGetValue(id, out var cached) || cached == null)
            {
                cached = FindHumanoidArmatureCandidates(root);
                armatureCandidatesCache[id] = cached;
            }
            return cached;
        }

        private void RefreshArmatureCandidates(GameObject root)
        {
            if (root == null) return;
            armatureCandidatesCache[root.GetInstanceID()] = FindHumanoidArmatureCandidates(root);
        }

        private GameObject GetFirstOutfitRootOrNull()
        {
            return outfitRoots?.FirstOrDefault(r => r != null);
        }

        private static List<ArmatureCandidate> FindHumanoidArmatureCandidates(GameObject root)
        {
            var result = new List<ArmatureCandidate>();
            if (root == null) return result;

            var animators = root.GetComponentsInChildren<Animator>(true);
            if (animators == null || animators.Length == 0) return result;

            foreach (var a in animators)
            {
                if (a == null) continue;

                // Animator が有効なヒューマノイドアバターを持つ場合、ヒューマノイドマッピング経由でのボーン検出を優先する。
                if (a.avatar != null && a.avatar.isHuman)
                {
                    var bones = GetHumanoidBones(a);
                    if (bones.Count == 0) continue;

                    var armatureRoot = FindLowestCommonAncestor(bones, root.transform);
                    if (armatureRoot == null) continue;

                    var score = CountCoreHumanoidBones(a);
                    // Animator 名を露出しないよう、ラベルにはパスのみを使用する。
                    var label = $"{GetPathRelative(root.transform, armatureRoot)}";
                    result.Add(new ArmatureCandidate(a, armatureRoot, score, label));
                }
                else
                {
                    // フォールバック: Animator は存在するがヒューマノイドとしてインポートされていない（またはアバターがない）場合。
                    // Animator の Transform をアーマチュア候補として扱い、既存のソース解決（フルパス→名前検索）が動作するようにする。
                    var armatureRoot = a.transform;
                    if (armatureRoot == null) continue;
                    var score = 0; // 未知のヒューマノイドスコア
                    // フォールバックラベルは最小限（パスのみ）にする。
                    var label = $"{GetPathRelative(root.transform, armatureRoot)}";
                    result.Add(new ArmatureCandidate(a, armatureRoot, score, label));
                }
            }

            // アーマチュアルートで重複を排除し、最もスコアの高い候補を残す。
            var bestByRoot = new Dictionary<Transform, ArmatureCandidate>();
            foreach (var c in result)
            {
                if (!bestByRoot.TryGetValue(c.ArmatureRoot, out var existing) || c.Score > existing.Score)
                    bestByRoot[c.ArmatureRoot] = c;
            }

            var deduped = bestByRoot.Values.ToList();
            deduped.Sort((x, y) => y.Score.CompareTo(x.Score));
            return deduped;
        }

        private static int CountCoreHumanoidBones(Animator a)
        {
            if (a == null || a.avatar == null || !a.avatar.isHuman) return 0;
            var core = new[]
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
            foreach (var b in core)
            {
                try { if (a.GetBoneTransform(b) != null) score++; } catch { }
            }
            return score;
        }

        private static List<Transform> GetHumanoidBones(Animator a)
        {
            var result = new List<Transform>();
            if (a == null || a.avatar == null || !a.avatar.isHuman) return result;

            var broad = new[]
            {
                HumanBodyBones.Hips,
                HumanBodyBones.Spine,
                HumanBodyBones.Chest,
                HumanBodyBones.UpperChest,
                HumanBodyBones.Neck,
                HumanBodyBones.Head,
                HumanBodyBones.LeftUpperArm,
                HumanBodyBones.LeftLowerArm,
                HumanBodyBones.LeftHand,
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

            foreach (var b in broad)
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

            var minLen = int.MaxValue;
            foreach (var p in paths) minLen = Mathf.Min(minLen, p.Count);

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

        private static string GetPathRelative(Transform root, Transform t)
        {
            if (t == null) return "(null)";
            if (root == null) return t.name;
            // パス整形は一貫性のため PathUtils に委譲する。ルート名があればそれを含める。
            return PathUtils.GetRelativePath(root, t, includeSelf: true, includeRoot: true);
        }

        /* UNUSED: DrawImportedCountsSection
         * This helper previously displayed the imported counts as a separate
         * section. The UI now shows counts inline under the Import headline
         * and the standalone section is no longer referenced. Keep the
         * implementation here commented out for historical/reference purposes.
         */
        /*
        private void DrawImportedCountsSection()
        {
            if (imported == null || imported.Count == 0) return;
            showImportedCounts = EditorGUILayout.Foldout(showImportedCounts, "インポート内容（種類別カウント）", true);
            if (!showImportedCounts) return;
            DrawCounts();
        }
        */

#if CONSTRAINTA_DIAGNOSTICS
        private void DrawDiagnosticsSection()
        {
            EditorGUILayout.LabelField("3) 診断（必要なときだけ）", EditorStyles.boldLabel);

            showDiagnostics = EditorGUILayout.Foldout(showDiagnostics, "診断ツールを表示", true);
            if (!showDiagnostics) return;

            debugSkipHumanoidLookup = EditorGUILayout.ToggleLeft("[デバッグ] Humanoid一致探索をスキップ", debugSkipHumanoidLookup);
            debugSkipFullPathLookup = EditorGUILayout.ToggleLeft("[デバッグ] フルパス一致をスキップ", debugSkipFullPathLookup);
            if (debugSkipHumanoidLookup || debugSkipFullPathLookup)
            {
                EditorGUILayout.HelpBox("デバッグ: 探索スキップON。未解決のSourceが増えます。", MessageType.Warning);
            }

            var diagnosticsRoot = GetFirstOutfitRootOrNull();
            using (new EditorGUI.DisabledScope(diagnosticsRoot == null))
            {
                EditorGUILayout.HelpBox("推奨: (1)全部OFF → (2)Activate → (3)全部ON → (4)参照ログ（必要なら）", MessageType.None);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("(1) 全OFF"))
                {
                    var changed = ConstraintDiagnostics.SetAllConstraintsActive(diagnosticsRoot, false);
                    SetLastAction($"Set IsActive OFF: {changed}");
                }
                if (GUILayout.Button("(3) 全ON"))
                {
                    var changed = ConstraintDiagnostics.SetAllConstraintsActive(diagnosticsRoot, true);
                    SetLastAction($"Set IsActive ON: {changed}");
                }
                EditorGUILayout.EndHorizontal();

                if (GUILayout.Button("(2) Activate（Inspectorと同じ）"))
                {
                    var activated = ConstraintDiagnostics.ActivateAllConstraints(diagnosticsRoot);
                    var r = ConstraintDiagnostics.LastActivateReport;
                    SetLastAction($"Activate: {r.Succeeded}/{r.Total} (failed={r.Failed}{(string.IsNullOrEmpty(r.FailedTypesSummary) ? "" : $" {r.FailedTypesSummary}")})");
                }

                if (GUILayout.Button("(4) 参照ログをConsoleに出す"))
                {
                    ConstraintDiagnostics.LogConstraintBindings(diagnosticsRoot);
                    SetLastAction("Logged bindings to Console");
                }
            }
        }

        private void DrawLastAction()
        {
            if (string.IsNullOrEmpty(lastActionMessage)) return;
            var age = EditorApplication.timeSinceStartup - lastActionTime;
            EditorGUILayout.HelpBox($"Last: {lastActionMessage}  ({age:0.0}s ago)", MessageType.None);
        }

        private void SetLastAction(string message)
        {
            lastActionMessage = message;
            lastActionTime = EditorApplication.timeSinceStartup;
            Repaint();
        }
#endif

        private void DrawCounts()
        {
            var known = new[] { "VRCAimConstraint", "VRCLookAtConstraint", "VRCParentConstraint", "VRCPositionConstraint", "VRCRotationConstraint", "VRCScaleConstraint" };

            var counts = new Dictionary<string, int>();
            foreach (var k in known) counts[k] = 0;
            counts["Other"] = 0;

            if (imported != null)
            {
                foreach (var d in imported)
                {
                    var name = string.IsNullOrEmpty(d.constraintType) ? string.Empty : SimplifyTypeName(d.constraintType);
                    if (counts.ContainsKey(name)) counts[name]++;
                    else counts["Other"]++;
                }
            }

            foreach (var k in known)
            {
                EditorGUILayout.LabelField($"{k}: {counts[k]}");
            }
            EditorGUILayout.LabelField($"Other: {counts["Other"]}");
        }

        private string SimplifyTypeName(string assemblyQualifiedName)
        {
            if (string.IsNullOrEmpty(assemblyQualifiedName)) return "(unknown)";
            var comma = assemblyQualifiedName.IndexOf(',');
            var typeName = comma >= 0 ? assemblyQualifiedName.Substring(0, comma) : assemblyQualifiedName;
            var lastDot = typeName.LastIndexOf('.');
            return lastDot >= 0 ? typeName.Substring(lastDot + 1) : typeName;
        }
    }
}
