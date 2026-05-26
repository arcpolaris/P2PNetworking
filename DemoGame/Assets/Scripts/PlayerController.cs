using System.Collections.Generic;
using DemoGame.Util;
using MessagePack;
using NetModel;
using UnityEngine;

namespace DemoGame
{
	[RequireComponent(typeof(CharacterController)), RequireComponent(typeof(Damageable)), RequireComponent(typeof(HealthTracker))]
	public class PlayerController : Injector, IDamageSource, IDamageable
	{
		[Header("Constants")]
		[SerializeField] private Vector2 lookSpeed = new(1, 1);
		[SerializeField] private float moveSpeed = 1;
		[SerializeField] private float sprintMultiplier = 1;
		[SerializeField] private float jumpHeight = 1;
		[SerializeField] private float jumpTime = 1;

		[Space]
		[Header("Objects")]
		[SerializeField]
		private Transform head;
		[SerializeField]
		private List<Transform> respawns = new();

		private CharacterController ctrl;
		private HealthTracker healthTracker;
		private Damageable damageable;

		Vector3 velocity;

		void Start()
		{
			ctrl = GetComponent<CharacterController>();
			healthTracker = GetComponent<HealthTracker>();

			damageable = GetComponent<Damageable>();
			damageable.OnDamaged.AddListener(OnDamaged);

			healthTracker.OnDeath.AddListener(OnDeath);
		}

		void Update()
		{
			if (transform.position.y < -10)
			{
				damageable.Damage(VoidDamage.Instance, 10f);
			}

			if (!PauseManager.Instance.IsPaused)
			{
				float dx = Input.GetAxisRaw("Mouse X") * lookSpeed.x;
				float dy = Input.GetAxisRaw("Mouse Y") * lookSpeed.y;

				transform.Rotate(Vector3.up, dx);

				float tilt = head.localEulerAngles.x + dy;
				tilt = tilt switch
				{
					> 90 and < 180 => 90,
					< 270 and > 180 => 270,
					_ => tilt
				};

				head.localRotation = Quaternion.Euler(tilt, 0, 0);
				if (Input.GetButtonDown("Jump"))
				{
					if (ctrl.isGrounded)
					{
						velocity.y = 2 * jumpHeight / jumpTime;
					}
				}
				Vector3 move = transform.forward * Input.GetAxisRaw("Vertical") + transform.right * Input.GetAxisRaw("Horizontal");
				move.Normalize();
				if (Input.GetButton("Sprint")) move *= sprintMultiplier;
				ctrl.Move(moveSpeed * Time.deltaTime * move);
			}

			if (ctrl.isGrounded)
			{
				if (velocity.y < Mathf.Epsilon)
					velocity.y = -2f;
			}
			else
			{
				velocity.y -= 4 * jumpHeight / jumpTime / jumpTime * Time.deltaTime;
			}

			ctrl.Move(Time.deltaTime * velocity);
		}

		void OnDamaged(DamageEventArgs args)
		{
			Debug.Log($"{args.damage} damage from {args.source.FriendlyName}");
		}

		public string FriendlyName
		{
			get
			{
				if (NetworkManager.Instance.Network == null) return "Player";
				ushort? id = NetworkManager.Instance.Network.MyId;
				if (id != null) return $"Player #{id}";
				else return "Unidentified player";
			}
		}

		public void Damage(IDamageSource source, float damage)
		{
			print($"damaged by {source.FriendlyName}. yeowch!");
		}

		void OnDeath(IDamageSource source)
		{
			Transform respawn = respawns[Random.Range(0, respawns.Count)];
			print(respawn.position);
			ctrl.enabled = false;
			transform.position = respawn.position;
			ctrl.enabled = true;
			healthTracker.RestoreHealth();

			print("killed by " + source.FriendlyName);
			string message = FriendlyName + " killed by " + source.FriendlyName;

			if (NetworkManager.Instance.Network == null) return;
			NetworkManager.Instance.Network.Send<DeathMessage>(new(message), reliable: true);
		}

		protected override void Inject()
		{
			NetworkManager.Instance.NetworkBuilder.RegisterWithForward<DeathMessage>(302,
				static (_, _, deathMessage) =>
				{
					Debug.Log(deathMessage.Message);
				});
		}
	}

	[MessagePackObject]
	public partial class DeathMessage : IMessage
	{
		[Key(0)]
		public string Message { get; private set; }

		[SerializationConstructor]
		public DeathMessage(string message)
		{
			Message = message;
		}
	}

	class VoidDamage : IDamageSource
	{
		public string FriendlyName => "The void";

		public static VoidDamage Instance = new();

		private VoidDamage() { }
	}
}
