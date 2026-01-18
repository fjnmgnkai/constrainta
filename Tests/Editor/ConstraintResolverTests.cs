using NUnit.Framework;
using UnityEngine;
using ConstrainTA.Editor.Backend;

namespace Com.Github.Fjnmgnkai.Constrainta.Tests.Editor
{
    public class ConstraintResolverTests
    {
        [Test]
        public void ResolveTransformForPreview_ByFullPathAndName()
        {
            var outfit = new GameObject("OutfitRoot");
            var arm = new GameObject("ArmatureRoot");
            arm.transform.SetParent(outfit.transform, false);

            var a = new GameObject("A"); a.transform.SetParent(arm.transform, false);
            var b = new GameObject("B"); b.transform.SetParent(a.transform, false);
            var target = new GameObject("TargetBone"); target.transform.SetParent(b.transform, false);

            // full path from armature
            var path = PathUtils.GetRelativePath(arm.transform, target.transform, includeSelf: true, includeRoot: false);
            var resolved = ConstraintBuilder.ResolveTransformForPreview(outfit.transform, arm.transform, path, parentPath: string.Empty, name: string.Empty);
            Assert.IsNotNull(resolved);
            Assert.AreEqual(target.name, resolved.name);

            // resolve by name
            var byName = ConstraintBuilder.ResolveTransformForPreview(outfit.transform, arm.transform, string.Empty, parentPath: string.Empty, name: "TargetBone");
            Assert.IsNotNull(byName);
            Assert.AreEqual(target.name, byName.name);

            Object.DestroyImmediate(outfit);
        }
    }
}
