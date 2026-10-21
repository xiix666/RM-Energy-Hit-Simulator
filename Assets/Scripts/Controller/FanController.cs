using System;
using System.Collections.Generic;
using Label;
using ScriptableObjects;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

namespace Controller
{
    public enum EnergyRotationMode
    {
        Stopped,
        Uniform,
        Periodic
    }

    public class FanController : MonoBehaviour
    {
        [SerializeField] private FanControllerSO _fanControllerConfig;
        [SerializeField] private FanSO _fanConfig;
        [SerializeField] private float _curSpeed = 0;
        
        private ObjectRotator _rotator;
        private bool _isRotating = false;
        private bool _isPowerOn = false;
        private float _motionElapsedTime;
        private float _speedAmplitude;
        private float _angularFrequency;
        private float _speedBias;
        private EnergyRotationMode _rotationMode = EnergyRotationMode.Periodic;
        private Dictionary<int, Fan> _fans;
        private int _index = 0;

        public bool IsPowerOn => _isPowerOn;
        public bool IsRotating => _isRotating;
        public EnergyRotationMode RotationMode => _rotationMode;

        private void Awake()
        {
            _fans = new Dictionary<int, Fan>();
            _rotator = GetComponent<ObjectRotator>();
            _fanConfig.Init();
            FindFans();
            StopRotate();
        }

        public void ToggleRotate()
        {
            SetRotating(!_isRotating);
        }

        public void SetRotating(bool rotating)
        {
            _isRotating = rotating && _rotationMode != EnergyRotationMode.Stopped;
            if (!_isRotating)
            {
                StopRotate();
            }
        }

        public void SetRotationMode(EnergyRotationMode mode)
        {
            if (_rotationMode == mode) return;

            _rotationMode = mode;
            if (mode == EnergyRotationMode.Stopped)
            {
                SetRotating(false);
                return;
            }

            if (mode == EnergyRotationMode.Periodic)
            {
                // A new periodic run starts with a fresh rule-compliant profile.
                ResetMotionProfile();
            }

            SetRotating(true);
        }

        public void ToggleIsPowerOn()
        {
            SetPowerOn(!_isPowerOn);
        }

        public void SetPowerOn(bool powerOn)
        {
            if (_isPowerOn == powerOn)
            {
                return;
            }

            _isPowerOn = powerOn;
            if (!_isPowerOn)
            {
                StopAllLights();
            }
            else
            {
                // The rule resets t, a and omega whenever the large energy
                // mechanism enters its activatable state.
                ResetMotionProfile();
                ChangeFanLight();
            }
        }

        public void ChangeFanLight()
        {
            ChangeFanLight(-1);
        }

        /// <summary>
        /// Accepts a hit only when it belongs to one of the two currently lit,
        /// not-yet-activated fan targets. A successful hit generates the next
        /// pattern and prevents the just-hit fan from being selected again
        /// immediately.
        /// </summary>
        public bool TryHitFan(Transform fanTransform)
        {
            if (!_isPowerOn || fanTransform == null)
            {
                return false;
            }

            foreach (KeyValuePair<int, Fan> item in _fans)
            {
                if (fanTransform != item.Value.Root && !fanTransform.IsChildOf(item.Value.Root))
                {
                    continue;
                }

                if (!item.Value.IsHittable)
                {
                    return false;
                }

                ChangeFanLight(item.Key);
                return true;
            }

            return false;
        }

        public bool IsFanHittable(Transform fanTransform)
        {
            if (!_isPowerOn || fanTransform == null)
            {
                return false;
            }

            foreach (Fan fan in _fans.Values)
            {
                if (fanTransform == fan.Root || fanTransform.IsChildOf(fan.Root))
                {
                    return fan.IsHittable;
                }
            }

            return false;
        }

        private void ChangeFanLight(int excludedFanIndex)
        {
            if (!_isPowerOn)
            {
                _isPowerOn = true;
                ResetMotionProfile();
            }

            List<int> candidates = new List<int>();
            for (int i = 0; i < _fans.Count; i++)
            {
                if (i != excludedFanIndex)
                {
                    candidates.Add(i);
                }
            }

            if (candidates.Count < 2)
            {
                Debug.LogWarning("FanController requires at least two candidate fans.", this);
                return;
            }

            int firstCandidate = Random.Range(0, candidates.Count);
            int firstFanIndex = candidates[firstCandidate];
            candidates.RemoveAt(firstCandidate);
            int secondFanIndex = candidates[Random.Range(0, candidates.Count)];

            for (int i = 0; i < _fans.Count; i++)
            {
                if (i == firstFanIndex || i == secondFanIndex)
                {
                    _fans[i].ChangeState(FanState.NotActive);
                }
                else
                {
                    _fans[i].ChangeState(FanState.Activated);
                }
            }
        }

