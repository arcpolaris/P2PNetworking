using MessagePack;
using NetModel;
using UnityEngine;

namespace DemoGame.Networking
{
    [RequireComponent(typeof(NetworkObject)), RequireComponent(typeof(Damageable))]
    public class NetworkDamageForward : MonoBehaviour
    {
		NetworkObject netobj;
        Damageable damageable;

        void Start()
        {
            damageable = GetComponent<Damageable>();
            netobj = GetComponent<NetworkObject>();

            if (netobj.IsLocallyOwned)
            {
                Debug.LogError($"{nameof(NetworkDamageForward)} is intended for remote objects");
                Destroy(this);
            }

            damageable.OnDamaged.AddListener(args =>
            {
                if (netobj.GetOwner() == null)
                {
                    Debug.LogError("Object owner is null");
                    return;
                }

                DamageForward message = new(args.damage, netobj.guid, args.source);
                NetworkManager.Instance.Network.Send(message);
            });
        }
    }

    [MessagePackObject(AllowPrivate = true)]
    public partial class DamageForward : IMessage
    {
        [IgnoreMember]
        public float Damage { get; private set; }
        [Key(0)]
        private int DamageInt => (int)(Damage * 100);
        [Key(1)]
        public string Target { get; private set; }
        [IgnoreMember]
        public IDamageSource Source { get; private set; }
        [Key(2)]
        private string SourceName => Source.FriendlyName;

        public DamageForward(float damage, string target, IDamageSource source)
        {
            Damage = damage;
            Target = target;
            Source = source;
        }

        [SerializationConstructor]
        private DamageForward(int damage, string target, string source)
        {
            Damage = damage / 100f;
            Target = target;
            Source = new AnonymousDamageSource(source);
        }

		private struct AnonymousDamageSource : IDamageSource
		{
			public AnonymousDamageSource(string name) => FriendlyName = name;
			public readonly string FriendlyName { get; }
		}
	}
}