using UnityEngine;
using WarriorRun.Core;

namespace WarriorRun.World
{
    /// <summary>
    /// Chase camera: follows behind the player with lateral smoothing,
    /// speed-scaled FOV kick, and a small shake on death.
    /// </summary>
    public class RunnerCamera : MonoBehaviour
    {
        [SerializeField] Vector3 offset = new(0f, 3.4f, -5.2f);
        [SerializeField] float lookAheadZ = 8f;
        [SerializeField] float followXSmooth = 8f;
        [SerializeField] float followYSmooth = 5f;
        [SerializeField] float baseFov = 58f;
        [SerializeField] float fovKick = 8f;
        [SerializeField] float carFovAdd = 7f;      // extra wide-angle kick while driving
        [SerializeField] float carRumble = 0.04f;   // engine shake while driving

        Transform target;
        Camera cam;
        float shake;
        float smX;
        float smY;
        bool shookOnDeath;

        void Start()
        {
            cam = GetComponent<Camera>();
            var pc = FindFirstObjectByType<Player.PlayerController>();
            target = pc != null ? pc.transform : null;
            if (target != null)
            {
                smX = 0f;
                transform.position = new Vector3(smX, offset.y, target.position.z + offset.z);
            }
        }

        void LateUpdate()
        {
            if (target == null) return;
            var gm = GameManager.Instance;

            smX = Mathf.Lerp(smX, target.position.x * 0.55f, followXSmooth * Time.deltaTime);
            // track target height so the plane stays framed while airborne
            smY = Mathf.Lerp(smY, target.position.y, followYSmooth * Time.deltaTime);
            float z = target.position.z + offset.z;
            Vector3 pos = new(smX, offset.y + smY, z);

            if (shake > 0f)
            {
                pos += Random.insideUnitSphere * shake * 0.25f;
                shake = Mathf.MoveTowards(shake, 0f, Time.deltaTime * 4f);
            }
            else if (gm != null && gm.CarActive)
                pos += Random.insideUnitSphere * carRumble; // chassis vibration

            transform.position = pos;
            var look = new Vector3(smX * 0.4f, offset.y - 2.2f + smY, target.position.z + lookAheadZ);
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
