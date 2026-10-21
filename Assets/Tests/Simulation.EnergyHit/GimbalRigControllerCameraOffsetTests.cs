using NUnit.Framework;
using UnityEngine;

namespace Simulation.EnergyHit.Tests
{
    /// <summary>
    /// Covers the camera-position contract that <c>EnergyHitDebuggerWindow.DrawCameraPositionControls</c>
    /// relies on: the sliders are built with the fixed ranges LEFT/RIGHT [-2, 2],
    /// BACK/FORWARD [-4, 4] and DOWN/UP [-1, 1], and every dragged value is pushed
    /// straight into <see cref="GimbalRigController.SetCameraPositionOffsets"/>.
    /// The component is created on an inactive GameObject so Awake (which requires a
    /// fully wired rig and logs an error) never runs.
    /// </summary>
    [TestFixture]
    public sealed class GimbalRigControllerCameraOffsetTests
    {
        private const float RightLimit = 2f;
        private const float ForwardLimit = 4f;
        private const float UpLimit = 1f;

        private GameObject _root;
        private GimbalTestAccess _gimbal;
        private Component _gimbalComponent;

        // Test asmdefs cannot reliably reference the predefined Assembly-CSharp
        // assembly. Match the reflection access used by the other HUD tests.
        private sealed class GimbalTestAccess
        {
            private readonly Component _component;
            public GimbalTestAccess(Component component) { _component = component; }
            private T Get<T>(string property) =>
                (T)_component.GetType().GetProperty(property).GetValue(_component);
            public float CameraOffsetRight => Get<float>("CameraOffsetRight");
            public float CameraOffsetForward => Get<float>("CameraOffsetForward");
            public float CameraOffsetUp => Get<float>("CameraOffsetUp");
            public bool MouseAimEnabled => Get<bool>("MouseAimEnabled");
            public void SetCameraPositionOffsets(float right, float forward, float up) =>
                _component.GetType().GetMethod("SetCameraPositionOffsets")
                    .Invoke(_component, new object[] { right, forward, up });
            public void ResetCameraPosition() =>
                _component.GetType().GetMethod("ResetCameraPosition").Invoke(_component, null);
        }

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("GimbalOffsetTestRoot");
            _root.SetActive(false);

