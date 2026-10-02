using UnityEngine;
using WarriorRun.Core;

namespace WarriorRun.World
{
    /// <summary>
    /// Googly eyes for obstacles: pupils slide toward the runner (or wander
    /// while waiting) and the whites snap shut in quick random blinks.
    /// Pupils must be children of their eye whites so a blink takes the
    /// pupil with it. Phase is seeded by world position for pool desync.
    /// </summary>
    public class ObstacleEyes : MonoBehaviour
    {
        public Transform eyeL, eyeR;       // white spheres — blink squash
        public Transform pupilL, pupilR;   // pupils — look-at slide
        public float pupilTravel = 0.20f;  // pupil slide range, in eye-local units
        public Vector2 blinkGap = new Vector2(1.6f, 4.6f);
        public float blinkTime = 0.15f;

        Vector3 pupilL0, pupilR0, eyeScaleL, eyeScaleR;
        float nextBlink, blinkT = -1f, seed;

        void OnEnable()
        {
            if (pupilL != null) pupilL0 = pupilL.localPosition;
            if (pupilR != null) pupilR0 = pupilR.localPosition;
            if (eyeL != null) eyeScaleL = eyeL.localScale;
            if (eyeR != null) eyeScaleR = eyeR.localScale;
            var p = transform.position;
            seed = Mathf.Repeat(p.x * 7.13f + p.z * 3.71f, 10f);
            nextBlink = Time.time + Mathf.Lerp(blinkGap.x, blinkGap.y, seed / 10f);
            blinkT = -1f;
            SetEyes(1f);
        }

        void Update()
        {
            var player = GameManager.Instance != null ? GameManager.Instance.PlayerTf : null;
            Aim(eyeL, pupilL, pupilL0, player);
            Aim(eyeR, pupilR, pupilR0, player);

            if (blinkT >= 0f)
            {
                blinkT += Time.deltaTime;
                float k = blinkT / blinkTime;
                if (k >= 1f) { blinkT = -1f; SetEyes(1f); }
                else SetEyes(1f - Mathf.Sin(k * Mathf.PI) * 0.88f);
            }
            else if (Time.time >= nextBlink)
            {
                blinkT = 0f;
                nextBlink = Time.time + Random.Range(blinkGap.x, blinkGap.y);
            }
        }

        void Aim(Transform eye, Transform pupil, Vector3 rest, Transform player)
        {
            if (eye == null || pupil == null) return;
            Vector2 off;
            if (player != null)
            {
                var d = eye.InverseTransformDirection(player.position - eye.position);
                off = new Vector2(d.x, d.y);
                float m = Mathf.Max(Mathf.Abs(off.x), Mathf.Abs(off.y));
                off = m > 1e-3f ? off / m * pupilTravel : Vector2.zero;
            }
            else
            {
                off = new Vector2(Mathf.PerlinNoise(seed, Time.time * 0.6f) - 0.5f,
                                  Mathf.PerlinNoise(seed + 9.3f, Time.time * 0.6f) - 0.5f)
                      * (pupilTravel * 2f);
            }
            pupil.localPosition = rest + new Vector3(off.x, off.y, 0f);
        }

        void SetEyes(float y)
        {
            if (eyeL != null) eyeL.localScale = new Vector3(eyeScaleL.x, eyeScaleL.y * y, eyeScaleL.z);
            if (eyeR != null) eyeR.localScale = new Vector3(eyeScaleR.x, eyeScaleR.y * y, eyeScaleR.z);
        }
    }
}
