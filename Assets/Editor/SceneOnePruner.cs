using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityScene = UnityEngine.SceneManagement.Scene;

namespace Simulation.Editor
{
    /// <summary>
    /// Backs up and prunes Scene (1), retaining only Power Rune and ground.
    /// </summary>
    public static class SceneOnePruner
    {
        private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";
        private const string BackupFolder = "Assets/Backups/Scene1";
        private static readonly HashSet<string> KeepNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Power Rune",
                "ground",
            };

        [MenuItem("Tools/RM Energy Hit Simulator/Backup and Prune Scene (1) _F8")]
        public static void BackupAndPruneSceneOne()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("Stop Play Mode before pruning Scene (1).");
                return;
            }

            if (!Application.isBatchMode &&
                !EditorUtility.DisplayDialog(
                    "Backup and Prune Scene (1)",
                    "Scene (1) will retain only Power Rune and ground. " +
                    "All other direct children will be saved to a backup Prefab first.",
                    "Back up and prune",
                    "Cancel"))
            {
                return;
            }

            UnityScene scene = OpenSampleScene();
            if (!scene.IsValid()) return;

            GameObject sceneOne = FindSceneObject(scene, "Scene (1)");
            if (sceneOne == null)
            {
                throw new InvalidOperationException("Scene (1) was not found in SampleScene.");
            }

            List<GameObject> remove = new List<GameObject>();
            bool hasPowerRune = false;
            bool hasGround = false;
            foreach (Transform child in sceneOne.transform)
            {
                if (string.Equals(child.name, "Power Rune", StringComparison.OrdinalIgnoreCase))
                {
                    hasPowerRune = true;
                }
                else if (string.Equals(child.name, "ground", StringComparison.OrdinalIgnoreCase))
                {
                    hasGround = true;
                }
                else
                {
                    remove.Add(child.gameObject);
                }
            }

            if (!hasPowerRune || !hasGround)
            {
                throw new InvalidOperationException(
                    $"Safety check failed: Power Rune={hasPowerRune}, ground={hasGround}.");
            }
            if (remove.Count == 0)
            {
                Debug.Log("Scene (1) is already pruned; nothing was deleted.");
                return;
            }

            EnsureBackupFolder();
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string prefabPath = $"{BackupFolder}/Scene1_RemovedObjects_{stamp}.prefab";

            // Clone the complete hierarchy once so references between removed
            // siblings remain intact, then strip the two retained children.
            GameObject backupRoot = UnityEngine.Object.Instantiate(sceneOne);
            backupRoot.name = $"Scene1_RemovedObjects_Backup_{stamp}";
            for (int index = backupRoot.transform.childCount - 1; index >= 0; index--)
            {
                Transform child = backupRoot.transform.GetChild(index);
                if (KeepNames.Contains(child.name))
                {
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
            }

            PrefabUtility.SaveAsPrefabAsset(backupRoot, prefabPath, out bool prefabSaved);
            UnityEngine.Object.DestroyImmediate(backupRoot);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            if (!prefabSaved || AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                throw new IOException($"Backup Prefab could not be verified: {prefabPath}");
            }

            Undo.SetCurrentGroupName("Prune Scene (1)");
            int undoGroup = Undo.GetCurrentGroup();
            List<string> removedNames = new List<string>();
            foreach (GameObject objectToRemove in remove)
            {
                removedNames.Add(objectToRemove.name);
                Undo.DestroyObjectImmediate(objectToRemove);
            }
            Undo.CollapseUndoOperations(undoGroup);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
            {
                throw new IOException("SampleScene could not be saved after pruning.");
            }

            Debug.Log(
                $"Scene (1) pruned successfully. Kept Power Rune and ground; " +
                $"removed {removedNames.Count} objects: {string.Join(", ", removedNames)}. " +
                $"Backup Prefab: {prefabPath}");
        }

        private static UnityScene OpenSampleScene()
        {
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                UnityScene loaded = SceneManager.GetSceneAt(index);
                if (loaded.path == SampleScenePath) return loaded;
            }

            UnityScene active = SceneManager.GetActiveScene();
            if (active.path == SampleScenePath) return active;
            if (!Application.isBatchMode &&
                !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return default;
            }
            return EditorSceneManager.OpenScene(SampleScenePath, OpenSceneMode.Single);
        }

        private static void EnsureBackupFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Backups"))
            {
                AssetDatabase.CreateFolder("Assets", "Backups");
            }
            if (!AssetDatabase.IsValidFolder(BackupFolder))
            {
                AssetDatabase.CreateFolder("Assets/Backups", "Scene1");
            }
        }

        private static GameObject FindSceneObject(UnityScene scene, string objectName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
                {
                    if (candidate.name == objectName) return candidate.gameObject;
                }
            }
            return null;
        }
    }
}
