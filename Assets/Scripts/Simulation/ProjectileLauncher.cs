using System.Collections.Generic;
using UnityEngine;

namespace Simulation.EnergyHit
{
    /// <summary>
    /// Spawns and launches physical projectiles from a muzzle transform.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProjectileLauncher : MonoBehaviour
    {
        private static ProjectileLauncher _activeInstance;

        [Header("References")]
        [SerializeField] private Transform _muzzle;
        [SerializeField] private BallisticProjectile _projectilePrefab;

        [Header("Projectile")]
        [SerializeField] private float _muzzleVelocity = 20f;
        [SerializeField] private float _projectileMass = 0.041f;
        [SerializeField] private float _projectileRadius = 0.02125f;
        [Tooltip("Visual diameter only. Physics still uses Projectile Radius.")]
        [Min(0.01f)]
        [SerializeField] private float _visualDiameter = 0.06f;
        [SerializeField] private bool _useGravity = true;
        [SerializeField] private int _projectileLayer;
        [SerializeField] private Color _projectileColor = new Color(0.1f, 1f, 0.2f, 1f);
        [Min(0f)]
        [SerializeField] private float _trailTime = 0.4f;
        [Min(0.001f)]
        [SerializeField] private float _trailStartWidth = 0.045f;

        [Header("Fire control")]
        [SerializeField] private KeyCode _fireKey = KeyCode.Mouse0;
        [Tooltip("Keyboard fallback, useful when the Unity Game view has just gained focus.")]
        [SerializeField] private KeyCode _alternateFireKey = KeyCode.Space;
        [SerializeField] private bool _automatic;
        [Min(0.01f)]
        [SerializeField] private float _shotInterval = 0.15f;

        private float _nextShotTime;
        private bool _suppressMouseFireUntilRelease;
        private Collider[] _ownerColliders;
        private Transform _ownerRoot;
        private const string AntiFlightBarrierName = "防飞天";
        private const float CompetitionMuzzleVelocity = 20f;

        public Transform MuzzleTransform => _muzzle;
        public float MuzzleVelocity => _muzzleVelocity;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetActiveInstance()
        {
            _activeInstance = null;
        }

        private void Awake()
        {
            if (_activeInstance != null && _activeInstance != this)
            {
                Debug.LogWarning("Duplicate ProjectileLauncher disabled. Check additive scene loading.", this);
                enabled = false;
                return;
            }

            _activeInstance = this;
            // Keep the simulated launcher and the published ballistic model in
            // lockstep even when opening a scene serialized with an older value.
            _muzzleVelocity = CompetitionMuzzleVelocity;

            if (_muzzle == null)
            {
                _muzzle = transform;
            }

            GimbalRigController rig = GetComponentInParent<GimbalRigController>();
            _ownerRoot = rig != null ? rig.transform : transform.root;
            // Camera/mount colliders can be children or siblings of the launcher.
            List<Collider> ignoredColliders = new List<Collider>(
                _ownerRoot.GetComponentsInChildren<Collider>(true));
            // This arena volume prevents robots from flying; it is not a
            // physical obstacle for the energy projectile. It currently sits
            // across the firing lane and was swallowing shots after 1-20 ms.
            int ignoredBarrierCount = 0;
            foreach (Collider collider in FindObjectsOfType<Collider>(true))
            {
                if (collider != null && collider.name == AntiFlightBarrierName)
                {
                    ignoredColliders.Add(collider);
                    ignoredBarrierCount++;
                }
            }
            _ownerColliders = ignoredColliders.ToArray();
            Debug.Log($"Projectile ignores {ignoredBarrierCount} anti-flight barrier collider(s).", this);
        }

        private void Update()
        {
            // A click that begins on a camera slider must never become a shot,
            // even if the pointer leaves the controls while dragging.
            if (Input.GetMouseButtonDown(0) && EnergyHitDebuggerWindow.IsPointerOverCameraControls())
            {
                _suppressMouseFireUntilRelease = true;
            }

            bool primaryFire = ReadFireKey(_fireKey, _automatic);
            bool alternateFire = ReadFireKey(_alternateFireKey, _automatic);
            if (primaryFire || alternateFire)
            {
                Fire();
            }

            if (!Input.GetMouseButton(0))
            {
                _suppressMouseFireUntilRelease = false;
            }
        }

        private bool ReadFireKey(KeyCode key, bool held)
        {
            if (key == KeyCode.None)
            {
                return false;
            }

            if (key == KeyCode.Mouse0)
            {
                return !_suppressMouseFireUntilRelease &&
                    (held ? Input.GetMouseButton(0) : Input.GetMouseButtonDown(0));
            }

            return held ? Input.GetKey(key) : Input.GetKeyDown(key);
        }

        public bool Fire()
        {
            if (Time.time < _nextShotTime || _muzzle == null)
            {
                return false;
            }

            _nextShotTime = Time.time + _shotInterval;
            BallisticProjectile projectile = CreateProjectile();
            projectile.transform.SetPositionAndRotation(_muzzle.position, _muzzle.rotation);
            projectile.Launch(
                _muzzle.forward * _muzzleVelocity,
                _projectileMass,
                _useGravity,
                _ownerColliders,
                _ownerRoot);
            projectile.ConfigureVisual(_projectileColor, _trailTime, _trailStartWidth, _visualDiameter);
            EnergyHitStatistics.RecordFired();
            Debug.Log($"Projectile fired at {_muzzleVelocity:F1} m/s.", this);
            return true;
        }

        private BallisticProjectile CreateProjectile()
        {
            if (_projectilePrefab != null)
            {
                return Instantiate(_projectilePrefab);
            }

            GameObject projectileObject = new GameObject("Runtime Projectile");
            projectileObject.name = "Runtime Projectile";
            projectileObject.layer = _projectileLayer;
            SphereCollider projectileCollider = projectileObject.AddComponent<SphereCollider>();
            projectileCollider.radius = _projectileRadius;
            projectileObject.AddComponent<Rigidbody>();
            BallisticProjectile projectile = projectileObject.AddComponent<BallisticProjectile>();

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visual.name = "Projectile Visual";
            visual.layer = _projectileLayer;
            Collider visualCollider = visual.GetComponent<Collider>();
            visualCollider.enabled = false;
            Destroy(visualCollider);
            visual.transform.SetParent(projectileObject.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one * _visualDiameter;
            return projectile;
        }

        private void OnDrawGizmosSelected()
        {
            Transform muzzle = _muzzle != null ? _muzzle : transform;
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(muzzle.position, muzzle.forward * 2f);
        }

        private void OnDestroy()
        {
            if (_activeInstance == this)
            {
                _activeInstance = null;
            }
        }
    }
}
