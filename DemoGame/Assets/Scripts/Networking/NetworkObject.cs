using System;
using MessagePack;
using NetModel;
using UnityEngine;

namespace DemoGame.Networking
{
    public class NetworkObject : MonoBehaviour
    {
        public Guid Guid { get; private set; } = new();

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

        void Awake()
        {
            if (Guid == Guid.Empty)
                Guid = Guid.NewGuid();

            NetworkManager.Instance
                .Prefabs
                .TryAdd(
                PawnPrefab.Guid, 
                PawnPrefab);

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
                        NetworkObject instance = Instantiate(prefab, makePawn.Position, makePawn.Rotation);
                        instance.PawnPrefab = prefab;
                        instance.Guid = makePawn.Id;
                        instance.IsLocallyOwned = false;
                        instance.ownerId = makePawn.Owner;
                        instance.BroadcastMessage("AwakePawn");
                    }, reliable: true
                );
            }
        }

        void Start()
        {
            NetworkManager.Instance.Objects.Add(Guid, this);
        }

        void Update()
        {
            if (!IsLocallyOwned) return;
            if (NetworkManager.Instance.Network.Peers.Count == 0) return;

            print(NetworkManager.Instance.Network.MyId);
            NetworkManager.Instance.Network.Send<SetTransform>(new(this));
        }
    }

    [MessagePackObject(AllowPrivate = true)]
    public partial class SetTransform : IMessage
    {
        [Key(0)]
        public Guid Id { get; private set; }
        [Key(1)]
        public Vector3 Position { get; private set; }
        [Key(2)]
        public Quaternion Rotation { get; private set; }

        [SerializationConstructor]
        private SetTransform(Guid id, Vector3 position, Quaternion rotation)
        {
            Id = id;
            Position = position;
            Rotation = rotation;
        }

        public SetTransform(NetworkObject t)
        {
            Id = t.Guid;
            t.transform.GetLocalPositionAndRotation(out var pos, out var rot);
            Position = pos;
            Rotation = rot;
        }

		public void Apply(Transform t) => t.SetLocalPositionAndRotation(Position, Rotation);
	}

    [MessagePackObject(AllowPrivate = true)]
    public partial class MakePawn : IMessage
    {
        [Key(0)] public ushort Owner { get; private set; }
        [Key(1)] public Guid Id { get; private set;}
        [Key(2)] public Guid Prefab { get; private set; }
        [Key(3)] public Vector3 Position { get; private set; }
        [Key(4)] public Quaternion Rotation { get; private set; }

        public MakePawn(NetworkObject obj)
        {
            Owner = (ushort)NetworkManager.Instance.Network.MyId;
            Id = obj.Guid;
            Prefab = obj.PawnPrefab.Guid;
        }

        [SerializationConstructor]
		public MakePawn(ushort owner, Guid id, Guid prefab)
		{
			Owner = owner;
			Id = id;
			Prefab = prefab;
		}
	}
}