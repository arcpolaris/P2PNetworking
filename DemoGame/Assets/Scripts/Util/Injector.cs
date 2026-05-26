using UnityEngine;

namespace DemoGame.Util
{
	public abstract class Injector : MonoBehaviour
	{
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		static void ResetInjection() => injected = false;

		private static bool injected;

		protected abstract void Inject();

		private void Awake()
		{
			if (!injected)
			{
				injected = true;
				Inject();
			}
			OnInitialize();
		}

		/// <summary>
		/// Use this instead of <see cref="Awake"/>
		/// </summary>
		protected virtual void OnInitialize()
		{

		}
	}
}