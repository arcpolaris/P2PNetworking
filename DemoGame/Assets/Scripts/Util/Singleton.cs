using UnityEngine;

namespace DemoGame.Util
{
    public abstract class Singleton<T> : MonoBehaviour where T : Singleton<T>
    {
        public static T Instance { get; private set; } = null;

        private void Awake()
        {
			if (typeof(T) != GetType())
			{
				string message = $"Class {GetType().Name} must inherit from Singleton<{GetType().Name}>, not Singleton<{typeof(T).Name}>";
				throw new System.InvalidOperationException(message);
			}

			if (Instance && Instance != this)
			{
				Destroy(this);
				Debug.LogErrorFormat("Only one instance of a singleton is allowed per scene");
				return;
			}

			Instance = (T)this;
			if (DontDestoryOnLoad)
				DontDestroyOnLoad(Instance);	

			OnInitialize();
		}

		/// <summary>
		/// Use this instead of <see cref="Awake"/>
		/// </summary>
		protected virtual void OnInitialize()
		{
			
		}

		protected virtual bool DontDestoryOnLoad => true;
    }
}