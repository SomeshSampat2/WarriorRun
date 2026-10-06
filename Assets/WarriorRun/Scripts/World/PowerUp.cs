using UnityEngine;

namespace WarriorRun.World
{
    public enum PowerUpKind { Magnet, Shield, Boost, Car, Plane, Ghost, Spring, Giant }

    /// <summary>Floating powerup pickup — spins and bobs like coins.</summary>
    public class PowerUp : MonoBehaviour
    {
        public PowerUpKind kind;
        public GameObject burstPrefab;

        [SerializeField] float spinSpeed = 150f;
        [SerializeField] float bobAmount = 0.16f;
        [SerializeField] float bobSpeed = 2.6f;

        float baseY;
        float phase;
        static bool burstWarmed;

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
        }

        public void Collect()
        {
            if (burstPrefab != null)
                Instantiate(burstPrefab, transform.position, Quaternion.identity);
            gameObject.SetActive(false);
        }
    }
}
