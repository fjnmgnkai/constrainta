using System;
using UnityEngine;
using UnityEditor;
using VRC.Dynamics;

namespace ConstrainTA.Editor.Backend
{
    internal static class SdkUtils
    {
        public static bool TryInvokeInspectorActivate(Component constraint)
        {
            if (constraint == null) return false;

            try
            {
                if (constraint is VRCConstraintBase cb)
                    TrySdkRefreshGroups(new[] { cb });
            }
            catch { }

            var editor = UnityEditor.Editor.CreateEditor(constraint);
            if (editor == null) return false;

            try
            {
                if (TryInvokeComponentActivate(constraint)) return true;

                var et = editor.GetType();
                var m = et.GetMethod(
                    "Activate",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                    null,
                    Type.EmptyTypes,
                    null);

                if (m == null)
                {
                    foreach (var cand in et.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic))
                    {
                        if (!cand.Name.Contains("Activate", StringComparison.OrdinalIgnoreCase)) continue;
                        if (cand.GetParameters().Length != 0) continue;
                        m = cand;
                        break;
                    }
                }

                if (m == null) return false;
                m.Invoke(editor, null);
                return true;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(editor);
            }
        }

        public static bool TryInvokeComponentActivate(Component constraint)
        {
            if (constraint == null) return false;
            var t = constraint.GetType();
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance;

            var exactNames = new[]
            {
                "Activate",
                "Sdk_Activate",
                "ActivateConstraint",
                "Rebuild",
                "RebuildConstraint",
                "Refresh",
                "Reinitialize",
            };

            foreach (var name in exactNames)
            {
                var m = t.GetMethod(name, flags, null, Type.EmptyTypes, null);
                if (m == null) continue;
                try
                {
                    m.Invoke(constraint, null);
                    return true;
                }
                catch { }
            }

            foreach (var cand in t.GetMethods(flags))
            {
                if (!cand.Name.Contains("Activate", StringComparison.OrdinalIgnoreCase)) continue;
                if (cand.GetParameters().Length != 0) continue;
                try
                {
                    cand.Invoke(constraint, null);
                    return true;
                }
                catch { }
            }

            return false;
        }

        public static void TrySdkRefreshGroups(VRCConstraintBase[] constraints)
        {
            if (constraints == null) return;
            try
            {
                var t = FindType("VRC.Dynamics.VRCConstraintManager");
                if (t == null) return;
                var m = t.GetMethod(
                    "Sdk_ManuallyRefreshGroups",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (m == null) return;
                m.Invoke(null, new object[] { constraints });
            }
            catch { }
        }

        private static Type FindType(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType(fullName, throwOnError: false);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }

        public static bool GetConstraintIsActive(Component constraint)
        {
            if (constraint == null) return false;
            var type = constraint.GetType();

            var prop = type.GetProperty("IsActive", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (prop != null && prop.CanRead)
            {
                var v = prop.GetValue(constraint);
                if (v is bool b) return b;
            }

            var field = type.GetField("IsActive", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                        ?? type.GetField("m_IsActive", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field != null)
            {
                var v = field.GetValue(constraint);
                if (v is bool b) return b;
            }

            return false;
        }

        public static void SetConstraintIsActive(Component constraint, bool active)
        {
            if (constraint == null) return;
            var type = constraint.GetType();

            var prop = type.GetProperty("IsActive", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(constraint, active);
            }

            var field = type.GetField("IsActive", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                        ?? type.GetField("m_IsActive", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field != null)
            {
                field.SetValue(constraint, active);
            }
        }
    }
}
