using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;

namespace ConstrainTA.Editor.Backend
{
    internal static class ConstraintNormalizer
    {
        public static bool NormalizeConstraint(Component constraint)
        {
            if (constraint == null) return false;

            var constraintBase = constraint as VRCConstraintBase;
            if (constraintBase == null) return false;

            var target = GetTargetTransform(constraint);
            if (target == null) return false;

            var typeName = constraint.GetType().Name;
            if (string.Equals(typeName, nameof(VRCPositionConstraint), StringComparison.OrdinalIgnoreCase))
            {
                return NormalizePositionConstraint(constraint, constraintBase, target);
            }

            if (string.Equals(typeName, nameof(VRCRotationConstraint), StringComparison.OrdinalIgnoreCase))
            {
                return NormalizeRotationConstraint(constraint, constraintBase, target);
            }

            if (string.Equals(typeName, nameof(VRCParentConstraint), StringComparison.OrdinalIgnoreCase))
            {
                return NormalizeParentConstraint(constraintBase, target);
            }

            return false;
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

            for (int i = 0; i < sources.Count; i++)
            {
                var s = sources[i];
                var st = s.SourceTransform;
                if (st == null) continue;

                var posOffset = st.InverseTransformPoint(target.position);
                var rotOffsetQ = Quaternion.Inverse(st.rotation) * target.rotation;
                var rotOffsetEuler = rotOffsetQ.eulerAngles;

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
    }
}
