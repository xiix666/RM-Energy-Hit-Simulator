using System.Collections;
using GameFramework.Debugger;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace Simulation.EnergyHit
{
    /// <summary>
    /// Automatically adds an Energy Hit statistics page to the in-game
    /// UnityGameFramework debugger without requiring scene wiring.
    /// </summary>
    internal sealed class EnergyHitDebuggerWindow : MonoBehaviour
    {
        private const string WindowPath = "Energy Hit/Statistics";
        private static readonly Rect StatisticsRect = new Rect(125f, 10f, 420f, 38f);
        private static readonly Rect CameraControlsRect = new Rect(125f, 60f, 650f, 205f);
        private static bool _created;

        private DebuggerComponent _debugger;
        private EnergyHitStatisticsDebuggerPage _window;
        private GimbalRigController _gimbal;
        private Controller.PowerRuneController _powerRune;
        private GUIStyle _hudStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistration()
        {
            _created = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateRegistrar()
        {
            if (_created)
            {
                return;
            }

            _created = true;
            GameObject registrar = new GameObject("EnergyHitDebuggerRegistrar");
            registrar.hideFlags = HideFlags.HideInHierarchy;
            DontDestroyOnLoad(registrar);
            registrar.AddComponent<EnergyHitDebuggerWindow>();
        }

        private IEnumerator Start()
        {
            while (_debugger == null)
            {
                _debugger = FindObjectOfType<DebuggerComponent>();
                if (_debugger == null)
                {
                    yield return null;
                }
            }

            // Wait until DebuggerComponent.Start has registered its built-in pages.
            yield return null;
            _window = new EnergyHitStatisticsDebuggerPage();
            _debugger.RegisterDebuggerWindow(WindowPath, _window);
        }

        private void OnDestroy()
        {
            if (_debugger != null && _window != null)
            {
                _debugger.UnregisterDebuggerWindow(WindowPath);
            }
        }

        private void OnGUI()
        {
            if (_hudStyle == null)
            {
                _hudStyle = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.MiddleLeft,
                    fontSize = 14,
                    richText = true,
                    padding = new RectOffset(10, 10, 6, 6),
                };
            }

            GUI.Box(StatisticsRect,
                $"SHOT {EnergyHitStatistics.FiredCount}  HIT {EnergyHitStatistics.AcceptedHitCount}  RATE {EnergyHitStatistics.HitRate:P1}",
                _hudStyle);

            if (_gimbal == null)
            {
                _gimbal = FindObjectOfType<GimbalRigController>();
            }
            if (_gimbal != null)
            {
                DrawCameraPositionControls();
            }
        }

        private void DrawCameraPositionControls()
        {
            if (_powerRune == null)
            {
                _powerRune = FindObjectOfType<Controller.PowerRuneController>();
            }

            GUILayout.BeginArea(CameraControlsRect, GUI.skin.box);
            GUILayout.Label($"CAMERA POSITION  (M: MOUSE AIM {(_gimbal.MouseAimEnabled ? "ON" : "OFF")}, Esc: cursor)");
            if (_powerRune != null)
            {
                GUILayout.Label($"R: RUNE ROTATION {_powerRune.RotationMode.ToString().ToUpperInvariant()}");
            }

            float right = DrawSlider("LEFT / RIGHT", _gimbal.CameraOffsetRight, -2f, 2f);
            float forward = DrawSlider("BACK / FORWARD", _gimbal.CameraOffsetForward, -4f, 4f);
            float up = DrawSlider("DOWN / UP", _gimbal.CameraOffsetUp, -1f, 1f);
            _gimbal.SetCameraPositionOffsets(right, forward, up);

            if (GUILayout.Button("RESET CAMERA POSITION", GUILayout.Height(26f)))
            {
                _gimbal.ResetCameraPosition();
            }
            GUILayout.EndArea();
        }

        internal static bool IsPointerOverCameraControls()
        {
            if (Cursor.lockState == CursorLockMode.Locked) return false;
            Vector3 mouse = Input.mousePosition;
            return CameraControlsRect.Contains(new Vector2(mouse.x, Screen.height - mouse.y));
        }

        private static float DrawSlider(string label, float value, float minimum, float maximum)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label + ": " + value.ToString("+0.00;-0.00;0.00") + " m", GUILayout.Width(210f));
            value = GUILayout.HorizontalSlider(value, minimum, maximum, GUILayout.Width(400f));
            GUILayout.EndHorizontal();
            return value;
        }
    }

    internal sealed class EnergyHitStatisticsDebuggerPage : IDebuggerWindow
    {
        public void Initialize(params object[] args)
        {
        }

        public void Shutdown()
        {
        }

        public void OnEnter()
        {
        }

        public void OnLeave()
        {
        }

        public void OnUpdate(float elapseSeconds, float realElapseSeconds)
        {
        }

        public void OnDraw()
        {
            GUILayout.Label("<b>Energy Hit Simulation</b>");
            GUILayout.BeginVertical("box");
            DrawItem("Projectiles fired", EnergyHitStatistics.FiredCount.ToString());
            DrawItem("Accepted hits", EnergyHitStatistics.AcceptedHitCount.ToString());
            DrawItem("Hit rate", $"{EnergyHitStatistics.HitRate:P1}");
            GUILayout.Space(8f);
            if (GUILayout.Button("Reset counters", GUILayout.Height(30f)))
            {
                EnergyHitStatistics.Reset();
            }
            GUILayout.EndVertical();
        }

        private static void DrawItem(string title, string content)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(title, GUILayout.Width(240f));
            GUILayout.Label(content);
            GUILayout.EndHorizontal();
        }
    }
}
