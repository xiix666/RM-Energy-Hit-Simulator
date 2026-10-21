using System.Collections.Generic;
using Controller;
using UnityEngine;
using UnityEngine.Events;

namespace Simulation.EnergyHit
{
    /// <summary>
    /// Receives projectile impacts from colliders below this object and changes
    /// the associated energy mechanism state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnergyHitTarget : MonoBehaviour
    {
        private static readonly HashSet<EnergyHitTarget> ActiveTargets =
            new HashSet<EnergyHitTarget>();

        [Header("State change")]
        [SerializeField] private FanController _fanController;
        [SerializeField] private Transform _fanRoot;
        [Tooltip("Radius of the valid circular target area in this object's local XY plane.")]
        [Min(0.001f)]
        [SerializeField] private float _hitRadius = 0.2f;
        [SerializeField] private bool _autoFitRadiusFromCollider = true;
        [Min(0f)]
        [SerializeField] private float _hitCooldown = 0.2f;
        [Tooltip("Maximum distance from the target plane when resolving a hit on the visible model collider.")]
        [Min(0.01f)]
        [SerializeField] private float _collisionPlaneTolerance = 0.25f;

        [Header("Plate fitting")]
        [SerializeField] private bool _fitToPlateVisual = true;
        [Tooltip("The Select mesh is the circular armour face at the end of each fan arm.")]
        [SerializeField] private string _plateNamePrefix = "Select";
        [Tooltip("Official RoboMaster 2026 effective detection diameter: 300 mm.")]
        [Min(0.01f)]
        [SerializeField] private float _effectiveDetectionDiameter = 0.3f;

        [Header("Events")]
        [SerializeField] private UnityEvent _onAcceptedHit;

        private float _nextAcceptedHitTime;

        /// <summary>The center transform of this circular hit region.</summary>
        public Transform TargetTransform => transform;
        public string LastRejectionReason { get; private set; } = string.Empty;

        /// <summary>Whether this target is one of the currently lit valid targets.</summary>
        public bool IsCurrentlyHittable =>
            _fanController != null && _fanController.IsFanHittable(_fanRoot);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetActiveTargets()
        {
            ActiveTargets.Clear();
        }

        private void Reset()
        {
            _fanController = GetComponentInParent<FanController>();
        }

        private void Awake()
        {
            if (_fanController == null)
            {
                _fanController = GetComponentInParent<FanController>();
            }

            if (_fanRoot == null)
            {
                _fanRoot = FindFanRoot();
            }

            FitToPlateVisual();
            ConfigureRuntimeTrigger();
            AutoFitHitRadius();
        }

        private void OnEnable()
        {
            ActiveTargets.Add(this);
        }

        private void OnDisable()
        {
            ActiveTargets.Remove(this);
        }

        public bool TryApplyHit(BallisticProjectile projectile, Vector3 hitPoint, Vector3 hitNormal)
        {
            if (!isActiveAndEnabled)
            {
                LastRejectionReason = "target inactive";
                return false;
            }
            if (Time.time < _nextAcceptedHitTime)
            {
                LastRejectionReason = "target cooldown";
                return false;
            }

            Vector3 localHitPoint = transform.InverseTransformPoint(hitPoint);
            float radialDistanceSquared = localHitPoint.x * localHitPoint.x +
                                          localHitPoint.y * localHitPoint.y;
            if (radialDistanceSquared > _hitRadius * _hitRadius)
            {
                LastRejectionReason = $"outside circle {Mathf.Sqrt(radialDistanceSquared) * 1000f:0}mm > {_hitRadius * 1000f:0}mm";
                return false;
            }

            if (_fanController == null)
            {
                LastRejectionReason = "FanController missing";
                Debug.LogWarning("EnergyHitTarget has no FanController assigned.", this);
                return false;
            }

            if (!_fanController.TryHitFan(_fanRoot))
            {
                LastRejectionReason = "fan not lit/active";
                return false;
            }

            LastRejectionReason = string.Empty;
            _nextAcceptedHitTime = Time.time + _hitCooldown;
            EnergyHitStatistics.RecordAcceptedHit();
            _onAcceptedHit?.Invoke();
            return true;
        }

        /// <summary>
        /// Resolves collisions against the original mechanism meshes. Those
        /// colliders are often reached before the generated target collider.
        /// </summary>
        public static EnergyHitTarget FindForModelCollision(Collider hitCollider, Vector3 hitPoint)
        {
            if (hitCollider == null)
            {
                return null;
            }

            EnergyHitTarget bestTarget = null;
            float bestScore = float.PositiveInfinity;

            foreach (EnergyHitTarget target in ActiveTargets)
            {
                if (target == null || !target.isActiveAndEnabled ||
                    !target.IsCurrentlyHittable ||
                    !target.IsPointInsideCircle(hitPoint))
                {
                    continue;
                }

                Vector3 localPoint = target.transform.InverseTransformPoint(hitPoint);
                float planeDistance = Mathf.Abs(localPoint.z);
                if (planeDistance > target._collisionPlaneTolerance)
                {
                    continue;
                }

                // Prefer the normal fan hierarchy when available, but allow
                // geometry-only resolution for legacy colliders such as
                // ArmorC/Aim which visually overlap a plate yet live outside
                // the fan transform hierarchy.
                float hierarchyPenalty = target.IsColliderBelowFan(hitCollider.transform)
                    ? 0f
                    : 0.01f;
                float score = planeDistance + hierarchyPenalty;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestTarget = target;
                }
            }

            if (bestTarget != null &&
                !bestTarget.IsColliderBelowFan(hitCollider.transform))
            {
                Debug.Log($"Resolved legacy model collider {hitCollider.transform.name} " +
                          $"to lit circular target {bestTarget.name} by geometry.", hitCollider);
            }
            return bestTarget;
        }

