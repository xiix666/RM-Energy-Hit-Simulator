using UnityEngine;

namespace ScriptableObjects
{
    [CreateAssetMenu(fileName = "FanControllerConfig", menuName = "SO_Config/FanControllerConfig", order = 0)]
    public class FanControllerSO : ScriptableObject
    {
        [Header("RoboMaster 2026 large energy mechanism")]
        [Tooltip("Minimum a in rad/s.")]
        public float amplitudeMin = 0.780f;
        [Tooltip("Maximum a in rad/s.")]
        public float amplitudeMax = 1.045f;
        [Tooltip("Minimum omega in rad/s.")]
        public float angularFrequencyMin = 1.884f;
        [Tooltip("Maximum omega in rad/s.")]
        public float angularFrequencyMax = 2.000f;

        private void OnValidate()
        {
            amplitudeMin = Mathf.Clamp(amplitudeMin, 0.780f, 1.045f);
            amplitudeMax = Mathf.Clamp(amplitudeMax, amplitudeMin, 1.045f);
            angularFrequencyMin = Mathf.Clamp(angularFrequencyMin, 1.884f, 2.000f);
            angularFrequencyMax = Mathf.Clamp(angularFrequencyMax, angularFrequencyMin, 2.000f);
        }
    }
}
