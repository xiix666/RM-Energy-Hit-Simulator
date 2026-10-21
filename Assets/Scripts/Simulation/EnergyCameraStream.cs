using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Label;
using Robo.Cam;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Simulation.EnergyHit
{
    // Best-effort loopback UDP transport for the newest timestamped camera frame.
    // No Unity objects are accessed on the networking thread.
    internal sealed class EnergyCameraStream : MonoBehaviour
    {
        [Serializable]
        private sealed class TargetLabel
        {
            public string id;
            public int class_id;
            public bool hittable;
            public float[] pixels;
            public float[] hit_offset_object_m;
        }

        [Serializable]
        private sealed class FrameHeader
        {
            public long stamp_ns;
            public long seq;
            public int width;
            public int height;
            public float vertical_fov_deg;
            public float yaw_rad;
            public float pitch_rad;
            public float initial_yaw_world_rad;
            public float initial_camera_elevation_rad;
            public float[] muzzle_translation_camera_optical_m;
            public float muzzle_speed_mps;
            public float capture_duration_ms;
            public float unity_fps;
            public TargetLabel[] labels;
        }

        private sealed class RawFrame
        {
            public byte[] pixels;
            public byte[] metadata;
            public int width;
            public int height;
            public long sequence;
        }

        private const int Port = 15001;
        private const int ChunkBytes = 60000;
        private const int ChunkHeaderBytes = 20;
        // Publish at the native capture resolution so contestants can locate
        // small target features accurately at long range.
        private const int StreamWidth = 1920;
        private const int StreamHeight = 1080;
        // Upper bound of 30 Hz; actual rate depends on Unity frame/render time.
        private const float CaptureInterval = 1f / 30f;
        private static bool _created;
        private readonly object _frameLock = new object();
        private readonly object _rawFrameLock = new object();
        private readonly AutoResetEvent _rawFrameReady = new AutoResetEvent(false);
        private readonly Queue<byte[]> _availableRawBuffers = new Queue<byte[]>();
        private RawFrame _pendingRawFrame;
        private Thread _encoderThread;
        private readonly AutoResetEvent _frameReady = new AutoResetEvent(false);
        private UdpClient _socket;
        private IPEndPoint _receiver;
        private Thread _senderThread;
        private byte[] _latestPacket;
        private long _latestSequence;
        private bool _running;
        private volatile bool _oracleEnabled;
        private DateTime _lastHelloUtc;
        private long _sequence;
        private Camera _camera;
        private CameraCapture _capture;
        private GimbalRigController _gimbal;
        private ProjectileLauncher _launcher;
        private RenderTexture _texture;
        private Texture2D _pixels;
        private float _smoothedUnityFps;

        public static long PublishedFrames { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _created = false;
            PublishedFrames = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateStream()
        {
            if (_created) return;
            _created = true;
            GameObject holder = new GameObject("EnergyCameraStream");
            holder.hideFlags = HideFlags.HideInHierarchy;
            DontDestroyOnLoad(holder);
            holder.AddComponent<EnergyCameraStream>();
        }

        private void Awake()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            try
            {
                _socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, Port));
                _socket.Client.ReceiveBufferSize = 4 * 1024 * 1024;
                _sequence = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L;
                _running = true;
                lock (_rawFrameLock)
                {
                    _availableRawBuffers.Enqueue(new byte[StreamWidth * StreamHeight * 3]);
                    _availableRawBuffers.Enqueue(new byte[StreamWidth * StreamHeight * 3]);
                }
                _encoderThread = new Thread(EncodeLoop) { IsBackground = true, Name = "EnergyCameraJpeg" };
                _encoderThread.Start();
                _senderThread = new Thread(SendLoop) { IsBackground = true, Name = "EnergyCameraUdp" };
                _senderThread.Start();
                StartCoroutine(CaptureLoop());
                Debug.Log($"UDP camera stream listening on 127.0.0.1:{Port}.", this);
            }
            catch (Exception error)
            {
                Debug.LogError($"Camera stream could not start: {error.Message}", this);
            }
        }

        private IEnumerator CaptureLoop()
        {
            float nextCapture = 0f;
            while (_running)
            {
                yield return new WaitForEndOfFrame();
                float captureNow = Time.realtimeSinceStartup;
                if (captureNow + 0.001f < nextCapture) continue;
                nextCapture = Mathf.Max(nextCapture + CaptureInterval, captureNow + CaptureInterval * 0.5f);
                float currentFps = 1f / Mathf.Max(Time.unscaledDeltaTime, 0.001f);
                _smoothedUnityFps = _smoothedUnityFps <= 0f
                    ? currentFps : Mathf.Lerp(_smoothedUnityFps, currentFps, 0.15f);

                if (_camera == null)
                {
                    _camera = Camera.main;
                    _capture = _camera != null ? _camera.GetComponent<CameraCapture>() : null;
                    _gimbal = FindObjectOfType<GimbalRigController>();
                    _launcher = FindObjectOfType<ProjectileLauncher>();
                }
                if (_camera == null || _capture == null) continue;

                int width = StreamWidth;
                int height = StreamHeight;
                if (width <= 0 || height <= 0) continue;
                EnsureTextures(width, height);
                _camera.aspect = (float)width / height;

                FrameHeader header = new FrameHeader
                {
                    stamp_ns = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000000L,
                    seq = ++_sequence,
                    width = width,
                    height = height,
                    vertical_fov_deg = _camera.fieldOfView,
                    yaw_rad = _gimbal != null ? _gimbal.Yaw * Mathf.Deg2Rad : 0f,
                    pitch_rad = _gimbal != null ? _gimbal.Pitch * Mathf.Deg2Rad : 0f,
                    initial_yaw_world_rad = _gimbal != null ? _gimbal.InitialYawWorldRad : 0f,
                    initial_camera_elevation_rad = _gimbal != null ? _gimbal.InitialCameraElevationRad : 0f,
                    muzzle_translation_camera_optical_m = GetMuzzleTranslation(),
                    muzzle_speed_mps = _launcher != null ? _launcher.MuzzleVelocity : 20f,
                    unity_fps = _smoothedUnityFps,
                    labels = _oracleEnabled ? CollectLabels(width, height) : new TargetLabel[0]
                };

                byte[] rawBuffer;
                lock (_rawFrameLock)
                {
                    if (_availableRawBuffers.Count == 0) continue;
                    rawBuffer = _availableRawBuffers.Dequeue();
                }

                RenderTexture previousTarget = _camera.targetTexture;
                RenderTexture previousActive = RenderTexture.active;
                bool queuedForEncoding = false;
                try
                {
                    float captureStarted = Time.realtimeSinceStartup;
                    _camera.targetTexture = _texture;
                    _camera.Render();
                    RenderTexture.active = _texture;
                    // Read only: Apply would upload the same 1920x1080 image back to the GPU.
                    _pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                    _pixels.GetRawTextureData<byte>().CopyTo(rawBuffer);
                    header.capture_duration_ms = (Time.realtimeSinceStartup - captureStarted) * 1000f;
                    byte[] metadata = Encoding.UTF8.GetBytes(JsonUtility.ToJson(header));
                    lock (_rawFrameLock)
                    {
                        _pendingRawFrame = new RawFrame
                        {
                            pixels = rawBuffer, metadata = metadata, width = width,
                            height = height, sequence = header.seq
                        };
                        queuedForEncoding = true;
                    }
                    _rawFrameReady.Set();
                }
                finally
                {
                    _camera.targetTexture = previousTarget;
                    RenderTexture.active = previousActive;
                    if (!queuedForEncoding)
                    {
                        lock (_rawFrameLock) _availableRawBuffers.Enqueue(rawBuffer);
                    }
                }
            }
        }

        private TargetLabel[] CollectLabels(int width, int height)
        {
            List<TargetLabel> labels = new List<TargetLabel>();
            foreach (CriticalPoints points in _capture.targetObjects)
            {
                if (points == null || points.Points == null) continue;
                EnergyHitTarget hitTarget = points.GetComponentInChildren<EnergyHitTarget>(true);
                if (hitTarget == null) continue;

                Transform[] keypoints = new Transform[5];
                foreach (Transform point in points.Points)
                {
                    int number;
                    if (point != null && int.TryParse(point.name, out number) && number >= 1 && number <= 5)
                        keypoints[number - 1] = point;
                }
                if (Array.Exists(keypoints, point => point == null)) continue;
                if (Vector3.Dot(keypoints[0].forward, _camera.transform.forward) >= 0f) continue;

                float[] pixels = new float[10];
                bool allVisible = true;
                Vector3 center = Vector3.zero;
                for (int i = 0; i < 5; i++)
                {
                    center += keypoints[i].position;
                    Vector3 viewport = _camera.WorldToViewportPoint(keypoints[i].position);
                    if (viewport.z <= 0f || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f)
                    {
                        allVisible = false;
                        break;
                    }
                    pixels[2 * i] = viewport.x * width;
                    pixels[2 * i + 1] = (1f - viewport.y) * height;
                }
                if (!allVisible) continue;

                // Physical offset in the public five-point model coordinate system.
                center /= 5f;
                Vector3 xAxis = (keypoints[1].position - keypoints[0].position).normalized;
                Vector3 yGuess = (keypoints[0].position + keypoints[1].position) * 0.5f - keypoints[3].position;
                Vector3 yAxis = (yGuess - Vector3.Dot(yGuess, xAxis) * xAxis).normalized;
                Vector3 zAxis = Vector3.Cross(xAxis, yAxis);
                Vector3 offset = hitTarget.TargetTransform.position - center;
                labels.Add(new TargetLabel
                {
                    id = hitTarget.GetInstanceID().ToString(),
                    class_id = points.Color * 3 + points.Tag,
                    hittable = hitTarget.IsCurrentlyHittable,
                    pixels = pixels,
                    hit_offset_object_m = new[]
                    {
                        Vector3.Dot(offset, xAxis), Vector3.Dot(offset, yAxis), Vector3.Dot(offset, zAxis)
                    },
                });
            }
            return labels.ToArray();
        }

        private float[] GetMuzzleTranslation()
        {
            if (_launcher == null || _launcher.MuzzleTransform == null)
                return new[] { 0f, 0f, 0f };
            Vector3 local = _camera.transform.InverseTransformPoint(_launcher.MuzzleTransform.position);
            return new[] { local.x, -local.y, local.z };
        }

        private void EnsureTextures(int width, int height)
        {
            if (_texture != null && _texture.width == width && _texture.height == height) return;
            if (_texture != null) Destroy(_texture);
            if (_pixels != null) Destroy(_pixels);
            _texture = new RenderTexture(width, height, 24);
            _pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
        }

        // Encode managed RGB bytes on a worker thread; EncodeArrayToJPG is thread-safe.
        private void EncodeLoop()
        {
            while (_running)
            {
                _rawFrameReady.WaitOne(50);
                RawFrame frame;
                lock (_rawFrameLock)
                {
                    frame = _pendingRawFrame;
                    _pendingRawFrame = null;
                }
                if (frame == null) continue;
                try
                {
                    byte[] jpeg = ImageConversion.EncodeArrayToJPG(
                        frame.pixels, GraphicsFormat.R8G8B8_UNorm,
                        (uint)frame.width, (uint)frame.height, 0, 95);
                    if (!_running) continue;
                    using (MemoryStream stream = new MemoryStream(8 + frame.metadata.Length + jpeg.Length))
                    using (BinaryWriter writer = new BinaryWriter(stream))
                    {
                        writer.Write(frame.metadata.Length);
                        writer.Write(frame.metadata);
                        writer.Write(jpeg.Length);
                        writer.Write(jpeg);
                        lock (_frameLock)
                        {
                            _latestPacket = stream.ToArray();
                            _latestSequence = frame.sequence;
                        }
                    }
                    _frameReady.Set();
                }
                catch (Exception error)
                {
                    if (_running) Debug.LogWarning($"JPEG encoding failed: {error.Message}");
                }
                finally
                {
                    lock (_rawFrameLock) _availableRawBuffers.Enqueue(frame.pixels);
                }
            }
        }

        private void SendLoop()
        {
            while (_running)
            {
                try
                {
                    ReceiveHello();
                    if (_receiver == null || (DateTime.UtcNow - _lastHelloUtc).TotalSeconds > 3.0)
                    {
                        _receiver = null;
                        _oracleEnabled = false;
                        _frameReady.WaitOne(20);
                        continue;
                    }
                    if (!_frameReady.WaitOne(10)) continue;
                    byte[] frame;
                    long sequence;
                    lock (_frameLock)
                    {
                        frame = _latestPacket;
                        sequence = _latestSequence;
                        _latestPacket = null;
                    }
                    if (frame == null) continue;
                    SendFrame(frame, sequence, _receiver);
                    PublishedFrames++;
                }
                catch (Exception error)
                {
                    if (_running)
                    {
                        Debug.LogWarning($"UDP camera send failed: {error.Message}");
                        Thread.Sleep(200);
                    }
                }
            }
        }

        private void ReceiveHello()
        {
            while (_socket != null && _socket.Available > 0)
            {
                IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
                byte[] hello = _socket.Receive(ref sender);
                if (!sender.Address.Equals(IPAddress.Loopback) || hello.Length != 5 ||
                    hello[0] != (byte)'R' || hello[1] != (byte)'M' ||
                    hello[2] != (byte)'H' || hello[3] != (byte)'1') continue;
                _receiver = sender;
                _oracleEnabled = hello[4] == 1;
                _lastHelloUtc = DateTime.UtcNow;
            }
        }

        private void SendFrame(byte[] frame, long sequence, IPEndPoint receiver)
        {
            int count = (frame.Length + ChunkBytes - 1) / ChunkBytes;
            if (count == 0 || count > ushort.MaxValue) return;
            for (int index = 0; index < count; index++)
            {
                int offset = index * ChunkBytes;
                int length = Math.Min(ChunkBytes, frame.Length - offset);
                byte[] datagram = new byte[ChunkHeaderBytes + length];
                datagram[0] = (byte)'R';
                datagram[1] = (byte)'M';
                datagram[2] = (byte)'F';
                datagram[3] = (byte)'1';
                Buffer.BlockCopy(BitConverter.GetBytes(sequence), 0, datagram, 4, 8);
                Buffer.BlockCopy(BitConverter.GetBytes((ushort)index), 0, datagram, 12, 2);
                Buffer.BlockCopy(BitConverter.GetBytes((ushort)count), 0, datagram, 14, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(frame.Length), 0, datagram, 16, 4);
                Buffer.BlockCopy(frame, offset, datagram, ChunkHeaderBytes, length);
                _socket.Send(datagram, datagram.Length, receiver);
            }
        }

        private void OnDestroy()
        {
            _running = false;
            _rawFrameReady.Set();
            _frameReady.Set();
            _socket?.Close();
            _encoderThread?.Join(1000);
            _senderThread?.Join(1000);
            if (_texture != null) Destroy(_texture);
            if (_pixels != null) Destroy(_pixels);
            _rawFrameReady.Dispose();
            _frameReady.Dispose();
        }
    }
}
