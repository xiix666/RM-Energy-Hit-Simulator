using UnityEngine;
using UnityEngine.Rendering;

namespace Simulation.EnergyHit
{
    /// <summary>
    /// Marker and collision handler for a physical projectile.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(Collider))]
    public sealed class BallisticProjectile : MonoBehaviour
    {
        [Min(0.1f)]
        [SerializeField] private float _maximumLifetime = 8f;
        [SerializeField] private bool _destroyOnAnyCollision = true;

        private Rigidbody _rigidbody;
        private bool _hasCollided;
        private bool _impactRecorded;
        private bool _targetRejectionRecorded;
        private Material _runtimeMaterial;
        private Material _runtimeTrailMaterial;
        private Transform _ownerRoot;
        private float _launchTime;

        public Vector3 Velocity => _rigidbody != null ? _rigidbody.velocity : Vector3.zero;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        }

        public void Launch(Vector3 velocity, float mass, bool useGravity,
            Collider[] ownerColliders, Transform ownerRoot)
        {
            if (_rigidbody == null)
            {
                _rigidbody = GetComponent<Rigidbody>();
            }

            _rigidbody.mass = Mathf.Max(0.0001f, mass);
            _rigidbody.useGravity = useGravity;
            _rigidbody.velocity = velocity;
            _ownerRoot = ownerRoot;
            _launchTime = Time.time;

            Collider projectileCollider = GetComponent<Collider>();
            if (ownerColliders != null)
            {
                foreach (Collider ownerCollider in ownerColliders)
                {
                    if (ownerCollider != null)
                    {
                        Physics.IgnoreCollision(projectileCollider, ownerCollider, true);
                    }
                }
            }

            Destroy(gameObject, _maximumLifetime);
        }

        public void ConfigureVisual(Color color, float trailTime, float trailStartWidth,
            float visualDiameter)
        {
            // Use a lit material so the projectile reads as a small 3D sphere
            // instead of a flat emissive dot.
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            if (shader != null)
            {
                _runtimeMaterial = new Material(shader);
                if (_runtimeMaterial.HasProperty("_BaseColor"))
                {
                    _runtimeMaterial.SetColor("_BaseColor", color);
                }
                if (_runtimeMaterial.HasProperty("_Color"))
                {
                    _runtimeMaterial.SetColor("_Color", color);
                }
                if (_runtimeMaterial.HasProperty("_EmissionColor"))
                {
                    _runtimeMaterial.DisableKeyword("_EMISSION");
                    _runtimeMaterial.SetColor("_EmissionColor", Color.black);
                }
                if (_runtimeMaterial.HasProperty("_Smoothness"))
                {
                    _runtimeMaterial.SetFloat("_Smoothness", 0.55f);
                }
                if (_runtimeMaterial.HasProperty("_Glossiness"))
                {
                    _runtimeMaterial.SetFloat("_Glossiness", 0.55f);
                }
                if (_runtimeMaterial.HasProperty("_Metallic"))
                {
                    _runtimeMaterial.SetFloat("_Metallic", 0.05f);
                }

                Renderer projectileRenderer = GetComponentInChildren<Renderer>();
                if (projectileRenderer != null)
                {
                    projectileRenderer.sharedMaterial = _runtimeMaterial;
                    projectileRenderer.shadowCastingMode = ShadowCastingMode.On;
                    projectileRenderer.receiveShadows = true;
                    projectileRenderer.transform.localScale = Vector3.one * visualDiameter;
                }
            }

            TrailRenderer trail = GetComponent<TrailRenderer>();
            if (trail == null)
            {
                trail = gameObject.AddComponent<TrailRenderer>();
            }

            trail.time = trailTime;
            trail.startWidth = trailStartWidth;
            trail.endWidth = 0f;
            trail.minVertexDistance = 0.02f;
            trail.startColor = color;
            trail.endColor = new Color(color.r, color.g, color.b, 0f);
            trail.emitting = trailTime > 0f;
            Shader trailShader = Shader.Find("Sprites/Default");
            if (trailShader == null)
            {
                trailShader = shader;
            }
            if (trailShader != null)
            {
                _runtimeTrailMaterial = new Material(trailShader);
                if (_runtimeTrailMaterial.HasProperty("_Color"))
                {
                    _runtimeTrailMaterial.SetColor("_Color", color);
                }
                trail.sharedMaterial = _runtimeTrailMaterial;
            }
        }

        private void FixedUpdate()
        {
            if (_hasCollided || _rigidbody == null || _rigidbody.velocity.sqrMagnitude < 0.0001f)
            {
                return;
            }

            Vector3 start = _rigidbody.position;
            Vector3 end = start + _rigidbody.velocity * Time.fixedDeltaTime;
            if (EnergyHitTarget.TryFindTrajectoryHit(start, end, out EnergyHitTarget target,
                    out Vector3 hitPoint, out Vector3 hitNormal))
            {
                TryImpactTarget(target, hitPoint, hitNormal);
            }
        }

        private void OnDestroy()
        {
            if (_runtimeMaterial != null)
            {
                Destroy(_runtimeMaterial);
            }
            if (_runtimeTrailMaterial != null)
            {
                Destroy(_runtimeTrailMaterial);
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (_hasCollided)
            {
                return;
            }

            ContactPoint impact = collision.GetContact(0);
            EnergyHitTarget target = collision.collider.GetComponentInParent<EnergyHitTarget>();
            if (target == null)
            {
                target = EnergyHitTarget.FindForModelCollision(collision.collider, impact.point);
            }

            bool accepted = target != null && TryImpactTarget(target, impact.point, impact.normal);
            bool hitOwnRig = _ownerRoot != null && collision.collider.transform.IsChildOf(_ownerRoot);
            Transform colliderTransform = collision.collider.transform;
            string colliderName = colliderTransform.parent != null
                ? $"{colliderTransform.parent.name}/{colliderTransform.name}"
                : colliderTransform.name;
            RecordImpactOnce(colliderName, hitOwnRig, accepted);

            if (target == null && colliderTransform.name.StartsWith("EnergyHitTarget"))
            {
                Debug.LogWarning($"Projectile hit an unbound target collider: {colliderName}.",
                    collision.collider);
            }

            if (accepted || _destroyOnAnyCollision)
            {
                _hasCollided = true;
                Destroy(gameObject);
            }
        }

        public bool TryImpactTarget(EnergyHitTarget target, Vector3 hitPoint, Vector3 hitNormal)
        {
            if (_hasCollided || target == null)
            {
                return false;
            }
            if (!target.TryApplyHit(this, hitPoint, hitNormal))
            {
                if (!_targetRejectionRecorded)
                {
                    _targetRejectionRecorded = true;
                    EnergyHitStatistics.RecordTargetRejection(target.name, target.LastRejectionReason);
                }
                return false;
            }

            _hasCollided = true;
            RecordImpactOnce(target.name, false, true);

            Collider projectileCollider = GetComponent<Collider>();
            if (projectileCollider != null)
            {
                projectileCollider.enabled = false;
            }

            if (_rigidbody != null)
            {
                _rigidbody.velocity = Vector3.zero;
                _rigidbody.isKinematic = true;
            }

            Destroy(gameObject);
            return true;
        }

        private void RecordImpactOnce(string colliderName, bool hitOwnRig, bool accepted)
        {
            if (_impactRecorded) return;
            _impactRecorded = true;
            EnergyHitStatistics.RecordProjectileImpact(
                colliderName, Time.time - _launchTime, hitOwnRig, accepted);
        }
    }
}
