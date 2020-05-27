using UnityEngine;

namespace UniHelpers
{
    public abstract class Singleton<T> where T : Singleton<T>, new()
    {
        private static T api;
        protected static T Api
        {
            get
            {
#if UNITY_EDITOR
                if (UnityEditor.EditorApplication.isPlaying)
                {
                    CreateInScene();
                }
                else if (api == null)
                {
                    api = new T();
                    UnityEditor.EditorApplication.update += api.OnUpdate;
                }
#else
            CreateInScene();
#endif
                return api;
            }
        }

        private SingletonComponent singletonInScene;

        private static void CreateInScene()
        {
            if (api == null)
            {
                api = new T();
                api.singletonInScene = new GameObject().AddComponent<SingletonComponent>().Init(() =>
                {
                    if (api != null) api.OnUpdate();
                }, () =>
                {
                    if (api != null) Shutdown();
                });
                api.singletonInScene.gameObject.name = "[SINGLETON] " + api.GetType().FullName;
                GameObject.DontDestroyOnLoad(api.singletonInScene.gameObject);
            }
        }

        protected virtual void OnUpdate() { }

        protected virtual void OnShutdown() { }

        public static void Shutdown()
        {
            if (api != null)
            {
#if UNITY_EDITOR
                try
                {
                    UnityEditor.EditorApplication.update -= api.OnUpdate;
                }
                catch (System.Exception e) { e.ToString(); }
#endif
                api.OnShutdown();
                MonoBehaviour beh = api.singletonInScene;
                api.singletonInScene = null;
                api = null;

                if (beh != null && beh.gameObject != null && !ReferenceEquals(beh.gameObject, null))
                {
                    GameObject.Destroy(beh.gameObject);
                }
            }
        }
    }

    public abstract class SingletonComponent<T> : MonoBehaviour where T : SingletonComponent<T>
    {
        protected static T Api { get; private set; }

        protected virtual void Awake() {
            Api = (T)this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy() {
            OnShutdown();
        }

        protected virtual void OnShutdown() { }

        public static void Shutdown()
        {
            Destroy(Api.gameObject);
        }
    }

    internal class SingletonComponent : MonoBehaviour
    {
        System.Action update;
        System.Action destroy;
        public SingletonComponent Init(System.Action update, System.Action destroy) { this.update = update; this.destroy = destroy; return this; }
        private void Update() { update?.Invoke(); }
        private void OnDestroy() { destroy?.Invoke(); }
    }
}
