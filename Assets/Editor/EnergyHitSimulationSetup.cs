using System.Collections.Generic;
using Controller;
using Label;
using Robo.Cam;
using Simulation.EnergyHit;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityScene = UnityEngine.SceneManagement.Scene;

namespace Simulation.Editor
{
    /// <summary>
    /// Idempotent editor setup for the gimbal/projectile/energy-target simulation.
    /// </summary>
    public static class EnergyHitSimulationSetup
    {
        private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";
        private const string InitScenePath = "Assets/Scenes/Init.unity";
        private const string RigName = "EnergyHitSimulation_GimbalYaw";
        private const string PitchName = "GimbalPitch";
        private const string CameraMountName = "CameraMount";
        private const string MuzzleName = "Muzzle";
        private const string TargetName = "EnergyHitTarget_Collider";
        private const float DefaultCameraHeight = 0.1f;
        private const float DesiredCameraRange = 6.5f;
        private const float DesiredCameraWorldHeight = 0.96f;
        private const float EnvironmentLightIntensity = 1.2f;
        private const float EnvironmentAmbientIntensity = 0.65f;
        private const float TargetThickness = 0.3f;
        private const float TargetMargin = 0.05f;
        private const float EffectiveDetectionDiameter = 0.3f;

        [MenuItem("Tools/RM Energy Hit Simulator/Setup Energy Hit Simulation")]
        public static void SetupEnergyHitSimulation()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Energy Hit Simulation",
                    "Please stop Play Mode before changing the scene.", "OK");
                return;
            }

            UnityScene scene = OpenSampleSceneSafely();
            if (!scene.IsValid())
            {
                return;
            }

            Camera mainCamera = FindMainCamera(scene);
            List<FanController> fanControllers = FindFanControllers(scene);
            List<CriticalPoints> fanTargets = FindFanTargets(fanControllers);

            if (mainCamera == null || fanControllers.Count == 0 || fanTargets.Count == 0)
            {
                EditorUtility.DisplayDialog("Energy Hit Simulation",
                    "Setup failed. Main Camera, FanController, or fan CriticalPoints could not be found.",
                    "OK");
                return;
            }

            Undo.SetCurrentGroupName("Setup Energy Hit Simulation");
            int undoGroup = Undo.GetCurrentGroup();

            Vector3 targetCenter = CalculateTargetCenter(fanTargets);
            SetupGimbal(scene, mainCamera, targetCenter);
            int targetCount = SetupHitTargets(fanControllers);
            ConfigurePowerRuneStartup(scene);
            ConfigureEnvironmentLighting(scene);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Undo.CollapseUndoOperations(undoGroup);

            Debug.Log($"Energy hit simulation setup complete: 1 gimbal, {targetCount} hit targets.");
            // Leave the editor in the only safe pre-Play state. EntryProcedure
            // will load SampleScene additively exactly once at runtime.
            EditorSceneManager.OpenScene(InitScenePath, OpenSceneMode.Single);
            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("Energy Hit Simulation",
                    $"Setup complete. Created/updated one gimbal and {targetCount} hit targets.\n\n" +
                    "Init.unity is now the only open scene. Press Play to run the project.",
                    "OK");
            }
        }

        [MenuItem("Tools/RM Energy Hit Simulator/Validate Energy Hit Simulation")]
        public static void ValidateEnergyHitSimulation()
        {
            UnityScene scene = SceneManager.GetActiveScene();
            GameObject rig = FindSceneObject(scene, RigName);
            EnergyHitTarget[] allTargets = Resources.FindObjectsOfTypeAll<EnergyHitTarget>();
            int sceneTargetCount = 0;

            foreach (EnergyHitTarget target in allTargets)
            {
                if (target.gameObject.scene == scene)
                {
                    sceneTargetCount++;
                }
            }

            Camera mainCamera = FindMainCamera(scene);
            bool cameraReady = mainCamera != null &&
                               mainCamera.GetComponent<FPCamera>() != null &&
                               !mainCamera.GetComponent<FPCamera>().enabled;
            bool ready = rig != null && cameraReady && sceneTargetCount >= 10;

            string message =
                $"Scene: {scene.path}\n" +
                $"Gimbal rig: {(rig != null ? "OK" : "MISSING")}\n" +
                $"Old FPCamera disabled: {(cameraReady ? "OK" : "CHECK")}\n" +
                $"Hit targets: {sceneTargetCount} (expected 10)\n\n" +
                (ready ? "The scene wiring is ready." : "Run Setup Energy Hit Simulation again.");

            Debug.Log(message);
            EditorUtility.DisplayDialog("Energy Hit Simulation Validation", message, "OK");
        }

        private static UnityScene OpenSampleSceneSafely()
        {
            UnityScene activeScene = SceneManager.GetActiveScene();
            if (Application.isBatchMode)
            {
                return activeScene.path == SampleScenePath
                    ? activeScene
                    : EditorSceneManager.OpenScene(SampleScenePath, OpenSceneMode.Single);
            }

            if (activeScene.path == SampleScenePath)
            {
                if (activeScene.isDirty && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    return default;
                }

                return activeScene;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return default;
            }

            return EditorSceneManager.OpenScene(SampleScenePath, OpenSceneMode.Single);
        }

        private static GameObject SetupGimbal(UnityScene scene, Camera camera, Vector3 targetCenter)
        {
            GameObject yawObject = FindSceneObject(scene, RigName);
            if (yawObject == null)
            {
                yawObject = new GameObject(RigName);
                Undo.RegisterCreatedObjectUndo(yawObject, "Create gimbal yaw axis");
                SceneManager.MoveGameObjectToScene(yawObject, scene);
            }

            Transform pitch = FindOrCreateChild(yawObject.transform, PitchName);
            Transform cameraMount = FindOrCreateChild(pitch, CameraMountName);
            Transform muzzle = FindOrCreateChild(pitch, MuzzleName);

            Vector3 awayFromTarget = yawObject.transform.position - targetCenter;
            awayFromTarget.y = 0f;
            if (awayFromTarget.sqrMagnitude < 0.01f)
            {
                awayFromTarget = camera.transform.position - targetCenter;
                awayFromTarget.y = 0f;
            }
            if (awayFromTarget.sqrMagnitude < 0.01f) awayFromTarget = Vector3.back;
            awayFromTarget.Normalize();

            // Keep the existing viewing azimuth, but move the complete rig
            // backwards to a 6.5 m slant range. The lowered optical center
            // produces a modest upward view.
            float cameraHeight = DefaultCameraHeight;
            float verticalDistance = targetCenter.y - DesiredCameraWorldHeight;
            float horizontalDistanceToTarget = Mathf.Sqrt(Mathf.Max(
                1f, DesiredCameraRange * DesiredCameraRange - verticalDistance * verticalDistance));
            Vector3 rigPosition = targetCenter + awayFromTarget * horizontalDistanceToTarget;
            rigPosition.y = DesiredCameraWorldHeight - cameraHeight;
            Vector3 cameraPosition = rigPosition + Vector3.up * cameraHeight;
            Vector3 aimDirection = targetCenter - cameraPosition;
            if (aimDirection.sqrMagnitude < 0.001f)
            {
                aimDirection = camera.transform.forward;
            }

            float yawAngle = Mathf.Atan2(aimDirection.x, aimDirection.z) * Mathf.Rad2Deg;
            float aimHorizontalDistance = new Vector2(aimDirection.x, aimDirection.z).magnitude;
            float pitchAngle = -Mathf.Atan2(aimDirection.y, aimHorizontalDistance) * Mathf.Rad2Deg;

            Undo.RecordObject(yawObject.transform, "Position gimbal");
            yawObject.transform.SetPositionAndRotation(rigPosition, Quaternion.Euler(0f, yawAngle, 0f));
            yawObject.transform.localScale = Vector3.one;

            Undo.RecordObject(pitch, "Position pitch axis");
            pitch.localPosition = Vector3.up * cameraHeight;
            pitch.localRotation = Quaternion.Euler(pitchAngle, 0f, 0f);
            pitch.localScale = Vector3.one;

            Undo.RecordObject(cameraMount, "Position camera mount");
            cameraMount.localPosition = Vector3.zero;
            cameraMount.localRotation = Quaternion.identity;
            cameraMount.localScale = Vector3.one;

            Undo.SetTransformParent(camera.transform, cameraMount, "Mount camera on gimbal");
            Undo.RecordObject(camera.transform, "Set fixed camera extrinsics");
            camera.transform.localPosition = Vector3.zero;
            camera.transform.localRotation = Quaternion.identity;
            camera.transform.localScale = Vector3.one;

            Undo.RecordObject(muzzle, "Position muzzle");
            muzzle.localPosition = new Vector3(0.08f, -0.06f, 0.25f);
            muzzle.localRotation = Quaternion.identity;
            muzzle.localScale = Vector3.one;

            FPCamera fpCamera = camera.GetComponent<FPCamera>();
            if (fpCamera != null)
            {
                Undo.RecordObject(fpCamera, "Disable old free camera");
                fpCamera.enabled = false;
                EditorUtility.SetDirty(fpCamera);
            }

            GimbalRigController gimbal = GetOrAddComponent<GimbalRigController>(yawObject);
            SetObjectReference(gimbal, "_yawAxis", yawObject.transform);
            SetObjectReference(gimbal, "_pitchAxis", pitch);
            SetObjectReference(gimbal, "_camera", camera);
            SetFloat(gimbal, "_yawSensitivity", 55f);
            SetFloat(gimbal, "_pitchSensitivity", 45f);
            SetFloat(gimbal, "_minimumYaw", -40f);
            SetFloat(gimbal, "_maximumYaw", 40f);
            SetFloat(gimbal, "_minimumPitch", -12f);
            SetFloat(gimbal, "_maximumPitch", 18f);

            ProjectileLauncher launcher = GetOrAddComponent<ProjectileLauncher>(pitch.gameObject);
            SetObjectReference(launcher, "_muzzle", muzzle);
            SetFloat(launcher, "_muzzleVelocity", 20f);
            SetFloat(launcher, "_visualDiameter", 0.06f);
            SetFloat(launcher, "_trailTime", 0.4f);
            SetFloat(launcher, "_trailStartWidth", 0.045f);
            SetColor(launcher, "_projectileColor", new Color(0.1f, 1f, 0.2f, 1f));

            EditorUtility.SetDirty(yawObject);
            EditorUtility.SetDirty(pitch.gameObject);
            EditorUtility.SetDirty(camera.gameObject);
            return yawObject;
        }

        private static void ConfigurePowerRuneStartup(UnityScene scene)
        {
            PowerRuneController[] controllers = Resources.FindObjectsOfTypeAll<PowerRuneController>();
            foreach (PowerRuneController controller in controllers)
            {
                if (controller.gameObject.scene != scene)
                {
                    continue;
                }

                SetBool(controller, "_autoStart", true);
                SetBool(controller, "_autoStartWithRedSide", true);
            }
        }

        private static void ConfigureEnvironmentLighting(UnityScene scene)
        {
            foreach (Light light in Resources.FindObjectsOfTypeAll<Light>())
            {
                if (light.gameObject.scene != scene || light.type != LightType.Directional)
                {
                    continue;
                }

                Undo.RecordObject(light, "Darken energy mechanism background");
                light.intensity = EnvironmentLightIntensity;
                EditorUtility.SetDirty(light);
            }

            RenderSettings.ambientIntensity = EnvironmentAmbientIntensity;
            RenderSettings.reflectionIntensity = 0.65f;
        }

        private static int SetupHitTargets(List<FanController> controllers)
        {
            int count = 0;
            foreach (FanController controller in controllers)
            {
                foreach (Transform fan in controller.transform)
                {
                    if (!fan.name.StartsWith("fan"))
                    {
                        continue;
                    }

                    CriticalPoints criticalPoints = fan.GetComponentInChildren<CriticalPoints>(true);
                    if (criticalPoints == null)
                    {
                        Debug.LogWarning($"No CriticalPoints found below {GetHierarchyPath(fan)}.", fan);
                        continue;
                    }

                    Transform targetTransform = FindOrCreateChild(criticalPoints.transform, TargetName);
                    ConfigureTargetTransform(targetTransform, criticalPoints);

                    BoxCollider collider = GetOrAddComponent<BoxCollider>(targetTransform.gameObject);
                    float hitRadius = EffectiveDetectionDiameter * 0.5f;
                    float hitDiameter = hitRadius * 2f;
                    // A trigger volume catches the projectile before the
                    // original visible mesh collider can consume the impact.
                    collider.isTrigger = true;
                    collider.center = Vector3.zero;
                    collider.size = new Vector3(hitDiameter, hitDiameter, TargetThickness);

                    EnergyHitTarget target = GetOrAddComponent<EnergyHitTarget>(targetTransform.gameObject);
                    SetObjectReference(target, "_fanController", controller);
                    SetObjectReference(target, "_fanRoot", fan);
                    SetFloat(target, "_hitRadius", hitRadius);
                    target.FitToPlateVisual();

                    EditorUtility.SetDirty(collider);
                    EditorUtility.SetDirty(target);
                    count++;
                }
            }

            return count;
        }

        private static void ConfigureTargetTransform(Transform target, CriticalPoints criticalPoints)
        {
            Vector3 center = CalculatePointCenter(criticalPoints);
            Vector3 normal = criticalPoints.Box.normal;
            if (normal.sqrMagnitude < 0.001f)
            {
                normal = criticalPoints.transform.forward;
            }

            Vector3 up = Mathf.Abs(Vector3.Dot(normal.normalized, Vector3.up)) > 0.98f
                ? Vector3.forward
                : Vector3.up;

            Undo.RecordObject(target, "Configure hit target transform");
            target.SetPositionAndRotation(center, Quaternion.LookRotation(normal.normalized, up));
            SetWorldScale(target, Vector3.one);
        }

        private static Vector3 CalculateTargetSize(Transform target, CriticalPoints criticalPoints)
        {
            float maximumX = 0f;
            float maximumY = 0f;
            bool hasPoint = false;

            foreach (Transform point in criticalPoints.transform)
            {
                if (point == target || point.name == TargetName)
                {
                    continue;
                }

                Vector3 localPoint = target.InverseTransformPoint(point.position);
                maximumX = Mathf.Max(maximumX, Mathf.Abs(localPoint.x));
                maximumY = Mathf.Max(maximumY, Mathf.Abs(localPoint.y));
                hasPoint = true;
            }

            if (hasPoint)
            {
                return new Vector3(
                    Mathf.Max(0.1f, maximumX * 2f + TargetMargin),
                    Mathf.Max(0.1f, maximumY * 2f + TargetMargin),
                    TargetThickness);
            }

            Vector3 configuredSize = criticalPoints.Box.size;
            return new Vector3(
                Mathf.Max(0.1f, Mathf.Abs(configuredSize.x)),
                Mathf.Max(0.1f, Mathf.Abs(configuredSize.y)),
                TargetThickness);
        }

        private static Vector3 CalculateTargetCenter(List<CriticalPoints> targets)
        {
            Vector3 sum = Vector3.zero;
            foreach (CriticalPoints target in targets)
            {
                sum += CalculatePointCenter(target);
            }

            return sum / targets.Count;
        }

        private static Vector3 CalculatePointCenter(CriticalPoints criticalPoints)
        {
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (Transform point in criticalPoints.transform)
            {
                if (point.name == TargetName)
                {
                    continue;
                }

                sum += point.position;
                count++;
            }

            return count > 0 ? sum / count : criticalPoints.Box.center;
        }

        private static float FindGroundHeight(Vector3 position)
        {
            Vector3 origin = new Vector3(position.x, position.y + 20f, position.z);
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 100f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            float bestHeight = float.NegativeInfinity;
            foreach (RaycastHit hit in hits)
            {
                if (hit.point.y <= position.y && hit.point.y > bestHeight)
                {
                    bestHeight = hit.point.y;
                }
            }

            return float.IsNegativeInfinity(bestHeight) ? 0f : bestHeight;
        }

        private static List<FanController> FindFanControllers(UnityScene scene)
        {
            List<FanController> result = new List<FanController>();
            FanController[] allControllers = Resources.FindObjectsOfTypeAll<FanController>();
            foreach (FanController controller in allControllers)
            {
                if (controller.gameObject.scene == scene)
                {
                    result.Add(controller);
                }
            }

            return result;
        }

        private static List<CriticalPoints> FindFanTargets(List<FanController> controllers)
        {
            List<CriticalPoints> result = new List<CriticalPoints>();
            foreach (FanController controller in controllers)
            {
                foreach (Transform fan in controller.transform)
                {
                    if (!fan.name.StartsWith("fan"))
                    {
                        continue;
                    }

                    CriticalPoints target = fan.GetComponentInChildren<CriticalPoints>(true);
                    if (target != null)
                    {
                        result.Add(target);
                    }
                }
            }

            return result;
        }

        private static Camera FindMainCamera(UnityScene scene)
        {
            Camera fallback = null;
            Camera[] allCameras = Resources.FindObjectsOfTypeAll<Camera>();
            foreach (Camera camera in allCameras)
            {
                if (camera.gameObject.scene != scene)
                {
                    continue;
                }

                fallback = camera;
                if (camera.CompareTag("MainCamera") || camera.name == "Main Camera")
                {
                    return camera;
                }
            }

            return fallback;
        }

        private static GameObject FindSceneObject(UnityScene scene, string objectName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
                foreach (Transform candidate in transforms)
                {
                    if (candidate.name == objectName)
                    {
                        return candidate.gameObject;
                    }
                }
            }

            return null;
        }

        private static Transform FindOrCreateChild(Transform parent, string childName)
        {
            Transform child = parent.Find(childName);
            if (child != null)
            {
                return child;
            }

            GameObject childObject = new GameObject(childName);
            Undo.RegisterCreatedObjectUndo(childObject, $"Create {childName}");
            Undo.SetTransformParent(childObject.transform, parent, $"Parent {childName}");
            childObject.transform.localPosition = Vector3.zero;
            childObject.transform.localRotation = Quaternion.identity;
            childObject.transform.localScale = Vector3.one;
            return childObject.transform;
        }

        private static T GetOrAddComponent<T>(GameObject gameObject) where T : Component
        {
            T component = gameObject.GetComponent<T>();
            return component != null ? component : Undo.AddComponent<T>(gameObject);
        }

        private static void SetObjectReference(Object target, string propertyName, Object value)
        {
            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogError($"Serialized property {propertyName} was not found on {target.GetType().Name}.", target);
                return;
            }

            property.objectReferenceValue = value;
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }

        private static void SetFloat(Object target, string propertyName, float value)
        {
            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogError($"Serialized property {propertyName} was not found on {target.GetType().Name}.", target);
                return;
            }

            property.floatValue = value;
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }

        private static void SetBool(Object target, string propertyName, bool value)
        {
            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogError($"Serialized property {propertyName} was not found on {target.GetType().Name}.", target);
                return;
            }

            property.boolValue = value;
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }

        private static void SetColor(Object target, string propertyName, Color value)
        {
            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogError($"Serialized property {propertyName} was not found on {target.GetType().Name}.", target);
                return;
            }

            property.colorValue = value;
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }

        private static void SetWorldScale(Transform transform, Vector3 worldScale)
        {
            Transform parent = transform.parent;
            if (parent == null)
            {
                transform.localScale = worldScale;
                return;
            }

            Vector3 parentScale = parent.lossyScale;
            transform.localScale = new Vector3(
                SafeDivide(worldScale.x, parentScale.x),
                SafeDivide(worldScale.y, parentScale.y),
                SafeDivide(worldScale.z, parentScale.z));
        }

        private static float SafeDivide(float value, float divisor)
        {
            return Mathf.Abs(divisor) < 0.0001f ? value : value / divisor;
        }

        private static string GetHierarchyPath(Transform transform)
        {
            string path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }

            return path;
        }
    }
}
