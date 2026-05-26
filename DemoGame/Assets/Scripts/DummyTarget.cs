using UnityEngine;

namespace DemoGame
{
    [RequireComponent(typeof(Damageable))]
    public class DummyTarget : MonoBehaviour
    {
        Damageable damageable;

        void Start()
        {
            damageable = GetComponent<Damageable>();
            damageable.OnDamaged.AddListener(static args => print($"took {args.damage} damage from {args.source.FriendlyName}"));
        }
    }
}