using UnityEngine;
using UnityEngine.Events;

namespace DemoGame
{
    public class Damageable : MonoBehaviour, IDamageable
    {
        [field: SerializeField]
        public UnityEvent<DamageEventArgs> OnDamaged { get; private set; } = new();

        public void Damage(IDamageSource source, float damage)
        {
            OnDamaged.Invoke(new DamageEventArgs(source, damage));
        }
    }

    public interface IDamageable
    {
        public void Damage(IDamageSource source, float damage);
    }

    [System.Serializable]
    public struct DamageEventArgs
    {
        public IDamageSource source;
        public float damage;

        public DamageEventArgs(IDamageSource source, float damage)
        {
            this.damage = damage;
            this.source = source;
        }
    }

    public interface IDamageSource
    {
		public string FriendlyName { get; }
	}
}