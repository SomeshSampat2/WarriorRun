using System.Collections.Generic;
using UnityEngine;
using WarriorRun.Core;
using WarriorRun.World;

namespace WarriorRun.Player
{
    /// <summary>
    /// Owns every on-runner special effect: airborne heel ribbons, boost
    /// afterimages + foot flames, the shield bubble, magnet ground ring,
    /// ghost-phase material swap, spring-boot sparks and giant-mode
    /// growth/stomps. All targets are wired by the editor builder; every
    /// effect idles while the run isn't live.
    /// </summary>
    public class RunnerFx : MonoBehaviour
    {
        [Header("Wiring (builder)")]
        public Transform visualRoot;        // the knight's Visual child
        public TrailRenderer[] airTrails;   // heel ribbons — emit only while airborne
        public ParticleSystem boostFlames;  // foot fire while boosting
        public ParticleSystem springSwirl;  // orbit sparks while spring boots live
        public ParticleSystem shieldShards; // one-shot burst on shield break
        public Renderer shieldBubble;       // translucent dome while a shield is held
        public ParticleSystem magnetSwirl;  // sparks spiralling into the runner
        public Material ghostMat;           // phase swap — translucent, flickers
        public Material afterMat;           // afterimage silhouettes — additive

        [Header("Afterimages")]
        [SerializeField] float ghostInterval = 0.06f;
        [SerializeField] float ghostLife = 0.5f;
        [SerializeField] Color ghostTint = new Color(0.35f, 0.85f, 1f, 0.5f);

        [Header("Giant")]
        [SerializeField] float giantScale = 2.05f;
        [SerializeField] float growSpeed = 5f;
        [SerializeField] float stompShake = 0.11f;

        CharacterController cc;
        RunnerCamera cam;
        float ghostTimer;
        float stompTimer;
        bool stompFlip;
        float phaseFlash;
        bool hadShield;
        bool ghostApplied;
        float giantCur = 1f;
        Vector3 bubbleScale;
        readonly List<(Renderer r, Material[] mats)> swapped = new();
        readonly Queue<Mesh> bakedPool = new();

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            if (shieldBubble != null)
            {
                bubbleScale = shieldBubble.transform.localScale;
                shieldBubble.gameObject.SetActive(false);
            }
        }

        void OnDisable() => RestorePhase();

        /// <summary>Quick shimmer spike when a ghosted runner phases through a blocker.</summary>
        public void OnPhaseThrough() => phaseFlash = 1f;

        void Update()
        {
            var gm = GameManager.Instance;
            bool running = gm != null && gm.State == RunState.Running;
            bool vehicle = gm != null && gm.VehicleActive;
            bool grounded = cc == null || cc.isGrounded;

            UpdateTrails(running, grounded, vehicle);
            UpdateShield(gm, running);
            UpdateMagnet(gm, running);
            UpdateFlames(gm, running);
            UpdateGhosts(gm, running);
            UpdatePhase(gm, running);
            UpdateGiant(gm, running, grounded);

            phaseFlash = Mathf.Max(0f, phaseFlash - Time.deltaTime * 4f);
        }

        // ---- airborne heel ribbons ----

        void UpdateTrails(bool running, bool grounded, bool vehicle)
        {
            bool on = running && !grounded && !vehicle;
            if (airTrails == null) return;
            foreach (var t in airTrails)
                if (t != null && t.emitting != on) t.emitting = on;
        }

        // ---- shield bubble ----

        void UpdateShield(GameManager gm, bool running)
        {
            bool on = running && gm.ShieldActive;
            if (shieldBubble != null)
            {
                if (shieldBubble.gameObject.activeSelf != on)
                    shieldBubble.gameObject.SetActive(on);
                if (on)
                {
                    float p = 1f + Mathf.Sin(Time.time * 4.2f) * 0.035f;
                    shieldBubble.transform.localScale = bubbleScale * p;
                }
            }
            // consumed mid-run -> glass shards where the dome was
            if (hadShield && (gm == null || !gm.ShieldActive) && shieldShards != null)
                shieldShards.Play();
            hadShield = gm != null && gm.ShieldActive;
        }

        // ---- magnet ring ----

        void UpdateMagnet(GameManager gm, bool running)
        {
            bool on = running && gm.MagnetActive;
            // the pull is the tell — sparks spiralling into the runner
            SetRate(magnetSwirl, on ? 44f * Flick(gm, PowerUpKind.Magnet) : 0f);
        }

        // ---- boost flames + spring sparks ----

