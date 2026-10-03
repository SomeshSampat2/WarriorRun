using UnityEngine;
using WarriorRun.Core;

namespace WarriorRun.World
{
    /// <summary>Spinning pickup. PlayerController consumes it via trigger.</summary>
    public class Coin : MonoBehaviour
    {
        [SerializeField] float spinSpeed = 240f;
        [SerializeField] float bobAmount = 0.12f;
        [SerializeField] float bobSpeed = 2.4f;
        public GameObject burstPrefab;

        float baseY;
        float phase;
        static bool burstWarmed;   // first pickup shouldn't pay shader-compile cost

        void OnEnable()
        {
            baseY = transform.position.y;
            phase = Random.value * Mathf.PI * 2f;
            if (!burstWarmed && burstPrefab != null)
            {
                burstWarmed = true;
                Destroy(Instantiate(burstPrefab, transform.position, Quaternion.identity));
            }
        }

        void Update()
        {
            transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
            var p = transform.position;
            p.y = baseY + Mathf.Sin(Time.time * bobSpeed + phase) * bobAmount;
            transform.position = p;

            // magnet: coins fly to the runner
            var gm = GameManager.Instance;
            if (gm != null && gm.MagnetActive && gm.PlayerTf != null)
            {
                var target = gm.PlayerTf.position + Vector3.up * 1.1f;
                if ((transform.position - target).sqrMagnitude < 49f)
                {
                    transform.position = Vector3.MoveTowards(transform.position, target, 17f * Time.deltaTime);
                    if ((transform.position - target).sqrMagnitude < 0.7f)
                    {
                        gm.AddCoin();
                        Collect();
                    }
                }
            }
        }

        public void Collect()
        {
            if (burstPrefab != null)
                Instantiate(burstPrefab, transform.position, Quaternion.identity);
            gameObject.SetActive(false); // returns to pool
        }
    }
}
