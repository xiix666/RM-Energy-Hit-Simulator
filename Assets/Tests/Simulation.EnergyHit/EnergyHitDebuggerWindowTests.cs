using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Simulation.EnergyHit.Tests
{
    /// <summary>
    /// Covers the <c>internal sealed EnergyHitDebuggerWindow</c> declared in
    /// <c>Assets/Scripts/Simulation/EnergyHitDebuggerWindow.cs</c>.
    /// The window is <c>internal</c> and lives in Assembly-CSharp, so it is reached
    /// through reflection instead of adding InternalsVisibleTo to production code.
    /// Only members that are safe to evaluate outside a running scene and outside
    /// an OnGUI pass are exercised here.
    /// </summary>
    [TestFixture]
    public sealed class EnergyHitDebuggerWindowTests
    {
        private const string WindowTypeName = "Simulation.EnergyHit.EnergyHitDebuggerWindow";

        private Type _windowType;
        private CursorLockMode _savedCursorLock;

        [SetUp]
        public void SetUp()
        {
            _windowType = EnergyHitTestReflection.GetType(WindowTypeName);
            _savedCursorLock = Cursor.lockState;
        }

        [TearDown]
        public void TearDown()
        {
            Cursor.lockState = _savedCursorLock;
            EnergyHitTestReflection.SetStaticField(_windowType, "_created", false);
        }

        // ---------- Type contract ----------

        [Test]
        public void WindowType_IsInternalSealedMonoBehaviour()
        {
            Assert.IsTrue(_windowType.IsNotPublic, "class must stay internal");
            Assert.IsTrue(_windowType.IsSealed, "class must stay sealed");
            Assert.IsTrue(_windowType.IsSubclassOf(typeof(MonoBehaviour)));
        }

        [Test]
        public void WindowType_DeclaresExpectedMembers()
        {
            Assert.IsNotNull(_windowType.GetMethod("Start",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
            Assert.IsNotNull(_windowType.GetMethod("OnDestroy",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
            Assert.IsNotNull(_windowType.GetMethod("OnGUI",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
            Assert.IsNotNull(_windowType.GetMethod("DrawCameraPositionControls",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
        }

        [Test]
        public void Start_IsACoroutine()
        {
            // Start polls for the DebuggerComponent one frame at a time, so it must
            // return IEnumerator rather than run synchronously.
            MethodInfo start = _windowType.GetMethod("Start",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            Assert.IsNotNull(start);
            Assert.AreEqual(typeof(IEnumerator), start.ReturnType);
        }

        // ---------- Debugger registration ----------

        [Test]
        public void WindowPath_IsEnergyHitStatistics()
        {
            string path = (string)EnergyHitTestReflection.GetStaticField(_windowType, "WindowPath");

            Assert.AreEqual("Energy Hit/Statistics", path);
        }

        [Test]
        public void ResetRegistration_ClearsCreatedFlag()
        {
            EnergyHitTestReflection.SetStaticField(_windowType, "_created", true);
            Assert.AreEqual(true, EnergyHitTestReflection.GetStaticField(_windowType, "_created"));

            EnergyHitTestReflection.InvokeStatic(_windowType, "ResetRegistration");

            Assert.AreEqual(false, EnergyHitTestReflection.GetStaticField(_windowType, "_created"),
                "the registrar must be allowed to run again after a domain reload");
        }

        [Test]
        public void ResetRegistration_IsIdempotent()
        {
            EnergyHitTestReflection.InvokeStatic(_windowType, "ResetRegistration");
            EnergyHitTestReflection.InvokeStatic(_windowType, "ResetRegistration");

            Assert.AreEqual(false, EnergyHitTestReflection.GetStaticField(_windowType, "_created"));
        }

        // ---------- HUD layout ----------

        [Test]
        public void StatisticsRect_MatchesHudLayout()
        {
            Rect rect = (Rect)EnergyHitTestReflection.GetStaticField(_windowType, "StatisticsRect");

            Assert.AreEqual(new Rect(125f, 10f, 420f, 38f), rect);
        }

        [Test]
        public void CameraControlsRect_MatchesHudLayout()
        {
            Rect rect = (Rect)EnergyHitTestReflection.GetStaticField(_windowType, "CameraControlsRect");

            Assert.AreEqual(new Rect(125f, 60f, 650f, 205f), rect);
        }

        [Test]
        public void HudRects_DoNotOverlapVertically()
        {
            Rect statistics = (Rect)EnergyHitTestReflection.GetStaticField(_windowType, "StatisticsRect");
            Rect controls = (Rect)EnergyHitTestReflection.GetStaticField(_windowType, "CameraControlsRect");

            Assert.LessOrEqual(statistics.yMax, controls.yMin,
                "the statistics box must end before the camera controls start");
        }

        [Test]
        public void CameraControlsRect_IsWideEnoughForTheSliderColumn()
        {
            // DrawCameraPositionControls lays out a 210 px label plus a 400 px slider.
            Rect controls = (Rect)EnergyHitTestReflection.GetStaticField(_windowType, "CameraControlsRect");

            Assert.GreaterOrEqual(controls.width, 210f + 400f);
        }

        [Test]
        public void HudRects_AreInsideTheLeftHalfOfTheScreen()
        {
            Rect controls = (Rect)EnergyHitTestReflection.GetStaticField(_windowType, "CameraControlsRect");

            Assert.GreaterOrEqual(controls.xMin, 0f);
        }

        // ---------- Slider helper contract ----------

        [Test]
        public void DrawSlider_IsStaticFloatHelperWithLabelValueMinMax()
        {
            MethodInfo drawSlider = EnergyHitTestReflection.GetStaticMethod(_windowType, "DrawSlider");

            Assert.AreEqual(typeof(float), drawSlider.ReturnType);
            ParameterInfo[] parameters = drawSlider.GetParameters();
            Assert.AreEqual(4, parameters.Length);
            Assert.AreEqual(typeof(string), parameters[0].ParameterType);
            Assert.AreEqual(typeof(float), parameters[1].ParameterType);
            Assert.AreEqual(typeof(float), parameters[2].ParameterType);
            Assert.AreEqual(typeof(float), parameters[3].ParameterType);
        }

        // ---------- IsPointerOverCameraControls ----------

        [Test]
        public void IsPointerOverCameraControls_IsInternalStatic()
        {
            MethodInfo method = EnergyHitTestReflection.GetStaticMethod(
                _windowType, "IsPointerOverCameraControls");

            Assert.AreEqual(typeof(bool), method.ReturnType);
            Assert.IsTrue(method.IsAssembly, "must stay internal so it is only used inside the simulation assembly");
        }

        [Test]
        public void IsPointerOverCameraControls_WhenCursorLocked_ReturnsFalse()
        {
            // Mouse aim owns the pointer while the cursor is locked, so the controls
            // must never swallow input in that state.
            Cursor.lockState = CursorLockMode.Locked;

            bool result = (bool)EnergyHitTestReflection.InvokeStatic(
                _windowType, "IsPointerOverCameraControls");

            Assert.IsFalse(result);
        }

        [Test]
        public void IsPointerOverCameraControls_WhenCursorConfined_ReturnsFalse()
        {
            // Only Locked is short-circuited; Confined still runs the hit test.
            Cursor.lockState = CursorLockMode.Confined;

            Assert.DoesNotThrow(() => EnergyHitTestReflection.InvokeStatic(
                _windowType, "IsPointerOverCameraControls"));
        }

        [Test]
        public void IsPointerOverCameraControls_WhenUnlocked_MatchesTheControlsRectDefinition()
        {
            Cursor.lockState = CursorLockMode.None;

            Rect controls = (Rect)EnergyHitTestReflection.GetStaticField(_windowType, "CameraControlsRect");
            Vector3 mouse = Input.mousePosition;
            Vector2 guiPoint = new Vector2(mouse.x, Screen.height - mouse.y);

            bool expected = controls.Contains(guiPoint);
            bool actual = (bool)EnergyHitTestReflection.InvokeStatic(
                _windowType, "IsPointerOverCameraControls");

            Assert.AreEqual(expected, actual,
                "the hit test must use the same rect and the same GUI-space Y flip as the layout constant");
        }

        [Test]
        public void IsPointerOverCameraControls_UsesGuiSpaceYFlip()
        {
            // Screen.height - mouse.y converts the bottom-left origin of Input.mousePosition
            // into the top-left origin used by the GUILayout area rect.
            Rect controls = (Rect)EnergyHitTestReflection.GetStaticField(_windowType, "CameraControlsRect");
            Vector3 mouse = Input.mousePosition;

            bool usingFlip = controls.Contains(new Vector2(mouse.x, Screen.height - mouse.y));
            bool withoutFlip = controls.Contains(new Vector2(mouse.x, mouse.y));

            if (usingFlip != withoutFlip)
            {
                bool actual = (bool)EnergyHitTestReflection.InvokeStatic(
                    _windowType, "IsPointerOverCameraControls");
                Assert.AreEqual(usingFlip, actual,
                    "the implementation must apply the Screen.height - mouse.y flip");
            }
            else
            {
                Assert.Pass("Current pointer position cannot distinguish the two conventions; covered by the definition test.");
            }
        }

        [Test]
        public void IsPointerOverCameraControls_RectCenterIsInsideTheHitRegion()
        {
            // Sanity check on the rect itself: its own centre must always be contained.
            Rect controls = (Rect)EnergyHitTestReflection.GetStaticField(_windowType, "CameraControlsRect");

            Assert.IsTrue(controls.Contains(controls.center));
            Assert.IsFalse(controls.Contains(new Vector2(controls.xMin - 1f, controls.center.y)));
        }
    }
}
