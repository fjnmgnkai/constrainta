/// －－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－ーーーーーーーーーーーー
/// <summary>
/// 概要 : Constraint 情報をシリアライズ可能な形で格納するクラス定義。
/// </summary>
/// <remarks>
/// 詳細 : Empty 名、親パス、元の Constraint の数値バックアップ(JSON)、
///        Sources の名前リストと target 名を保持する。Import 時に生成され、Build 側で再利用される。
/// 依存関係 : VRCRotationConstraint を一時参照として保持するフィールドがあるが、シリアライズされない。
/// 最終更新 : 2026-01-03
/// </remarks>
/// －－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－－ーーーーーーーーーーーー
/// 
using System;
using System.Collections.Generic;
using VRC.SDK3.Dynamics.Constraint.Components;

namespace ConstrainTA.Editor.Backend
{
    [Serializable]
    public class ConstraintData
    {
        // Empty 名・階層
        public string emptyName; // Empty (GameObject) の名前
        public string parentName; // importRoot からの相対親パス（スラッシュ区切り）
        public string constraintPathFromArmature; // armatureRoot からの相対パス（self を含む）
        public string armatureRootName; // 参照元アーマチュアのルート名（推定値）
        public string armatureRootPath; // importRoot から armatureRoot までの相対パス
        public string constraintType; // コンポーネント型（AssemblyQualifiedName）

        // 元 Constraint（衣装①）
        [NonSerialized]
        public VRCRotationConstraint sourceConstraint; // 元の Constraint (ランタイム的に参照するがシリアライズしない)

        // 数値系バックアップ（保険）
        public string constraintJson; // RestoreNumeric で使う数値バックアップ(JSON)

        [Serializable]
        public class SourceName
        {
            public string name;
            public float weight;
            public string pathFromArmature; // armatureRoot からの相対パス（重複名対策）
        }

        public List<SourceName> sources = new(); // Source 名（Transform 名）と weight の一覧
        public string targetName; // ターゲット Transform の名前（存在しない場合は空文字）
        public string targetPathFromArmature; // armatureRoot からターゲットへの相対パス
    }
}
