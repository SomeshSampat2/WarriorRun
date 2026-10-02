using UnityEngine;
using WarriorRun.Core;

namespace WarriorRun.Player
{
    /// <summary>
    /// Procedural animation for the blocky runner rig. Drives limb pivots,
    /// body bob/lean, jump tuck, slide pose, landing squash and a death tumble.
    /// Pivots are wired up by the editor builder by name.
    /// </summary>
    public class RunnerAnimator : MonoBehaviour, IRunnerAnim
    {
        [Header("Rig pivots (wired by builder)")]
        public Transform body;
        public Transform head;
        public Transform armL, armR;
        public Transform legL, legR;

        [Header("Run cycle")]
        [SerializeField] float legSwingDeg = 52f;
        [SerializeField] float armSwingDeg = 42f;
        [SerializeField] float bobHeight = 0.07f;
        [SerializeField] float forwardLeanDeg = 8f;

        CharacterController cc;
        PlayerController pc;
        float cycle;
        float bodyY0;
        float squash = 1f;        // 1 = normal, <1 squash, >1 stretch
        float leanX;              // current forward lean
        float rollZ;              // lateral lean on lane switch
        float targetRoll;
        bool wasGrounded = true;
        bool dead;
        float deadT;
        Vector3 deadStartPos;

        void Awake()
        {
            cc = GetComponentInParent<CharacterController>();
            pc = GetComponentInParent<PlayerController>();
            if (body != null) bodyY0 = body.localPosition.y;
        }

        public void OnRunStart() => squash = 1.12f;
        public void OnJump() => squash = 1.22f;
        public void OnSlide(bool on) { if (on) squash = 0.8f; }
        public void OnLaneSwitch(int dir) => targetRoll = -dir * 9f;
        public void OnLand() => squash = 0.74f;

        public void OnDeath()
        {
            dead = true;
            deadT = 0f;
            if (body != null) deadStartPos = body.localPosition;
        }

        void LateUpdate()
        {
            if (dead) { AnimateDeath(); return; }

            var gm = GameManager.Instance;
            bool running = gm != null && gm.State == RunState.Running;

            if (running)
            {
                float stride = Mathf.Max(8f, gm.CurrentSpeed * 0.9f);
                cycle += Time.deltaTime * stride * 0.6f;
            }
            else
            {
                cycle += Time.deltaTime * 2f; // gentle idle
            }

            float s = Mathf.Sin(cycle * Mathf.PI);
            bool grounded = cc == null || cc.isGrounded;
            bool sliding = pc != null && pc.IsSliding;

            if (grounded && !wasGrounded) squash = 0.74f; // landing squash
            wasGrounded = grounded;

            // legs + arms
            if (!grounded)
            {
                // air pose: legs tucked, arms raised
                SetX(legL, Mathf.Lerp(legL.localEulerAngles.x.ToSigned(), -70f, 12f * Time.deltaTime));
                SetX(legR, Mathf.Lerp(legR.localEulerAngles.x.ToSigned(), -25f, 12f * Time.deltaTime));
                SetX(armL, Mathf.Lerp(armL.localEulerAngles.x.ToSigned(), 60f, 10f * Time.deltaTime));
                SetX(armR, Mathf.Lerp(armR.localEulerAngles.x.ToSigned(), 60f, 10f * Time.deltaTime));
            }
            else if (sliding)
            {
                SetX(legL, Mathf.Lerp(legL.localEulerAngles.x.ToSigned(), -75f, 18f * Time.deltaTime));
                SetX(legR, Mathf.Lerp(legR.localEulerAngles.x.ToSigned(), -75f, 18f * Time.deltaTime));
                SetX(armL, Mathf.Lerp(armL.localEulerAngles.x.ToSigned(), 45f, 18f * Time.deltaTime));
                SetX(armR, Mathf.Lerp(armR.localEulerAngles.x.ToSigned(), 45f, 18f * Time.deltaTime));
            }
            else
            {
                float legAmp = running ? legSwingDeg : 4f;
                float armAmp = running ? armSwingDeg : 3f;
                SetX(legL, s * legAmp);
                SetX(legR, -s * legAmp);
                SetX(armL, -s * armAmp);
                SetX(armR, s * armAmp);
            }

            // body bob + lean + squash
            if (body != null)
            {
                float bob = grounded && running ? Mathf.Abs(Mathf.Sin(cycle * Mathf.PI)) * bobHeight : 0f;
                float slideDrop = sliding ? -0.42f : 0f;
                var p = body.localPosition;
                p.y = Mathf.Lerp(p.y, bodyY0 + bob + slideDrop, 20f * Time.deltaTime);
                body.localPosition = p;

                squash = Mathf.Lerp(squash, 1f, 10f * Time.deltaTime);
                body.localScale = new Vector3(1f + (1f - squash) * 0.6f, squash, 1f + (1f - squash) * 0.6f);
            }

            float leanTarget = running ? forwardLeanDeg : 0f;
            if (sliding) leanTarget = -28f;
            else if (!grounded) leanTarget = -6f;
            leanX = Mathf.Lerp(leanX, leanTarget, 8f * Time.deltaTime);
            rollZ = Mathf.Lerp(rollZ, targetRoll, 10f * Time.deltaTime);
            targetRoll = Mathf.Lerp(targetRoll, 0f, 6f * Time.deltaTime);
            transform.localEulerAngles = new Vector3(leanX, 0f, rollZ);

            // head counter-bob
            if (head != null)
                head.localEulerAngles = new Vector3(-leanX * 0.5f, 0f, 0f);
        }

        void AnimateDeath()
        {
            deadT += Time.deltaTime;
            if (body == null) return;
            float t = Mathf.Clamp01(deadT * 3f);
            float e = 1f - (1f - t) * (1f - t);
            transform.localEulerAngles = new Vector3(-70f * e, 0f, rollZ);
            var p = body.localPosition;
            p.y = Mathf.Lerp(deadStartPos.y, bodyY0 - 0.55f, e);
            body.localPosition = p;
            SetX(armL, 80f); SetX(armR, 80f);
        }

        static void SetX(Transform t, float xDeg)
        {
            if (t == null) return;
            var e = t.localEulerAngles;
            e.x = xDeg;
            t.localEulerAngles = e;
        }
    }

    static class AngleExt
    {
        public static float ToSigned(this float deg)
        {
            deg %= 360f;
            if (deg > 180f) deg -= 360f;
            return deg;
        }
    }
}
