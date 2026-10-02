using UnityEngine;

namespace WarriorRun.World
{
    /// <summary>
    /// Spins a transform around its local axis — propellers, rotors.
    /// Eases in from standstill so activation feels like a spin-up.
    /// </summary>
    public class PropSpin : MonoBehaviour
    {
        public Vector3 axis = Vector3.forward;
        public float degreesPerSecond = 1600f;
        public float rampTime = 0.9f;

        float speedMul;

        void OnEnable() => speedMul = 0f;

        void Update()
        {
            speedMul = Mathf.MoveTowards(speedMul, 1f, Time.deltaTime / Mathf.Max(0.05f, rampTime));
            transform.Rotate(axis.normalized, degreesPerSecond * speedMul * Time.deltaTime, Space.Self);
        }
    }
}