        public static bool TryFindTrajectoryHit(Vector3 start, Vector3 end,
            out EnergyHitTarget hitTarget, out Vector3 hitPoint, out Vector3 hitNormal)
        {
            hitTarget = null;
            hitPoint = default;
            hitNormal = default;
            float bestFraction = float.PositiveInfinity;

            foreach (EnergyHitTarget target in ActiveTargets)
            {
                if (target == null || !target.isActiveAndEnabled ||
                    target._fanController == null ||
                    !target._fanController.IsFanHittable(target._fanRoot))
                {
                    continue;
                }

                Vector3 localStart = target.transform.InverseTransformPoint(start);
                Vector3 localEnd = target.transform.InverseTransformPoint(end);
                float denominator = localEnd.z - localStart.z;
                if (Mathf.Abs(denominator) < 0.00001f)
                {
                    continue;
                }

                float fraction = -localStart.z / denominator;
                if (fraction < 0f || fraction > 1f || fraction >= bestFraction)
                {
                    continue;
                }

                Vector3 localIntersection = Vector3.Lerp(localStart, localEnd, fraction);
                float radialDistanceSquared = localIntersection.x * localIntersection.x +
                                              localIntersection.y * localIntersection.y;
                if (radialDistanceSquared > target._hitRadius * target._hitRadius)
                {
                    continue;
                }

                bestFraction = fraction;
                hitTarget = target;
                hitPoint = target.transform.TransformPoint(localIntersection);
                hitNormal = target.transform.forward;
                if (Vector3.Dot(end - start, hitNormal) > 0f)
                {
                    hitNormal = -hitNormal;
                }
            }

            return hitTarget != null;
        }

