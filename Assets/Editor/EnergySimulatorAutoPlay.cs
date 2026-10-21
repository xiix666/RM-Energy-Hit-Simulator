using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityScene = UnityEngine.SceneManagement.Scene;

namespace Simulation.Editor
{
    [InitializeOnLoad]
    internal static class EnergySimulatorAutoPlay
    {
        private const string Argument = "-energySimulatorAutoPlay";
        private const string SessionKey = "RM.EnergyHitSimulator.AutoPlayHandled";
        private const string EntryScenePath = "Assets/Scenes/Init.unity";
        private static readonly double EarliestStartTime;

        static EnergySimulatorAutoPlay()
        {
            if (!HasCommandLineArgument() || SessionState.GetBool(SessionKey, false)) return;
            EarliestStartTime = EditorApplication.timeSinceStartup + 1.0;
            EditorApplication.update += TryStart;
        }

        private static bool HasCommandLineArgument()
        {
            foreach (string argument in Environment.GetCommandLineArgs())
            {
                if (string.Equals(argument, Argument, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static void TryStart()
        {
            if (EditorApplication.timeSinceStartup < EarliestStartTime ||
                EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode) return;

            UnityScene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || activeScene.path != EntryScenePath || SceneManager.sceneCount != 1)
            {
                EditorSceneManager.OpenScene(EntryScenePath, OpenSceneMode.Single);
            }

            SessionState.SetBool(SessionKey, true);
            EditorApplication.update -= TryStart;
            Debug.Log("RM Energy Hit Simulator: entry scene ready, entering Play Mode.");
            EditorApplication.isPlaying = true;
        }
    }
}
