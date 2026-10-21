using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace Simulation.EnergyHit
{
    /// <summary>
    /// Receives ROS 2 gimbal commands through a small localhost UDP relay.
    /// ROS remains the public API; UDP only removes the need for a version-
    /// sensitive ROS package inside this Unity 2021 project.
    /// </summary>
    [DefaultExecutionOrder(100)]
    internal sealed class EnergyHitRos2UdpBridge : MonoBehaviour
    {
        [Serializable]
        private sealed class WireCommand
        {
            public string session_id;
            public long seq;
            public double yaw_rad;
            public double pitch_rad;
            public bool fire;
        }

        private const int DefaultPort = 15000;
        private const float CommandTimeoutSeconds = 1f;
        private static bool _created;

        private UdpClient _udpClient;
        private GimbalRigController _gimbal;
        private ProjectileLauncher _launcher;
        private string _sessionId = string.Empty;
        private long _lastSequence = -1;
        private bool _lastFireState;
        private float _lastPacketTime = float.NegativeInfinity;
        private float _nextReferenceSearchTime;
        private float _rateWindowStart;
        private int _receivedInWindow;

        public static EnergyHitRos2UdpBridge Instance { get; private set; }
        public bool IsConnected => _udpClient != null && PacketAgeSeconds <= CommandTimeoutSeconds;
        public float PacketAgeSeconds => Time.realtimeSinceStartup - _lastPacketTime;
        public float ReceivedHz { get; private set; }
        public long LastSequence => _lastSequence;
        public float CommandYawDegrees { get; private set; }
        public float CommandPitchDegrees { get; private set; }
        public bool CommandFire { get; private set; }
        public bool LastFireAccepted { get; private set; }
        public string LastError { get; private set; } = string.Empty;
        public float ActualYawDegrees => _gimbal != null ? _gimbal.Yaw : 0f;
        public float ActualPitchDegrees => _gimbal != null ? _gimbal.Pitch : 0f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _created = false;
            Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateBridge()
        {
            if (_created)
            {
                return;
            }

            _created = true;
            GameObject bridgeObject = new GameObject("EnergyHitRos2UdpBridge");
            bridgeObject.hideFlags = HideFlags.HideInHierarchy;
            DontDestroyOnLoad(bridgeObject);
            bridgeObject.AddComponent<EnergyHitRos2UdpBridge>();
        }

        private void Awake()
        {
            Instance = this;
            Application.runInBackground = true;
            TryOpenSocket();
            FindRuntimeReferences();
        }

        private void Update()
        {
            if (Time.unscaledTime >= _nextReferenceSearchTime &&
                (_gimbal == null || _launcher == null))
            {
                FindRuntimeReferences();
                _nextReferenceSearchTime = Time.unscaledTime + 1f;
            }

            ReceiveAvailableCommands();
            float now = Time.realtimeSinceStartup;
            float windowSeconds = now - _rateWindowStart;
            if (windowSeconds >= 1f)
            {
                ReceivedHz = _receivedInWindow / windowSeconds;
                _receivedInWindow = 0;
                _rateWindowStart = now;
            }

            if (!IsConnected && _gimbal != null && _gimbal.ExternalControlEnabled)
            {
                _gimbal.ReleaseExternalControl();
                _lastFireState = false;
                CommandFire = false;
            }
        }

        private void TryOpenSocket()
        {
            try
            {
                _udpClient = new UdpClient(new IPEndPoint(IPAddress.Loopback, DefaultPort));
                _udpClient.Client.Blocking = false;
                LastError = string.Empty;
                Debug.Log($"ROS 2 UDP bridge listening on 127.0.0.1:{DefaultPort}.", this);
            }
            catch (Exception exception)
            {
                LastError = exception.Message;
                Debug.LogError($"Could not open ROS 2 UDP bridge: {exception.Message}", this);
            }
        }

        private void ReceiveAvailableCommands()
        {
            if (_udpClient == null)
            {
                return;
            }

            bool requestFire = false;
            try
            {
                while (_udpClient.Available > 0)
                {
                    IPEndPoint sender = null;
                    byte[] bytes = _udpClient.Receive(ref sender);
                    WireCommand command = JsonUtility.FromJson<WireCommand>(Encoding.UTF8.GetString(bytes));
                    if (command == null || !IsFinite(command.yaw_rad) || !IsFinite(command.pitch_rad))
                    {
                        continue;
                    }

                    if (command.session_id != _sessionId)
                    {
                        _sessionId = command.session_id ?? string.Empty;
                        _lastSequence = -1;
                        _lastFireState = false;
                    }
                    if (command.seq <= _lastSequence)
                    {
                        continue;
                    }

                    _lastSequence = command.seq;
                    _lastPacketTime = Time.realtimeSinceStartup;
                    _receivedInWindow++;
                    CommandYawDegrees = (float)(command.yaw_rad * Mathf.Rad2Deg);
                    CommandPitchDegrees = (float)(command.pitch_rad * Mathf.Rad2Deg);
                    requestFire |= command.fire && !_lastFireState;
                    _lastFireState = command.fire;
                    CommandFire = command.fire;
                }
            }
            catch (SocketException exception)
            {
                if (exception.SocketErrorCode != SocketError.WouldBlock)
                {
                    LastError = exception.Message;
                }
            }
            catch (Exception exception)
            {
                LastError = exception.Message;
                Debug.LogWarning($"Invalid ROS 2 UDP command: {exception.Message}", this);
            }

            if (_lastSequence >= 0 && IsConnected && _gimbal != null)
            {
                _gimbal.SetExternalAngles(CommandYawDegrees, CommandPitchDegrees);
            }

            if (requestFire)
            {
                LastFireAccepted = _launcher != null && _launcher.Fire();
                EnergyHitStatistics.RecordRosFireRequest(LastFireAccepted);
            }
        }

        private void FindRuntimeReferences()
        {
            if (_gimbal == null)
            {
                _gimbal = FindObjectOfType<GimbalRigController>();
            }
            if (_launcher == null)
            {
                _launcher = FindObjectOfType<ProjectileLauncher>();
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private void OnDestroy()
        {
            if (_gimbal != null)
            {
                _gimbal.ReleaseExternalControl();
            }
            _udpClient?.Close();
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