        public bool FitToPlateVisual()
        {
            if (!_fitToPlateVisual || _fanRoot == null)
            {
                return false;
            }

            Renderer plateRenderer = null;
            foreach (Transform child in _fanRoot)
            {
                if (child.name.StartsWith(_plateNamePrefix))
                {
                    plateRenderer = child.GetComponentInChildren<Renderer>(true);
                    if (plateRenderer != null)
                    {
                        break;
                    }
                }
            }

            if (plateRenderer == null)
            {
                return false;
            }

            MeshFilter meshFilter = plateRenderer.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                return false;
            }

            Bounds meshBounds = meshFilter.sharedMesh.bounds;
            transform.SetPositionAndRotation(
                meshFilter.transform.TransformPoint(meshBounds.center),
                meshFilter.transform.rotation);

            _hitRadius = _effectiveDetectionDiameter * 0.5f;
            BoxCollider boxCollider = GetComponent<BoxCollider>();
            if (boxCollider != null)
            {
                float diameter = _hitRadius * 2f;
                boxCollider.center = Vector3.zero;
                boxCollider.size = new Vector3(diameter, diameter,
                    Mathf.Max(boxCollider.size.z, 0.3f));
            }

            return true;
        }

        private void OnTriggerEnter(Collider other)
        {
            BallisticProjectile projectile = other.GetComponentInParent<BallisticProjectile>();
            if (projectile == null)
            {
                return;
            }

            Vector3 hitNormal = transform.forward;
            if (Vector3.Dot(projectile.Velocity, hitNormal) > 0f)
            {
                hitNormal = -hitNormal;
            }

            Vector3 hitPoint;
            Plane targetPlane = new Plane(transform.forward, transform.position);
            Vector3 velocity = projectile.Velocity;
            Ray trajectory = new Ray(projectile.transform.position,
                velocity.sqrMagnitude > 0.0001f ? velocity.normalized : -hitNormal);
            if (!targetPlane.Raycast(trajectory, out float distanceToPlane) ||
                distanceToPlane < 0f)
            {
                Vector3 localPoint = transform.InverseTransformPoint(projectile.transform.position);
                localPoint.z = 0f;
                hitPoint = transform.TransformPoint(localPoint);
            }
            else
            {
                hitPoint = trajectory.GetPoint(distanceToPlane);
            }

            projectile.TryImpactTarget(this, hitPoint, hitNormal);
        }

        private bool IsColliderBelowFan(Transform colliderTransform)
        {
            return _fanRoot != null &&
                   (colliderTransform == _fanRoot || colliderTransform.IsChildOf(_fanRoot));
        }

        private bool IsPointInsideCircle(Vector3 worldPoint)
        {
            Vector3 localPoint = transform.InverseTransformPoint(worldPoint);
            float radialDistanceSquared = localPoint.x * localPoint.x +
                                          localPoint.y * localPoint.y;
            return radialDistanceSquared <= _hitRadius * _hitRadius;
        }

        private Transform FindFanRoot()
        {
            Transform current = transform;
            while (current != null && current != _fanController?.transform)
            {
                if (current.name.StartsWith("fan"))
                {
                    return current;
                }

                current = current.parent;
            }

            return null;
        }

        private void AutoFitHitRadius()
        {
            if (!_autoFitRadiusFromCollider)
            {
                return;
            }

            BoxCollider boxCollider = GetComponent<BoxCollider>();
            if (boxCollider == null)
            {
                return;
            }

            // The generated BoxCollider XY size is the circle diameter.
            float fittedRadius = Mathf.Min(Mathf.Abs(boxCollider.size.x),
                Mathf.Abs(boxCollider.size.y)) * 0.5f;
            _hitRadius = Mathf.Max(_hitRadius, fittedRadius);
        }

        private void ConfigureRuntimeTrigger()
        {
            BoxCollider boxCollider = GetComponent<BoxCollider>();
            if (boxCollider == null)
            {
                return;
            }

            boxCollider.isTrigger = true;
            Vector3 size = boxCollider.size;
            size.z = Mathf.Max(size.z, 0.3f);
            boxCollider.size = size;
        }

    }
}
