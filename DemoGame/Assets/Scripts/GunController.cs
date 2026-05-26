using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace DemoGame
{
    public class GunController : MonoBehaviour, IDamageSource
    {
        [SerializeField, Min(0)] private float range;
        [SerializeField, Min(0)] private float damage;
        [SerializeField] AudioSource sfx;
        [SerializeField] RawImage flashFx;
        [SerializeField] GameObject hitIndicator;
        [SerializeField, Min(0.001f)] float indicatorLifetime;

        [SerializeField] PlayerController player;

        void Update()
        {
            DrawAimLine();
            if (PauseManager.Instance.IsPaused) return;
            if (!Input.GetMouseButtonDown(0)) return;

            if (sfx) sfx.Play();

            StopCoroutine(nameof(MuzzleFlashRoutine));
            StartCoroutine(nameof(MuzzleFlashRoutine));

            Ray ray = new(transform.position, transform.forward);
            if (!Physics.Raycast(ray, out var hit, range)) return;

            Debug.DrawLine(ray.origin, hit.point, Color.blue, 1f);
            var indicator = Instantiate(hitIndicator, hit.point, Quaternion.identity);
            Destroy(indicator, indicatorLifetime);

            if (!hit.collider.gameObject.TryGetComponent<Damageable>(out var damageable)) return;
            damageable.Damage(this, damage);
        }

        void DrawAimLine()
        {
            Ray ray = new(transform.position, transform.forward);
            if (Physics.Raycast(ray, out var hit, range))
            {
                Debug.DrawLine(ray.origin, hit.point, Color.green);
            }
            else
            {
                Debug.DrawLine(ray.origin, ray.GetPoint(range), Color.red);
            }
        }

        IEnumerator MuzzleFlashRoutine()
        {
            flashFx.enabled = true;
            yield return null;
            yield return new WaitForEndOfFrame();
            flashFx.enabled = false;
        }

		public string FriendlyName => player.FriendlyName;
	}
}