        void UpdateFlames(GameManager gm, bool running)
        {
            SetRate(boostFlames, running && gm.BoostActive ? 46f * Flick(gm, PowerUpKind.Boost) : 0f);
            SetRate(springSwirl, running && gm.SpringActive ? 26f * Flick(gm, PowerUpKind.Spring) : 0f);
        }

        static void SetRate(ParticleSystem ps, float want)
        {
            if (ps == null) return;
            var em = ps.emission;
            if (want <= 0f)
            {
                if (em.rateOverTime.constant > 0f) em.rateOverTime = 0f;
                return;
            }
            if (!ps.isPlaying) ps.Play();
            em.rateOverTime = want;
        }

        // ---- boost afterimages ----

        void UpdateGhosts(GameManager gm, bool running)
        {
            bool spawning = running && gm.BoostActive && !gm.VehicleActive;
            ghostTimer -= Time.deltaTime;
            if (!spawning || ghostTimer > 0f || visualRoot == null || afterMat == null) return;
            ghostTimer = ghostInterval;
            foreach (var smr in visualRoot.GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                if (!smr.enabled) continue;
                var mesh = bakedPool.Count > 0 ? bakedPool.Dequeue() : new Mesh();
                smr.BakeMesh(mesh);
                SpawnGhost(mesh, smr.transform, mesh);
            }
            foreach (var mr in visualRoot.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (!mr.enabled) continue;
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                SpawnGhost(mf.sharedMesh, mr.transform, null);
            }
        }

        void SpawnGhost(Mesh mesh, Transform src, Mesh baked)
        {
            var go = new GameObject("Afterimage");
            go.transform.SetPositionAndRotation(src.position, src.rotation);
            go.transform.localScale = src.lossyScale;
            go.AddComponent<GhostAfterimage>().Init(mesh, afterMat, ghostLife, ghostTint, bakedPool, baked);
        }

        // ---- ghost phase ----

        void UpdatePhase(GameManager gm, bool running)
        {
            bool want = running && gm.GhostActive;
            if (want != ghostApplied)
            {
                ghostApplied = want;
                if (ghostApplied) ApplyPhase(); else RestorePhase();
            }
            if (ghostApplied && ghostMat != null)
            {
                float a = (0.30f + Mathf.Sin(Time.time * 5.2f) * 0.05f + phaseFlash * 0.3f)
                          * Flick(gm, PowerUpKind.Ghost);
                ghostMat.SetColor("_BaseColor", new Color(0.45f, 0.92f, 1f, a));
                ghostMat.SetColor("_EmissionColor", new Color(0.35f, 0.85f, 1f) * (0.4f + a));
            }
        }

        void ApplyPhase()
        {
            if (visualRoot == null || ghostMat == null) return;
            swapped.Clear();
            foreach (var r in visualRoot.GetComponentsInChildren<Renderer>(false))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer) continue;
                swapped.Add((r, r.sharedMaterials));
                var arr = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < arr.Length; i++) arr[i] = ghostMat;
                r.sharedMaterials = arr;
            }
        }

        void RestorePhase()
        {
            foreach (var (r, mats) in swapped)
                if (r != null) r.sharedMaterials = mats;
            swapped.Clear();
        }

        // ---- giant mode ----

        void UpdateGiant(GameManager gm, bool running, bool grounded)
        {
            float want = running && gm.GiantActive ? giantScale : 1f;
            giantCur = Mathf.MoveTowards(giantCur, want, growSpeed * Time.deltaTime);
            if (visualRoot != null && Mathf.Abs(visualRoot.localScale.x - giantCur) > 0.001f)
                visualRoot.localScale = Vector3.one * giantCur;

            if (!running || !gm.GiantActive || !grounded) return;
            stompTimer -= Time.deltaTime;
            if (stompTimer <= 0f)
            {
                // cadence tracks run speed — heavier thud every other step
                stompTimer = Mathf.Clamp(2.8f / gm.CurrentSpeed, 0.11f, 0.34f);
                if (cam == null) cam = FindFirstObjectByType<RunnerCamera>();
                cam?.Shake(stompShake);
                stompFlip = !stompFlip;
                if (stompFlip) AudioManager.Instance?.Play(Sfx.Stomp);
            }
        }

        /// <summary>1 normally, strobing inside the expiry warning window.</summary>
        static float Flick(GameManager gm, PowerUpKind k)
            => gm != null && gm.PowerUpWarning(k)
                ? (Mathf.PingPong(Time.time * 9f, 2f) < 1f ? 1f : 0.25f) : 1f;
    }
}
