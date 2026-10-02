using UnityEngine;

namespace WarriorRun.World
{
    /// <summary>
    /// Procedural idle animation for obstacles and decor — sway, rock, bob,
    /// squash and spin channels that compose freely. Animates a child target
    /// (usually the model) so the root's trigger collider never moves.
    /// Phase is seeded by world position, so pooled siblings never sync up.
    /// </summary>
    public class ObstacleAnim : MonoBehaviour
    {
        public Transform target;                    // null = this transform

        [Header("Sway — fast tilt wobble (wind shiver, alarm rattle)")]
        public Vector3 swayAxis = new Vector3(0f, 0f, 1f);
        public float swayDeg;
        public float swayFreq = 2f;

        [Header("Rock — slower seesaw on a second axis")]
        public Vector3 rockAxis = new Vector3(1f, 0f, 0f);
        public float rockDeg;
        public float rockFreq = 1.2f;

        [Header("Bob — vertical float")]
        public float bobAmp;
        public float bobFreq = 2.4f;

        [Header("Squash — breathing scale (+Y, −XZ)")]
        public float squashAmp;
        public float squashFreq = 2.2f;

        [Header("Spin — continuous turn in the target's own space")]
        public Vector3 spinAxis = Vector3.up;
        public float spinDegPerSec;

        Vector3 basePos, baseScale;
        Quaternion baseRot;
        float phase;

        void OnEnable()
        {
            var t = target != null ? target : transform;
            basePos = t.localPosition;
            baseRot = t.localRotation;
            baseScale = t.localScale;
            var p = transform.position;
            phase = Mathf.Repeat(p.x * 12.9898f + p.z * 78.233f, Mathf.PI * 2f);
        }

        void OnDisable()
        {
            var t = target != null ? target : transform;
            t.localPosition = basePos;
            t.localRotation = baseRot;
            t.localScale = baseScale;
        }

        void Update()
        {
            var t = target != null ? target : transform;
            float time = Time.time;

            var rot = baseRot;
            if (swayDeg != 0f)
                rot = Quaternion.AngleAxis(Mathf.Sin(time * swayFreq + phase) * swayDeg, swayAxis) * rot;
            if (rockDeg != 0f)
                rot = Quaternion.AngleAxis(Mathf.Sin(time * rockFreq + phase * 1.31f) * rockDeg, rockAxis) * rot;
            if (spinDegPerSec != 0f)
                rot *= Quaternion.AngleAxis(time * spinDegPerSec + phase * 57.3f, spinAxis);
            t.localRotation = rot;

            if (bobAmp != 0f)
            {
                var lp = basePos;
                lp.y += Mathf.Sin(time * bobFreq + phase * 1.73f) * bobAmp;
                t.localPosition = lp;
            }

            if (squashAmp != 0f)
            {
                float s = Mathf.Sin(time * squashFreq + phase) * squashAmp;
                t.localScale = new Vector3(
                    baseScale.x * (1f - s * 0.7f),
                    baseScale.y * (1f + s),
                    baseScale.z * (1f - s * 0.7f));
            }
        }
    }
}
