using UnityEngine;
using WarriorRun.Core;

namespace WarriorRun.World
{
    /// <summary>
    /// Chase camera: sits behind the runner in the current heading frame.
    /// The smoothed yaw lags a beat through corners, which gives the
    /// signature Temple Run swing instead of a rigid snap.
    /// </summary>
    public class RunnerCamera : MonoBehaviour
    {
        [SerializeField] Vector3 offset = new(0f, 3.4f, -5.2f);
        [SerializeField] float lookAheadZ = 8f;
        [SerializeField] float followXSmooth = 8f;
        [SerializeField] float followYSmooth = 5f;
        [SerializeField] float followYawSmooth = 4.2f; // corner swing — lower = wider sweep
        [SerializeField] float baseFov = 58f;
        [SerializeField] float fovKick = 8f;
        [SerializeField] float carFovAdd = 7f;      // extra wide-angle kick while driving
        [SerializeField] float carRumble = 0.04f;   // engine shake while driving

        Transform target;
        Player.PlayerController pc;
        Camera cam;
        float shake;
        float smX;
        float smY;
        float smYaw;
        bool shookOnDeath;

        void Start()
        {
            cam = GetComponent<Camera>();
            pc = FindFirstObjectByType<Player.PlayerController>();
            target = pc != null ? pc.transform : null;
            if (target != null)
            {
                smX = 0f;
                smYaw = pc.HeadingYaw;
                transform.position = target.position + Quaternion.Euler(0f, smYaw, 0f) * offset;
            }
        }

        void LateUpdate()
        {
            if (target == null) return;
            var gm = GameManager.Instance;

            float yaw = pc != null ? pc.HeadingYaw : 0f;
            smYaw += Mathf.DeltaAngle(smYaw, yaw) * Mathf.Min(1f, followYawSmooth * Time.deltaTime);
            var rot = Quaternion.Euler(0f, smYaw, 0f);

            // the runner's lane offset measured against the path spine —
            // the camera slides ~halfway along it, like the original camera
            Vector3 spine = pc != null
                ? target.position - pc.RightDir * pc.Lateral
                : target.position;
            float lat = pc != null ? pc.Lateral : 0f;
            smX = Mathf.Lerp(smX, lat * 0.55f, followXSmooth * Time.deltaTime);
            // track target height so the plane stays framed while airborne
            smY = Mathf.Lerp(smY, spine.y, followYSmooth * Time.deltaTime);

            Vector3 pos = spine + rot * new Vector3(smX, offset.y + smY - spine.y, offset.z);

            if (shake > 0f)
            {
                pos += Random.insideUnitSphere * shake * 0.25f;
                shake = Mathf.MoveTowards(shake, 0f, Time.deltaTime * 4f);
            }
            else if (gm != null && gm.CarActive)
                pos += Random.insideUnitSphere * carRumble; // chassis vibration

            transform.position = pos;
            // look point leads a little less than the position — the subtle
            // lateral delta tilts the view toward the lane you're in, the
            // same lead-in the pre-turns camera had
            Vector3 look = spine + rot * new Vector3(smX * 0.4f, offset.y - 2.2f + smY - spine.y, lookAheadZ);
            transform.rotation = Quaternion.LookRotation(look - pos, Vector3.up);

            if (cam != null && gm != null)
            {
                float t = Mathf.InverseLerp(9f, gm.MaxSpeed, gm.CurrentSpeed);
                float target = baseFov + t * fovKick + (gm.CarActive ? carFovAdd : 0f);
                cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, target, 4f * Time.deltaTime);
            }

            if (gm != null && gm.State == RunState.Dead && !shookOnDeath)
            {
                shookOnDeath = true;
                Shake(1.4f);
            }
        }

        public void Shake(float amount = 1f) => shake = amount;
    }
}
