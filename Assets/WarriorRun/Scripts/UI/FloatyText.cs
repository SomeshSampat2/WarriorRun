using UnityEngine;

namespace WarriorRun.UI
{
    /// <summary>
    /// Gentle levitation for world-space title text: slow bob plus a lazy
    /// three-axis sway so the logo feels alive over the menu diorama.
    /// </summary>
    public class FloatyText : MonoBehaviour
    {
        [SerializeField] float bobAmount = 0.14f;
        [SerializeField] float bobSpeed = 0.85f;
        [SerializeField] float swayYaw = 3.5f;
        [SerializeField] float swayRoll = 2f;
        [SerializeField] float swaySpeed = 0.5f;

        Vector3 basePos;
        Quaternion baseRot;

        void Start()
        {
            basePos = transform.localPosition;
            baseRot = transform.localRotation;
        }

        void Update()
        {
            float t = Time.time;
            transform.localPosition = basePos + Vector3.up * (Mathf.Sin(t * bobSpeed) * bobAmount);
            transform.localRotation = baseRot * Quaternion.Euler(
                Mathf.Sin(t * swaySpeed * 0.7f) * 1.6f,
                Mathf.Sin(t * swaySpeed) * swayYaw,
                Mathf.Sin(t * swaySpeed * 0.83f) * swayRoll);
        }
    }
}
