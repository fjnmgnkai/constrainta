using NUnit.Framework;
using UnityEngine;
using ConstrainTA.Editor.Backend;

namespace Com.Github.Fjnmgnkai.Constrainta.Tests.Editor
{
    public class PathUtilsTests
    {
        [Test]
        public void NormalizeName_TrimsTrailingDots()
        {
            Assert.AreEqual("abc", PathUtils.NormalizeName("abc."));
            Assert.AreEqual(string.Empty, PathUtils.NormalizeName(null));
        }

        [Test]
        public void NameEquals_IsCaseInsensitiveAndNormalized()
        {
            Assert.IsTrue(PathUtils.NameEquals("Bone.Name", "bone.name"));
            Assert.IsFalse(PathUtils.NameEquals("a", "b"));
        }

        [Test]
        public void SplitAndParentSegments_Basic()
        {
            var segs = PathUtils.SplitPathSegments("a/b/c");
            Assert.AreEqual(3, segs.Count);
            var parents = PathUtils.GetParentSegments(segs);
            Assert.AreEqual(2, parents.Count);
            Assert.AreEqual("a", parents[0]);
        }

        [Test]
        public void StripLeading_RemovesRootIfMatches()
        {
            var segs = PathUtils.SplitPathSegments("root/child/grand");
            var stripped = PathUtils.StripLeading(segs, "root");
            Assert.AreEqual(2, stripped.Count);
            Assert.AreEqual("child", stripped[0]);
        }

        [Test]
        public void GetRelativePath_IncludesRootAndSelfOptions()
        {
            var rootGO = new GameObject("root");
            var child = new GameObject("child");
            child.transform.SetParent(rootGO.transform, false);

            var p = PathUtils.GetRelativePath(rootGO.transform, child.transform, includeSelf: true, includeRoot: true);
            Assert.AreEqual("root/child", p);

            Object.DestroyImmediate(rootGO);
        }
    }
}
