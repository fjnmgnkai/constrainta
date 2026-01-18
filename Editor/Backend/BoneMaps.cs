using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace ConstrainTA.Editor.Backend
{
	public static class BoneMaps
	{
		// 内部で可変な辞書。外部からの直接変更を避けるため private に保持する。
		private static readonly Dictionary<string, string> _aliasToCanonical = BuildAliasMap();
		private static readonly Dictionary<string, HumanBodyBones> _canonicalToBone = BuildCanonicalMap();

		// 公開は読み取り専用アクセサ経由で行う。
		public static System.Collections.Generic.IReadOnlyDictionary<string, string> AliasToCanonical => _aliasToCanonical;
		public static System.Collections.Generic.IReadOnlyDictionary<string, HumanBodyBones> CanonicalToBone => _canonicalToBone;

		// 呼び出し側が具体的な辞書 API に依存しないようセーフな検索ヘルパーを提供する。
		public static bool TryGetCanonical(string aliasOrCanonical, out string canonical)
		{
			if (string.IsNullOrEmpty(aliasOrCanonical)) { canonical = null; return false; }
			return _aliasToCanonical.TryGetValue(NormalizeKey(aliasOrCanonical), out canonical);
		}

		public static bool TryGetBone(string canonicalKey, out HumanBodyBones bone)
		{
			if (string.IsNullOrEmpty(canonicalKey)) { bone = default; return false; }
			return _canonicalToBone.TryGetValue(NormalizeKey(canonicalKey), out bone);
		}

		private static Dictionary<string, HumanBodyBones> BuildCanonicalMap()
		{
			var map = new Dictionary<string, HumanBodyBones>(StringComparer.OrdinalIgnoreCase);
			void Add(string canonicalKey, HumanBodyBones bone) => map[NormalizeKey(canonicalKey)] = bone;

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
				var key = NormalizeKey(canonical);
				foreach (var a in aliases)
				{
					var k = NormalizeKey(a);
					if (!map.ContainsKey(k)) map[k] = key;
				}
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
			Add("left_eye_end", "Eye.L_end", "eye.L_end", "LeftEye_end");
			Add("right_eye_end", "Eye.R_end", "eye.R_end", "RightEye_end");
			Add("left_toe_end", "Toes.L_end", "Toe.L_end", "Toes_END");
			Add("right_toe_end", "Toes.R_end", "Toe.R_end", "Toes_END.001");

			return map;
		}

		public static string NormalizeKey(string key)
		{
			if (string.IsNullOrEmpty(key)) return string.Empty;
			var lowered = key.Trim().ToLowerInvariant();
			lowered = lowered.Replace(" ", string.Empty)
							 .Replace("_", string.Empty)
							 .Replace(".", string.Empty);
			return lowered;
		}

		public static string TryNormalizeBoneKey(string raw)
		{
			var tokens = TokenizeName(raw);
			if (tokens.Count == 0) return null;

			var side = ExtractSideToken(tokens);
			var segment = ExtractSegmentToken(tokens);

			var baseName = DetectBaseToken(tokens);
			if (string.IsNullOrEmpty(baseName)) return null;

			var key = string.IsNullOrEmpty(side) ? baseName : $"{side}_{baseName}";
			if (!string.IsNullOrEmpty(segment)) key = $"{key}_{segment}";
			return key;
		}

		private static List<string> TokenizeName(string raw)
		{
			var result = new List<string>();
			if (string.IsNullOrEmpty(raw)) return result;

			var s = raw.Replace("_", " ").Replace(".", " ").Replace("-", " ");
			var sb = new StringBuilder(s.Length + 8);
			char prev = '\0';
			foreach (var c in s)
			{
				if (char.IsWhiteSpace(c))
				{
					sb.Append(' ');
					prev = c;
					continue;
				}

				var boundary = prev != '\0' &&
							   ((char.IsLower(prev) && char.IsUpper(c)) ||
								(char.IsLetter(prev) && char.IsDigit(c)) ||
								(char.IsDigit(prev) && char.IsLetter(c)));
				if (boundary) sb.Append(' ');
				sb.Append(c);
				prev = c;
			}

			var parts = sb.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
			foreach (var p in parts)
			{
				if (string.IsNullOrEmpty(p)) continue;
				var lower = p.ToLowerInvariant();

				if (lower == "upperarm") { result.Add("upper"); result.Add("arm"); continue; }
				if (lower == "lowerarm") { result.Add("lower"); result.Add("arm"); continue; }
				if (lower == "upperleg") { result.Add("upper"); result.Add("leg"); continue; }
				if (lower == "lowerleg") { result.Add("lower"); result.Add("leg"); continue; }
				if (lower == "upperchest") { result.Add("upper"); result.Add("chest"); continue; }
				if (lower == "forearm") { result.Add("forearm"); continue; }
				if (lower == "thigh") { result.Add("thigh"); continue; }
				if (lower == "calf") { result.Add("calf"); continue; }

				result.Add(lower);
			}

			return result;
		}

		private static string ExtractSideToken(List<string> tokens)
		{
			if (tokens == null || tokens.Count == 0) return null;
			for (int i = tokens.Count - 1; i >= 0; i--)
			{
				var t = tokens[i];
				if (t == "left" || t == "l") { tokens.RemoveAt(i); return "left"; }
				if (t == "right" || t == "r") { tokens.RemoveAt(i); return "right"; }
			}
			return null;
		}

		private static string ExtractSegmentToken(List<string> tokens)
		{
			if (tokens == null || tokens.Count == 0) return null;

			for (int i = tokens.Count - 1; i >= 0; i--)
			{
				var t = NormalizeToken(tokens[i]);
				if (t == "1") { tokens.RemoveAt(i); return "proximal"; }
				if (t == "2") { tokens.RemoveAt(i); return "intermediate"; }
				if (t == "3") { tokens.RemoveAt(i); return "distal"; }
				if (t == "proximal" || t == "prox") { tokens.RemoveAt(i); return "proximal"; }
				if (t == "intermediate" || t == "inter") { tokens.RemoveAt(i); return "intermediate"; }
				if (t == "distal") { tokens.RemoveAt(i); return "distal"; }
				if (t == "end" || t == "tip") { tokens.RemoveAt(i); return "end"; }
			}
			return null;
		}

		private static string NormalizeToken(string token)
		{
			if (string.IsNullOrEmpty(token)) return token;
			switch (token)
			{
				case "sholder": return "shoulder";
				case "toes": return "toe";
				case "fingers": return "finger";
				case "proxima": return "proximal";
				case "little": return "pinky";
				default: return token;
			}
		}

		private static string DetectBaseToken(List<string> tokens)
		{
			if (tokens == null || tokens.Count == 0) return null;
			var set = new HashSet<string>(tokens.Select(NormalizeToken));

			if (set.Contains("hips")) return "hips";
			if (set.Contains("spine")) return "spine";
			if (set.Contains("upper") && set.Contains("chest")) return "upper_chest";
			if (set.Contains("chest")) return "chest";
			if (set.Contains("neck")) return "neck";
			if (set.Contains("head")) return "head";
			if (set.Contains("eye")) return "eye";
			if (set.Contains("shoulder") || set.Contains("sholder")) return "shoulder";

			if ((set.Contains("upper") && set.Contains("arm")) || set.Contains("upperarm")) return "arm";
			if ((set.Contains("lower") && set.Contains("arm")) || set.Contains("forearm") || set.Contains("lowerarm")) return "forearm";
			if (set.Contains("hand")) return "hand";

			if (set.Contains("thumb")) return "thumb";
			if (set.Contains("index")) return "index";
			if (set.Contains("middle")) return "middle";
			if (set.Contains("ring")) return "ring";
			if (set.Contains("pinky") || set.Contains("little")) return "pinky";

			if ((set.Contains("upper") && set.Contains("leg")) || set.Contains("thigh") || set.Contains("upperleg")) return "thigh";
			if ((set.Contains("lower") && set.Contains("leg")) || set.Contains("calf") || set.Contains("lowerleg")) return "calf";
			if (set.Contains("foot")) return "foot";
			if (set.Contains("toe")) return "toe";

			return null;
		}
	}
}