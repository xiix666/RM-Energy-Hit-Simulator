using UnityEngine;

namespace Simulation.EnergyHit
{
    /// <summary>
    /// Two-axis gimbal controller. The camera pose relative to the pitch frame is
    /// recorded at startup and kept fixed, which models a rigid camera mount.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GimbalRigController : MonoBehaviour
    {
        private const float ChallengeCameraRange = 6.5f;
        private const float ChallengeCameraWorldHeight = 0.96f;
        private const float ChallengeDirectionalIntensity = 1.2f;
        private const float ChallengeAmbientIntensity = 0.65f;

        [Header("Rig references")]
        [SerializeField] private Transform _yawAxis;
        [SerializeField] private Transform _pitchAxis;
        [SerializeField] private Camera _camera;

        [Header("Control")]
        [SerializeField] private float _yawSensitivity = 55f;
        [SerializeField] private float _pitchSensitivity = 45f;
        [SerializeField] private float _keyboardAngularSpeed = 45f;
        [SerializeField] private bool _useMouse = true;
        [SerializeField] private bool _lockCursor = true;
        [SerializeField] private bool _invertPitch;
        [SerializeField] private KeyCode _toggleMouseAimKey = KeyCode.M;

        [Header("Mechanical limits")]
        [SerializeField] private float _minimumYaw = -40f;
        [SerializeField] private float _maximumYaw = 40f;
        [SerializeField] private float _minimumPitch = -12f;
        [SerializeField] private float _maximumPitch = 18f;
        [SerializeField] private KeyCode _recenterKey = KeyCode.C;

        [Header("Camera extrinsics")]
        [Tooltip("Keep the startup camera pose rigidly fixed relative to the pitch frame.")]
        [SerializeField] private bool _enforceFixedCameraExtrinsics = true;

        private float _yaw;
        private float _pitch;
        private float _cameraOffsetRight;
        private float _cameraOffsetForward;
        private float _cameraOffsetUp;
        private Vector3 _initialYawWorldPosition;
        private Vector3 _movementRightWorld;
        private Vector3 _movementForwardWorld;
        private Vector3 _initialYawLocalEuler;
        private Vector3 _initialPitchLocalEuler;
        private Matrix4x4 _pitchToCamera;

        public float Yaw => _yaw;
        public float Pitch => _pitch;
        public float CameraOffsetRight => _cameraOffsetRight;
        public float CameraOffsetForward => _cameraOffsetForward;
        public float CameraOffsetUp => _cameraOffsetUp;
        public bool MouseAimEnabled => _useMouse;
        public bool ExternalControlEnabled { get; private set; }
        public float InitialYawWorldRad { get; private set; }
        public float InitialCameraElevationRad { get; private set; }

        /// <summary>
        /// Fixed transform from the pitch coordinate frame to the camera frame.
        /// </summary>
        public Matrix4x4 PitchToCamera => _pitchToCamera;

        private void Awake()
        {
            if (_yawAxis == null)
            {
                _yawAxis = transform;
            }

            if (_pitchAxis == null || _camera == null)
            {
                Debug.LogError("GimbalRigController requires a pitch axis and a camera.", this);
                enabled = false;
                return;
            }

            ConfigureChallengeStartingPose();
            ConfigureChallengeLighting();

            _initialYawWorldPosition = _yawAxis.position;
            _movementRightWorld = Vector3.ProjectOnPlane(_yawAxis.right, Vector3.up).normalized;
            _movementForwardWorld = Vector3.ProjectOnPlane(_yawAxis.forward, Vector3.up).normalized;
            _initialYawLocalEuler = _yawAxis.localEulerAngles;
            _initialPitchLocalEuler = _pitchAxis.localEulerAngles;
            _pitchToCamera = _pitchAxis.worldToLocalMatrix * _camera.transform.localToWorldMatrix;
            Vector3 initialForward = _camera.transform.forward;
            InitialYawWorldRad = Mathf.Atan2(initialForward.x, initialForward.z);
            InitialCameraElevationRad = Mathf.Atan2(initialForward.y,
                new Vector2(initialForward.x, initialForward.z).magnitude);

            if (_lockCursor && _useMouse)
            {
                SetCursorLocked(true);
            }
        }

        private void ConfigureChallengeStartingPose()
        {
            EnergyHitTarget[] targets = FindObjectsOfType<EnergyHitTarget>(true);
            Vector3 targetCenter = Vector3.zero;
            int targetCount = 0;
            foreach (EnergyHitTarget target in targets)
            {
                if (target == null || target.TargetTransform == null) continue;
                targetCenter += target.TargetTransform.position;
                targetCount++;
            }
            if (targetCount == 0) return;
            targetCenter /= targetCount;

            Vector3 away = _camera.transform.position - targetCenter;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = Vector3.back;
            away.Normalize();

            float verticalDistance = targetCenter.y - ChallengeCameraWorldHeight;
            float horizontalDistance = Mathf.Sqrt(Mathf.Max(1f,
                ChallengeCameraRange * ChallengeCameraRange - verticalDistance * verticalDistance));
            Vector3 cameraPosition = targetCenter + away * horizontalDistance;
            cameraPosition.y = ChallengeCameraWorldHeight;
            Vector3 aimDirection = targetCenter - cameraPosition;
            float yawAngle = Mathf.Atan2(aimDirection.x, aimDirection.z) * Mathf.Rad2Deg;
            float aimHorizontal = new Vector2(aimDirection.x, aimDirection.z).magnitude;
            float pitchAngle = -Mathf.Atan2(aimDirection.y, aimHorizontal) * Mathf.Rad2Deg;

            Vector3 rigPosition = cameraPosition - Vector3.up * _pitchAxis.localPosition.y;
            _yawAxis.SetPositionAndRotation(rigPosition, Quaternion.Euler(0f, yawAngle, 0f));
            _pitchAxis.localRotation = Quaternion.Euler(pitchAngle, 0f, 0f);
        }

        private static void ConfigureChallengeLighting()
        {
            foreach (Light light in FindObjectsOfType<Light>(true))
            {
                if (light.type == LightType.Directional && light.intensity > ChallengeDirectionalIntensity)
                {
                    light.intensity = ChallengeDirectionalIntensity;
                }
            }
            RenderSettings.ambientIntensity = ChallengeAmbientIntensity;
            RenderSettings.reflectionIntensity = 0.65f;
        }

        private void Update()
        {
            bool toggledMouseAim = Input.GetKeyDown(_toggleMouseAimKey);
            if (toggledMouseAim)
            {
                _useMouse = !_useMouse;
                if (_lockCursor || !_useMouse)
                {
                    SetCursorLocked(_useMouse && _lockCursor);
                }
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                SetCursorLocked(_useMouse && Cursor.lockState != CursorLockMode.Locked);
            }

            if (ExternalControlEnabled)
            {
                ApplyRotation();
                return;
            }

            if (Input.GetKeyDown(_recenterKey))
            {
                SetAngles(0f, 0f);
                return;
            }

            float yawInput = 0f;
            float pitchInput = 0f;

            if (_useMouse && !toggledMouseAim && Cursor.lockState == CursorLockMode.Locked)
            {
                yawInput += Input.GetAxis("Mouse X") * _yawSensitivity;
                pitchInput += Input.GetAxis("Mouse Y") * _pitchSensitivity;
            }

            // Arrow keys remain available when the mouse cursor is released.
            yawInput += GetKeyboardAxis(KeyCode.LeftArrow, KeyCode.RightArrow) * _keyboardAngularSpeed;
            pitchInput += GetKeyboardAxis(KeyCode.DownArrow, KeyCode.UpArrow) * _keyboardAngularSpeed;

            _yaw = ClampAngle(_yaw + yawInput * Time.deltaTime, _minimumYaw, _maximumYaw);
            float pitchSign = _invertPitch ? -1f : 1f;
            _pitch = ClampAngle(_pitch + pitchInput * pitchSign * Time.deltaTime,
                _minimumPitch, _maximumPitch);

            ApplyRotation();
        }

        private void LateUpdate()
        {
            if (!_enforceFixedCameraExtrinsics || _camera == null || _pitchAxis == null)
            {
                return;
            }

            Matrix4x4 cameraLocalToWorld = _pitchAxis.localToWorldMatrix * _pitchToCamera;
            _camera.transform.SetPositionAndRotation(
                cameraLocalToWorld.GetColumn(3),
                cameraLocalToWorld.rotation);
        }

        public void SetAngles(float yaw, float pitch)
        {
            _yaw = ClampAngle(yaw, _minimumYaw, _maximumYaw);
            _pitch = ClampAngle(pitch, _minimumPitch, _maximumPitch);
            ApplyRotation();
        }

        public void SetExternalAngles(float yawDegrees, float pitchDegrees)
        {
            ExternalControlEnabled = true;
            SetAngles(yawDegrees, pitchDegrees);
        }

        public void ReleaseExternalControl()
        {
            ExternalControlEnabled = false;
        }

        public void SetCameraPositionOffsets(float right, float forward, float up)
        {
            _cameraOffsetRight = Mathf.Clamp(right, -2f, 2f);
            _cameraOffsetForward = Mathf.Clamp(forward, -4f, 4f);
            _cameraOffsetUp = Mathf.Clamp(up, -1f, 1f);
            ApplyPosition();
        }

        public void ResetCameraPosition()
        {
            SetCameraPositionOffsets(0f, 0f, 0f);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus && _lockCursor && _useMouse)
            {
                SetCursorLocked(true);
            }
        }

        private void ApplyRotation()
        {
            ApplyPosition();
            _yawAxis.localRotation = Quaternion.Euler(
                _initialYawLocalEuler.x,
                _initialYawLocalEuler.y + _yaw,
                _initialYawLocalEuler.z);

            _pitchAxis.localRotation = Quaternion.Euler(
                _initialPitchLocalEuler.x - _pitch,
                _initialPitchLocalEuler.y,
                _initialPitchLocalEuler.z);
        }

        private void ApplyPosition()
        {
            _yawAxis.position = _initialYawWorldPosition
                + _movementRightWorld * _cameraOffsetRight
                + _movementForwardWorld * _cameraOffsetForward
                + Vector3.up * _cameraOffsetUp;
        }

        private static float GetKeyboardAxis(KeyCode negative, KeyCode positive)
        {
            float value = 0f;
            if (Input.GetKey(negative)) value -= 1f;
            if (Input.GetKey(positive)) value += 1f;
            return value;
        }

        private static float ClampAngle(float value, float minimum, float maximum)
        {
            if (maximum - minimum >= 360f)
            {
                return Mathf.Repeat(value + 180f, 360f) - 180f;
            }

            return Mathf.Clamp(value, minimum, maximum);
        }

        private static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
