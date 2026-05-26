using DemoGame;
using UnityEngine;
using UnityEngine.Events;

namespace DemoGame
{
    [RequireComponent(typeof(Damageable))]
    public class HealthTracker : MonoBehaviour, IDamageable
    {
        [field: SerializeField]
        public float Health { get; private set; }

        [SerializeField, Min(1)]
        private float maxHealth = 100;

        [field: SerializeField]
        public UnityEvent<IDamageSource> OnDeath { get; private set; } = new();

        private void OnValidate()
        {
            if (!Application.isPlaying)
            {
                Health = maxHealth;
            }
        }

        void Awake()
        {
            Health = maxHealth;
        }

        void Start()
        {
            var damagable = GetComponent<Damageable>();
            damagable.OnDamaged.AddListener(args => Damage(args.source, args.damage));
        }

        public void Damage(IDamageSource source, float damage)
        {
            if (Health <= 0)
            {
                return;
            }
            Health -= damage;
            Health = Mathf.Max(Health, 0);
            if (Health <= 0)
            {
                OnDeath.Invoke(source);
            }
        }

        public void RestoreHealth()
        {
            Health = maxHealth;
        }
    }
}