using System;
using System.Linq;
using MessagePack;
using NetModel;
using UnityEngine;

namespace DemoGame.Networking
{
    public class NetworkObject : MonoBehaviour
    {
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		static void ResetStatics() => injected = false;

		public string guid = Guid.NewGuid().ToString();

        private static bool injected;

        [field: SerializeField]
        public bool IsLocallyOwned { get; private set; } = true;

        private ushort? ownerId;
        
        public ushort? GetOwner()
        {
            if (ownerId != null) return ownerId;
            if (NetworkManager.Instance.Network == null) return null;
            ownerId = NetworkManager.Instance.Network.MyId;
            return ownerId;
        }

        public NetworkObject PawnPrefab;

        bool IsPrefab => PawnPrefab == this;

        void Awake()
        {
            if (!IsPrefab)
                guid = Guid.NewGuid().ToString();

            NetworkManager.Instance
                .Prefabs
                .TryAdd(PawnPrefab.guid, PawnPrefab);
            UnityEditor.EditorUtility.SetDirty(NetworkManager.Instance);
            if (!injected)
            {
                injected = true;
                NetworkManager.Instance.NetworkBuilder
                    .RegisterWithForward<SetTransform>(200,
                    static (net, sender, setTransform) =>
                    {
                        if (NetworkManager.Instance.Objects.TryGetValue(setTransform.Id, out NetworkObject obj))
                        {
                            setTransform.Apply(obj.transform);
                        }
                    }
                )
                    .RegisterWithForward<MakePawn>(201,
                    static (net, sender, makePawn) =>
                    {
                        // idempotentcy
                        if (NetworkManager.Instance.Objects.ContainsKey(makePawn.Id)) return;

                        NetworkObject prefab = NetworkManager.Instance.Prefabs[makePawn.Prefab];
						UnityEditor.EditorUtility.SetDirty(NetworkManager.Instance);
						NetworkObject instance = Instantiate(prefab, makePawn.Position, makePawn.Rotation);
                        instance.PawnPrefab = prefab;
                        instance.guid = makePawn.Id;
                        instance.IsLocallyOwned = false;
                        instance.ownerId = makePawn.Owner;
                        instance.BroadcastMessage("AwakePawn");
                    }, reliable: true
                );
            }
        }

        void Start()
        {
            NetworkManager.Instance.Objects.Add(guid, this);
			UnityEditor.EditorUtility.SetDirty(NetworkManager.Instance);
		}

		void Update()
        {
            if (!IsLocallyOwned) return;
            if (NetworkManager.Instance.Network.Peers.Count == 0) return;

            print(NetworkManager.Instance.Network.MyId);
            NetworkManager.Instance.Network.Send<SetTransform>(new(this));
        }
    }

    [MessagePackObject]
    public partial class SetTransform : IMessage
    {
        [Key(0)]
        public string Id { get; private set; }
        [Key(1)]
        public Vector3 Position { get; private set; }
        [Key(2)]
        public Quaternion Rotation { get; private set; }

        [SerializationConstructor]
        public SetTransform(string id, Vector3 position, Quaternion rotation)
        {
            Id = id;
            Position = position;
            Rotation = rotation;
        }

        public SetTransform(NetworkObject t)
        {
            Id = t.guid;
            t.transform.GetLocalPositionAndRotation(out var pos, out var rot);
            Position = pos;
            Rotation = rot;
        }

		public void Apply(Transform t) => t.SetLocalPositionAndRotation(Position, Rotation);
	}

    [MessagePackObject]
    public partial class MakePawn : IMessage
    {
        [Key(0)] public ushort Owner { get; private set; }
        [Key(1)] public string Id { get; private set;}
        [Key(2)] public string Prefab { get; private set; }
        [Key(3)] public Vector3 Position { get; private set; }
        [Key(4)] public Quaternion Rotation { get; private set; }

        public MakePawn(NetworkObject obj)
        {
            Owner = (ushort)(NetworkManager.Instance.Network.MyId ?? 255u);
            Id = obj.guid;
            Prefab = obj.PawnPrefab.guid;
            Position = obj.gameObject.transform.localPosition;
            Rotation = obj.gameObject.transform.localRotation;
        }

        [SerializationConstructor]
		public MakePawn(ushort owner, string id, string prefab, Vector3 position, Quaternion rotation)
		{
			Owner = owner;
			Id = id;
			Prefab = prefab;
            Position = position;
            Rotation = rotation;
		}
	}
}