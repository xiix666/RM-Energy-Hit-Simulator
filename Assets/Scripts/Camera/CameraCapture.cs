using System.Collections.Generic;
using Label;
using UnityEngine;

namespace Robo.Cam
{
    // Serialized target references used by EnergyCameraStream. The private
    // offline capture and ground-truth export workflow is not published.
    [RequireComponent(typeof(Camera))]
    public class CameraCapture : MonoBehaviour
    {
        public List<CriticalPoints> targetObjects = new List<CriticalPoints>();
    }
}
