using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Com.Github.Fjnmgnkai.Constrainta.Tests.Editor
{
    public class ConstraintApplierTests
    {
        [Test]
        public void ResolveOrAddConstraintComponent_AddsKnownComponent()
        {
            var go = new GameObject("TestGO");

            // Find internal ConstraintApplier type via reflection
            Type applierType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                applierType = asm.GetType("ConstrainTA.Editor.Backend.ConstraintApplier", throwOnError: false);
                if (applierType != null) break;
            }

            Assert.IsNotNull(applierType, "ConstraintApplier type not found");

            var method = applierType.GetMethod("ResolveOrAddConstraintComponent", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(method, "ResolveOrAddConstraintComponent not found");

            // Use a common Unity component type name; method will search loaded assemblies
            var res = method.Invoke(null, new object[] { go, "UnityEngine.BoxCollider" }) as Component;
            Assert.IsNotNull(res);
            Assert.IsTrue(res is BoxCollider);

            UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
