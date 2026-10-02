using UnityEngine;

namespace WarriorRun.UI
{
    /// <summary>Gentle scale/alpha pulse for prompts and titles.</summary>
    public class Pulse : MonoBehaviour
    {
        [SerializeField] float speed = 2.2f;
        [SerializeField] float scaleAmount = 0.06f;
        [SerializeField] float alphaAmount = 0.25f;

        Vector3 baseScale;
        CanvasGroup group;

        void Awake()
        {
            baseScale = transform.localScale;
            group = GetComponent<CanvasGroup>();
        }

        void Update()
        {
            float s = Mathf.Sin(Time.unscaledTime * speed);
            transform.localScale = baseScale * (1f + s * scaleAmount);
            if (group != null)
                group.alpha = 1f - (0.5f + 0.5f * s) * alphaAmount;
        }
    }
}
