using UnityEngine;

namespace WarriorRun.World
{
    /// <summary>Cheap flame flicker — jitters a point light's intensity/range.</summary>
    [RequireComponent(typeof(Light))]
    public class TorchFlicker : MonoBehaviour
    {
        [SerializeField] float baseIntensity = 1.6f;
        [SerializeField] float baseRange = 5f;
        [SerializeField] float jitter = 0.28f;
        [SerializeField] float speed = 9f;

        Light l;
        float seed;

        void Awake()
        {
            l = GetComponent<Light>();
            seed = transform.position.x * 13.7f + transform.position.z * 7.1f;
        }

        void Update()
        {
            float n = Mathf.PerlinNoise(seed, Time.time * speed);
            l.intensity = baseIntensity * (1f - jitter * 0.5f + jitter * n);
            l.range = baseRange * (0.92f + 0.16f * n);
        }
    }
}
