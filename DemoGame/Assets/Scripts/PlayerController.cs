using UnityEngine;

namespace DemoGame
{
	[RequireComponent(typeof(CharacterController))]
	public class PlayerController : MonoBehaviour
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

		private CharacterController ctrl;

		public bool IsFocused
		{
			get => Cursor.lockState is CursorLockMode.Locked;
			set
			{
				Cursor.visible = !value;
				Cursor.lockState = value ? CursorLockMode.Locked : CursorLockMode.None;
			}
		}

		Vector3 velocity;

		void AwakePawn()
		{
			this.enabled = false;
		}

	    void Start()
	    {
			ctrl = GetComponent<CharacterController>();
	    }

	    void Update()
	    {
			if (Input.GetButtonDown("Cancel"))
			{
				IsFocused = false;
			}
			if (Input.GetMouseButtonDown(0))
			{
				IsFocused = true;
			}

			if (IsFocused)
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
	}
}
