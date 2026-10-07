using UnityEngine;
using WarriorRun.Core;
using WarriorRun.World;

namespace WarriorRun.Player
{
    /// <summary>
    /// Owns the car & plane special abilities on the runner: swaps the knight
    /// visual for a vehicle rig, drives engine audio (stinger + seamless loop),
    /// spins wheels/prop, banks on lane changes and runs the speed FX.
    /// Rigs are wired by the editor builder and live as children of the player.
    /// </summary>
    public class SpecialVehicleController : MonoBehaviour
    {
        [Header("Wiring (builder)")]
        public GameObject carRig;
        public GameObject planeRig;
        public GameObject visualRoot;          // the knight's Visual child
        public Transform[] carWheels;
        public ParticleSystem speedLines;
        public ParticleSystem exhaust;

        [Header("Feel")]
        [SerializeField] float wheelRadius = 0.36f;
        [SerializeField] float bankDeg = 14f;
        [SerializeField] float carShake = 0.018f;
        [SerializeField] float planeBobAmp = 0.09f;
        [SerializeField] float planeBobSpeed = 2.2f;
        [SerializeField] float engineVolume = 0.6f;

        AudioSource engine;
        AudioClip carLoop;
        AudioClip planeLoop;
        PlayerController rider;

        bool car;
        bool plane;
        Vector3 prevPos;
        float bank;
        float bobPhase;
        bool enginePaused;

        void Awake()
        {
            engine = gameObject.AddComponent<AudioSource>();
            engine.playOnAwake = false;
            engine.spatialBlend = 0f;
            engine.loop = true;
            engine.volume = engineVolume;
            carLoop = Resources.Load<AudioClip>("Audio/sfx_carloop");
            planeLoop = Resources.Load<AudioClip>("Audio/sfx_planeloop");
            rider = GetComponent<PlayerController>();
            prevPos = transform.position;
        }

        void Update()
        {
            var gm = GameManager.Instance;
            // stay mounted through pause — only dismount on death, menu or expiry
            bool alive = gm != null && gm.State != RunState.Dead && gm.State != RunState.Ready;

            // hanging from the zip trolley means the knight visual — a
            // mounted car/plane can't ride the rope; the timers still run,
            // so a long-lived vehicle remounts after the landing
            bool zipping = rider != null && rider.IsZipping;
            bool wantCar = alive && !zipping && gm.CarActive;
            bool wantPlane = alive && !zipping && gm.PlaneActive;
            if (wantCar != car) SetCar(wantCar);
            if (wantPlane != plane) SetPlane(wantPlane);

            // pause silences the engine loop, resume brings it back
            if (gm != null && gm.State == RunState.Paused)
            {
                if (engine.isPlaying) { engine.Pause(); enginePaused = true; }
            }
            else if (enginePaused)
            {
                enginePaused = false;
                if (car || plane) engine.UnPause();
            }

            // engine note climbs with road speed
            if (engine.isPlaying && gm != null)
                engine.pitch = Mathf.Lerp(0.85f, 1.45f,
                    Mathf.InverseLerp(9f, gm.MaxSpeed * 1.6f, gm.CurrentSpeed));

            // lateral velocity -> bank into the lane change (heading-relative,
            // so banking still reads correctly after a corner)
            float dt = Mathf.Max(Time.deltaTime, 1e-5f);
            float vx = Vector3.Dot(transform.position - prevPos, transform.right) / dt;
            prevPos = transform.position;
            bank = Mathf.Lerp(bank, Mathf.Clamp(-vx * 2.4f, -bankDeg, bankDeg), 8f * Time.deltaTime);

            if (car && carRig != null)
            {
                float spin = (gm != null ? gm.CurrentSpeed : 20f) / wheelRadius * Mathf.Rad2Deg * Time.deltaTime;
                if (carWheels != null)
                    foreach (var w in carWheels)
                        if (w != null) w.Rotate(spin, 0f, 0f, Space.Self);

                // chassis trembles at speed, body rolls into lane changes
                carRig.transform.localPosition = new Vector3(0f, Random.Range(-carShake, carShake), 0f);
                carRig.transform.localEulerAngles = new Vector3(0f, 0f, bank * 0.55f);
            }

            if (plane && planeRig != null)
            {
                bobPhase += Time.deltaTime * planeBobSpeed;
                planeRig.transform.localPosition = new Vector3(0f, Mathf.Sin(bobPhase) * planeBobAmp, 0f);
                planeRig.transform.localEulerAngles =
                    new Vector3(Mathf.Sin(bobPhase * 0.7f) * 3f, 0f, bank);
            }
        }

        void SetCar(bool on)
        {
            car = on;
            if (carRig != null) carRig.SetActive(on);
            if (on)
            {
                if (plane) { plane = false; if (planeRig != null) planeRig.SetActive(false); }
                if (visualRoot != null) visualRoot.SetActive(false);
                AudioManager.Instance?.Play(Sfx.Car);
                PlayEngine(carLoop);
                var gm = GameManager.Instance;
                if (gm != null) VehicleTint.ApplyRig(carRig, gm.VehicleA, gm.VehicleB, Random.value < 0.5f);
                if (speedLines != null) speedLines.Play();
                if (exhaust != null) exhaust.Play();
            }
            else
            {
                RestoreIfIdle();
            }
        }

        void SetPlane(bool on)
        {
            plane = on;
            if (planeRig != null) planeRig.SetActive(on);
            if (on)
            {
                if (car) { car = false; if (carRig != null) carRig.SetActive(false); }
                if (visualRoot != null) visualRoot.SetActive(false);
                AudioManager.Instance?.Play(Sfx.Plane);
                PlayEngine(planeLoop);
                var gm = GameManager.Instance;
                if (gm != null) VehicleTint.ApplyRig(planeRig, gm.VehicleA, gm.VehicleB, Random.value < 0.5f);
                if (speedLines != null) speedLines.Play();
            }
            else
            {
                RestoreIfIdle();
            }
        }

        void RestoreIfIdle()
        {
            if (car || plane) return;
            if (visualRoot != null) visualRoot.SetActive(true);
            engine.Stop();
            enginePaused = false;
            if (speedLines != null && speedLines.isPlaying) speedLines.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (exhaust != null && exhaust.isPlaying) exhaust.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        void PlayEngine(AudioClip clip)
        {
            if (clip == null) return;
            engine.Stop();
            engine.clip = clip;
            engine.volume = engineVolume;
            engine.pitch = 0.85f;
            if (SaveSystem.SoundOn) engine.Play();
        }

        void OnDisable()
        {
            engine.Stop();
            enginePaused = false;
        }
    }
}
