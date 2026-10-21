using System;
using Flower;
using System.Collections.Generic;
using UnityEngine;

namespace Controller
{
    public class PowerRuneController : MonoBehaviour
    {
        private static PowerRuneController _activeInstance;
        private readonly List<Material> _centerLogoMaterials = new List<Material>();

        [SerializeField] private FanController _red;
        [SerializeField] private FanController _blue;
        [SerializeField] private int _controlMode = 0x01;
        [Header("Simulation startup")]
        [SerializeField] private bool _autoStart = true;
        [SerializeField] private bool _autoStartWithRedSide = true;
        private EnergyRotationMode _rotationMode = EnergyRotationMode.Periodic;
        private int red = 0x01;
        private int blue = 0x10;

        public EnergyRotationMode RotationMode => _rotationMode;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetActiveInstance()
        {
            _activeInstance = null;
        }

        private void Awake()
        {
            if (_activeInstance != null && _activeInstance != this)
            {
                Debug.LogWarning("Duplicate PowerRuneController disabled. Check additive scene loading.", this);
                gameObject.SetActive(false);
                return;
            }

            _activeInstance = this;
            ColourCenterLogos();
        }

        private void ColourCenterLogos()
        {
            // The visible R is Logo_Base_A/B in the original Power Rune model.
            // The new fan model's "robomaster标志" is a backing disk, NOT the R.
            // Keep the original disk black; render the extracted R outline in
            // front of the fan's backing plate. No collider is added to the R.
            Material template = Resources.Load<Material>("EnergyRuneCenterLogo");
            Material redLetter = Resources.Load<Material>("EnergyRuneRedR");
            Mesh letterMesh = Resources.Load<Mesh>("EnergyRuneExactR");
            Mesh diskMesh = Resources.Load<Mesh>("EnergyRuneCenterDisk");
            if (template == null || redLetter == null || letterMesh == null || diskMesh == null)
            {
                Debug.LogError("Cannot colour the centre R logo: centre logo resources are missing.", this);
                return;
            }

            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name != "Logo_Base_A" && renderer.name != "Logo_Base_B"
                    && renderer.name != "robomaster标志") continue;
                Material material = new Material(template) { name = "EnergyRune_CenterDisk_Black" };
                _centerLogoMaterials.Add(material);
                Material[] slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++) slots[i] = material;
                renderer.sharedMaterials = slots;
                if (renderer.name == "robomaster标志") continue;
                var disk = new GameObject("EnergyRune_CenterDisk_Black");
                disk.transform.SetParent(renderer.transform, false);
                disk.layer = renderer.gameObject.layer;
                disk.AddComponent<MeshFilter>().sharedMesh = diskMesh;
                MeshRenderer diskRenderer = disk.AddComponent<MeshRenderer>();
                diskRenderer.sharedMaterial = material;
                diskRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                diskRenderer.receiveShadows = false;
                var letter = new GameObject("EnergyRune_CenterR_Red");
                letter.transform.SetParent(renderer.transform, false);
                letter.layer = renderer.gameObject.layer;
                letter.AddComponent<MeshFilter>().sharedMesh = letterMesh;
                MeshRenderer letterRenderer = letter.AddComponent<MeshRenderer>();
                letterRenderer.sharedMaterial = redLetter;
                letterRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                letterRenderer.receiveShadows = false;
            }
        }

        private void Start()
        {
            if (!_autoStart)
            {
                return;
            }

            // Only one colour side may be powered at a time. Each powered side
            // contains exactly two hittable illuminated fan targets.
            ActivateSide(_autoStartWithRedSide);
        }

        private void Update()
        {
            bool isRotate = MyGameEntry.Input.GetBool("Rotate");
            bool isTogglePowerOn = MyGameEntry.Input.GetBool("TogglePowerOn");
            bool isChangeMode = MyGameEntry.Input.GetBool("ChangeMode");

            if (MyGameEntry.Input.GetBool("ControlModeRed")) ActivateSide(true);
            if (MyGameEntry.Input.GetBool("ControlModeBlue")) ActivateSide(false);
            if (MyGameEntry.Input.GetBool("ControlModeBoth")) ActivateSide(true);

            int isRed = _controlMode & red;
            int isBlue = _controlMode & blue;
            if (isRotate)
            {
                CycleRotationMode();
            }

            if (isTogglePowerOn)
            {
                ToggleSelectedPower(isRed > 0, isBlue > 0);
            }
            
            if (isChangeMode)
            {
                ChangeSelectedPattern(isRed > 0, isBlue > 0);
            }
        }

        private void ToggleSelectedPower(bool selectRed, bool selectBlue)
        {
            if (selectRed && !selectBlue)
            {
                bool turnOn = !_red.IsPowerOn;
                _blue.SetPowerOn(false);
                _red.SetPowerOn(turnOn);
                return;
            }

            if (selectBlue && !selectRed)
            {
                bool turnOn = !_blue.IsPowerOn;
                _red.SetPowerOn(false);
                _blue.SetPowerOn(turnOn);
                return;
            }

            // "Both" still respects the global maximum of two illuminated
            // targets: turn off the active side, or start the red side.
            if (_red.IsPowerOn || _blue.IsPowerOn)
            {
                _red.SetPowerOn(false);
                _blue.SetPowerOn(false);
            }
            else
            {
                _red.SetPowerOn(true);
            }
        }

        private void ActivateSide(bool useRedSide)
        {
            FanController activeController = useRedSide ? _red : _blue;
            FanController inactiveController = useRedSide ? _blue : _red;

            // The red and blue objects are complete overlapping variants of
            // the same physical mechanism. Keep exactly one variant active.
            inactiveController.SetPowerOn(false);
            inactiveController.SetRotating(false);
            inactiveController.gameObject.SetActive(false);

            activeController.gameObject.SetActive(true);
            activeController.SetRotationMode(_rotationMode);
            activeController.SetPowerOn(true);
            activeController.SetRotating(_rotationMode != EnergyRotationMode.Stopped);
            _controlMode = useRedSide ? red : blue;
        }

        private void CycleRotationMode()
        {
            _rotationMode = (EnergyRotationMode)(((int)_rotationMode + 1) % 3);
            if (_red.gameObject.activeInHierarchy)
            {
                _red.SetRotationMode(_rotationMode);
            }
            else if (_blue.gameObject.activeInHierarchy)
            {
                _blue.SetRotationMode(_rotationMode);
            }
        }

        private void ChangeSelectedPattern(bool selectRed, bool selectBlue)
        {
            if (selectRed && !selectBlue)
            {
                _blue.SetPowerOn(false);
                _red.ChangeFanLight();
                return;
            }

            if (selectBlue && !selectRed)
            {
                _red.SetPowerOn(false);
                _blue.ChangeFanLight();
                return;
            }

            // In "Both" mode, update only the powered side so that no more
            // than two hittable targets can be illuminated globally.
            if (_blue.IsPowerOn)
            {
                _blue.ChangeFanLight();
            }
            else
            {
                _red.ChangeFanLight();
            }
        }

        private void OnDestroy()
        {
            foreach (Material material in _centerLogoMaterials)
            {
                if (material != null) Destroy(material);
            }
            _centerLogoMaterials.Clear();
            if (_activeInstance == this)
            {
                _activeInstance = null;
            }
        }
    }
}