            _gimbalComponent = _root.AddComponent(
                EnergyHitTestReflection.GetType("Simulation.EnergyHit.GimbalRigController"));
            _gimbal = new GimbalTestAccess(_gimbalComponent);
            EnergyHitTestReflection.SetInstanceField(_gimbalComponent, "_yawAxis", _root.transform);
            EnergyHitTestReflection.SetInstanceField(_gimbalComponent, "_pitchAxis", _root.transform);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_gimbalComponent);
            Object.DestroyImmediate(_root);
        }

        // ---------- Default state ----------

        [Test]
        public void CameraOffsets_StartAtZero()
        {
            Assert.AreEqual(0f, _gimbal.CameraOffsetRight);
            Assert.AreEqual(0f, _gimbal.CameraOffsetForward);
            Assert.AreEqual(0f, _gimbal.CameraOffsetUp);
        }

        [Test]
        public void MouseAimEnabled_DefaultsToTrue()
        {
            // The HUD prints "M: MOUSE AIM ON/OFF" from this flag.
            Assert.IsTrue(_gimbal.MouseAimEnabled);
        }

        // ---------- Values inside the slider ranges ----------

        [Test]
        public void SetCameraPositionOffsets_ValuesInsideRange_ArePreserved()
        {
            _gimbal.SetCameraPositionOffsets(1.5f, -3.25f, 0.75f);

            Assert.AreEqual(1.5f, _gimbal.CameraOffsetRight, 1e-6f);
            Assert.AreEqual(-3.25f, _gimbal.CameraOffsetForward, 1e-6f);
            Assert.AreEqual(0.75f, _gimbal.CameraOffsetUp, 1e-6f);
        }

        [Test]
        public void SetCameraPositionOffsets_ExactPositiveLimits_AreAllowed()
        {
            _gimbal.SetCameraPositionOffsets(RightLimit, ForwardLimit, UpLimit);

            Assert.AreEqual(RightLimit, _gimbal.CameraOffsetRight, 1e-6f);
            Assert.AreEqual(ForwardLimit, _gimbal.CameraOffsetForward, 1e-6f);
            Assert.AreEqual(UpLimit, _gimbal.CameraOffsetUp, 1e-6f);
        }

        [Test]
        public void SetCameraPositionOffsets_ExactNegativeLimits_AreAllowed()
        {
            _gimbal.SetCameraPositionOffsets(-RightLimit, -ForwardLimit, -UpLimit);

            Assert.AreEqual(-RightLimit, _gimbal.CameraOffsetRight, 1e-6f);
            Assert.AreEqual(-ForwardLimit, _gimbal.CameraOffsetForward, 1e-6f);
            Assert.AreEqual(-UpLimit, _gimbal.CameraOffsetUp, 1e-6f);
        }

        [Test]
        public void SetCameraPositionOffsets_LastCallWins()
        {
            _gimbal.SetCameraPositionOffsets(1f, 2f, 0.5f);
            _gimbal.SetCameraPositionOffsets(-1f, -2f, -0.5f);

            Assert.AreEqual(-1f, _gimbal.CameraOffsetRight, 1e-6f);
            Assert.AreEqual(-2f, _gimbal.CameraOffsetForward, 1e-6f);
            Assert.AreEqual(-0.5f, _gimbal.CameraOffsetUp, 1e-6f);
        }

        // ---------- Clamping of out-of-range values ----------

        [Test]
        public void SetCameraPositionOffsets_ClampsRightAboveLimit()
        {
            _gimbal.SetCameraPositionOffsets(99f, 0f, 0f);

            Assert.AreEqual(RightLimit, _gimbal.CameraOffsetRight, 1e-6f);
        }

        [Test]
        public void SetCameraPositionOffsets_ClampsRightBelowLimit()
        {
            _gimbal.SetCameraPositionOffsets(-99f, 0f, 0f);

            Assert.AreEqual(-RightLimit, _gimbal.CameraOffsetRight, 1e-6f);
        }

        [Test]
        public void SetCameraPositionOffsets_ClampsForwardAboveLimit()
        {
            _gimbal.SetCameraPositionOffsets(0f, 99f, 0f);

            Assert.AreEqual(ForwardLimit, _gimbal.CameraOffsetForward, 1e-6f);
        }

        [Test]
        public void SetCameraPositionOffsets_ClampsForwardBelowLimit()
        {
            _gimbal.SetCameraPositionOffsets(0f, -99f, 0f);

            Assert.AreEqual(-ForwardLimit, _gimbal.CameraOffsetForward, 1e-6f);
        }

        [Test]
        public void SetCameraPositionOffsets_ClampsUpAboveLimit()
        {
            _gimbal.SetCameraPositionOffsets(0f, 0f, 99f);

            Assert.AreEqual(UpLimit, _gimbal.CameraOffsetUp, 1e-6f);
        }

        [Test]
        public void SetCameraPositionOffsets_ClampsUpBelowLimit()
        {
            _gimbal.SetCameraPositionOffsets(0f, 0f, -99f);

            Assert.AreEqual(-UpLimit, _gimbal.CameraOffsetUp, 1e-6f);
        }

        [Test]
        public void SetCameraPositionOffsets_ClampsEachAxisIndependently()
        {
            _gimbal.SetCameraPositionOffsets(50f, -50f, 0.25f);

            Assert.AreEqual(RightLimit, _gimbal.CameraOffsetRight, 1e-6f);
            Assert.AreEqual(-ForwardLimit, _gimbal.CameraOffsetForward, 1e-6f);
            Assert.AreEqual(0.25f, _gimbal.CameraOffsetUp, 1e-6f);
        }

        [Test]
        public void SetCameraPositionOffsets_ExtremeFloats_DoNotThrow()
        {
            Assert.DoesNotThrow(() =>
                _gimbal.SetCameraPositionOffsets(float.MaxValue, float.MinValue, float.MaxValue));

            Assert.AreEqual(RightLimit, _gimbal.CameraOffsetRight, 1e-6f);
            Assert.AreEqual(-ForwardLimit, _gimbal.CameraOffsetForward, 1e-6f);
            Assert.AreEqual(UpLimit, _gimbal.CameraOffsetUp, 1e-6f);
        }

        // ---------- Reset button ----------

        [Test]
        public void ResetCameraPosition_ZeroesEveryOffset()
        {
            _gimbal.SetCameraPositionOffsets(1.5f, -3f, 0.75f);

            _gimbal.ResetCameraPosition();

            Assert.AreEqual(0f, _gimbal.CameraOffsetRight);
            Assert.AreEqual(0f, _gimbal.CameraOffsetForward);
            Assert.AreEqual(0f, _gimbal.CameraOffsetUp);
        }

        [Test]
        public void ResetCameraPosition_FromClampedExtremes_ZeroesEveryOffset()
        {
            _gimbal.SetCameraPositionOffsets(99f, 99f, 99f);

            _gimbal.ResetCameraPosition();

            Assert.AreEqual(0f, _gimbal.CameraOffsetRight);
            Assert.AreEqual(0f, _gimbal.CameraOffsetForward);
            Assert.AreEqual(0f, _gimbal.CameraOffsetUp);
        }

        [Test]
        public void ResetCameraPosition_IsIdempotent()
        {
            _gimbal.ResetCameraPosition();
            _gimbal.ResetCameraPosition();

            Assert.AreEqual(0f, _gimbal.CameraOffsetRight);
            Assert.AreEqual(0f, _gimbal.CameraOffsetForward);
            Assert.AreEqual(0f, _gimbal.CameraOffsetUp);
        }
    }
}
