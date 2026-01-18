using NUnit.Framework;
using UnityEngine;
using ConstrainTA.Editor.Backend;

namespace Com.Github.Fjnmgnkai.Constrainta.Tests.Editor
{
    public class BoneMapsTests
    {
        [Test]
        public void NormalizeKey_RemovesPunctuationAndSpaces()
        {
            var n = BoneMaps.NormalizeKey(" Upper_Chest . ");
            Assert.AreEqual("upperchest", n);
        }

        [Test]
        public void TryNormalizeBoneKey_ParsesCommonForms()
        {
            var key = BoneMaps.TryNormalizeBoneKey("LeftUpperArm");
            Assert.AreEqual("left_arm", key);
        }

        [Test]
        public void TryGetCanonicalAndBone_ReturnsKnownMappings()
        {
            Assert.IsTrue(BoneMaps.TryGetCanonical("Hips", out var canonical));
            Assert.AreEqual("hips", canonical);

            Assert.IsTrue(BoneMaps.TryGetBone("hips", out var bone));
            Assert.AreEqual(HumanBodyBones.Hips, bone);
        }
    }
}