        private void StopAllLights()
        {
            for (int i = 0; i < _fans.Count; i++)
            {
                _fans[i].ChangeState(FanState.Idle);
            }
        }

        private void Update()
        {
            if (_isPowerOn)
            {
                _motionElapsedTime += Time.deltaTime;
            }
            if (!_isRotating)
            {
                return;
            }
            Rotate();
        }

        private void Rotate()
        {
            // Rule: spd = a * sin(omega * t) + b. The rule specifies rad/s,
            // while ObjectRotator consumes degrees/s.
            float speedRadiansPerSecond = _rotationMode == EnergyRotationMode.Uniform || !_isPowerOn
                ? Mathf.PI / 3f
                : _speedAmplitude * Mathf.Sin(_angularFrequency * _motionElapsedTime) + _speedBias;
            _curSpeed = speedRadiansPerSecond * Mathf.Rad2Deg;
            _rotator.SetRotation(transform.up, _curSpeed, ObjectRotator.RotationDirection.CounterClockwise);
        }

        private void ResetMotionProfile()
        {
            float amplitudeMin = _fanControllerConfig != null ? _fanControllerConfig.amplitudeMin : 0.780f;
            float amplitudeMax = _fanControllerConfig != null ? _fanControllerConfig.amplitudeMax : 1.045f;
            float frequencyMin = _fanControllerConfig != null ? _fanControllerConfig.angularFrequencyMin : 1.884f;
            float frequencyMax = _fanControllerConfig != null ? _fanControllerConfig.angularFrequencyMax : 2.000f;
            _speedAmplitude = Random.Range(amplitudeMin, amplitudeMax);
            _angularFrequency = Random.Range(frequencyMin, frequencyMax);
            _speedBias = 2.090f - _speedAmplitude;
            _motionElapsedTime = 0f;
        }

        private void StopRotate()
        {
            _curSpeed = 0f;
            _rotator.SetRotation(transform.up, 0, ObjectRotator.RotationDirection.Clockwise);
        }

        private void FindFans()
        {
            foreach (Transform fanTransform in transform)
            {
                if (fanTransform.name.StartsWith("fan"))
                {
                    _fans.Add(_index, new Fan(fanTransform, _fanConfig));
                    _index += 1;
                }
            }
        }
    }
    
    public enum FanState
    {
        Idle, Activated, NotActive
    }
    public class Fan
    {
        private Dictionary<int, Transform> _lights;
        private int _index = 0;
        private FanState _curState;
        private FanSO _fanConfig;
        private CriticalPoints _criticalPoints;
        private Transform _root;

        public Transform Root => _root;
        public FanState CurrentState => _curState;
        public bool IsHittable => _curState == FanState.NotActive;

        public void Init(Transform parent, FanSO config)
        {
            _root = parent;
            _lights = new Dictionary<int, Transform>();
            foreach (Transform child in parent)
            {
                if(child.gameObject.CompareTag("CriticalPoints"))continue;
                _lights[_index] = child;
                _index += 1;
            }

            _fanConfig = config;

            _curState = FanState.Idle;
            _criticalPoints = parent.GetComponentInChildren<CriticalPoints>();
            _criticalPoints.Tag = 1;
            OnStateChange(FanState.Idle);
        }

        public void ChangeState(FanState nextState)
        {
            if (_curState == nextState) return;
            _curState = nextState;
            OnStateChange(nextState);
        }

        private void OnStateChange(FanState nextState)
        {
            TurnOffAllLights();
            List<int> turnOnLights = _fanConfig.GetLights(nextState);
            if (turnOnLights.Count == 0) return;
            turnOnLights.Sort();
            foreach (var key in turnOnLights)
            {
                _lights[key].gameObject.SetActive(true);
            }

            _criticalPoints.Tag = nextState == FanState.NotActive ? 1 : 2;
        }

        private void TurnOffAllLights()
        {
            for (int i = 0; i < _lights.Count; i++)
            {
                _lights[i].gameObject.SetActive(false);
            }
        }

        public Fan(){}

        public Fan(Transform fanTransform, FanSO fanConfig)
        {
            Init(fanTransform, fanConfig);
        }
    }
}
