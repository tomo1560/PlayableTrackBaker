using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace PlayableTrackBaking.Tests
{
    [TestFixture]
    public sealed class SignalEventTestSetupOwnershipTests
    {
        const string SetupTypeName = "PlayableTrackBaking.Tests.SignalEventTestSetup, Assembly-CSharp-Editor";
        const string OwnershipSignature = "PlayableTrackBaker.SignalEventTestSetup/v1:3f84fe19";
        const string OwnershipMarkerName = "__PlayableTrackBaker_SignalEventTestSetup_3f84fe19__";

        [TestCase(null, false)]
        [TestCase("", false)]
        [TestCase(OwnershipSignature, true)]
        [TestCase(OwnershipSignature + "-tampered", false)]
        public void HasOwnershipSignature_RequiresExactSignature(string signature, bool expected)
        {
            AssertOwnershipMethod("HasOwnershipSignature", signature, expected);
        }

        [TestCase(null, false)]
        [TestCase("SignalEventTest_Root", false)]
        [TestCase(OwnershipMarkerName, true)]
        [TestCase(OwnershipMarkerName + "copy", false)]
        public void HasOwnershipMarkerName_RequiresExactMarker(string markerName, bool expected)
        {
            AssertOwnershipMethod("HasOwnershipMarkerName", markerName, expected);
        }

        [Test]
        public void IsOwnedSceneRoot_RequiresMarkerChild()
        {
            var root = new GameObject("SignalEventTest_Root");
            try
            {
                AssertOwnershipMethod("IsOwnedSceneRoot", root, false);
                var marker = new GameObject(OwnershipMarkerName);
                marker.transform.SetParent(root.transform, false);
                AssertOwnershipMethod("IsOwnedSceneRoot", root, true);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void IsOwnedAsset_RequiresImporterSignature()
        {
            string testAssetPath = $"Assets/__SignalEventSetupOwnershipTest_{GUID.Generate()}.asset";
            var asset = ScriptableObject.CreateInstance<SignalAsset>();
            AssetDatabase.CreateAsset(asset, testAssetPath);
            try
            {
                AssertOwnershipMethod("IsOwnedAsset", testAssetPath, false);
                var importer = AssetImporter.GetAtPath(testAssetPath);
                importer.userData = OwnershipSignature;
                importer.SaveAndReimport();
                AssertOwnershipMethod("IsOwnedAsset", testAssetPath, true);
            }
            finally
            {
                AssetDatabase.DeleteAsset(testAssetPath);
            }
        }

        static void AssertOwnershipMethod(string methodName, string value, bool expected)
            => AssertOwnershipMethod(methodName, (object)value, expected);

        static void AssertOwnershipMethod(string methodName, object value, bool expected)
        {
            var setupType = Type.GetType(SetupTypeName, throwOnError: false);
            if (setupType == null)
                Assert.Ignore("SignalEventTestSetup is only present in the repository's VerificationProject.");

            var method = setupType.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            Assert.That(method.Invoke(null, new object[] { value }), Is.EqualTo(expected));
        }
    }
}
