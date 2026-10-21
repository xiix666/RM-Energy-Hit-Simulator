using ScriptableObjects;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityGameFramework.Runtime;

namespace Scene
{
    public class CustomSceneComponent : GameFrameworkComponent
    {
        [SerializeField] private HashSO _config;

        public bool LoadScene(string key, LoadSceneMode loadMode)
        {
            string sceneName = _config.Get(key);
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                UnityEngine.SceneManagement.Scene loadedScene = SceneManager.GetSceneAt(i);
                if (loadedScene.isLoaded && loadedScene.name == sceneName)
                {
                    Debug.Log($"Scene '{sceneName}' is already loaded; duplicate additive load skipped.", this);
                    return false;
                }
            }

            SceneManager.LoadScene(sceneName, loadMode);
            return true;
        }

        public void UnloadScene(string key)
        {
            SceneManager.UnloadSceneAsync(_config.Get(key));
        }

        protected override void Awake()
        {
            base.Awake();
            _config.Init();
        }
    }
}
