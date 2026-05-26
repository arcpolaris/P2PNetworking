using DemoGame.Util;
using UnityEngine;

namespace DemoGame.Networking {
    public class DamageForwardInjector : Singleton<DamageForwardInjector>
    {
		protected override bool DontDestoryOnLoad => false;

		protected override void OnInitialize()
		{
			NetworkManager.Instance.NetworkBuilder.RegisterWithForward<DamageForward>(304,
				(_, _, damagefwd) =>
				{
					Debug.Log("got a thing");
					if (!NetworkManager.Instance.Objects.TryGetValue(damagefwd.Target, out var obj))
					{
						Debug.Log($"Coudn't find {damagefwd.Target}");
						return;
					}
					var damageable = obj.GetComponent<Damageable>();
					damageable.Damage(damagefwd.Source, damagefwd.Damage);
				});
		}
    }
}