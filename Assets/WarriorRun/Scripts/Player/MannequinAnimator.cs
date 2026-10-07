using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using WarriorRun.Core;

namespace WarriorRun.Player
{
    /// <summary>
    /// Drives the rigged KayKit knight without an AnimatorController:
    /// a PlayableGraph mixes the embedded clips with weighted crossfades —
    /// deterministic, allocation-free, and immune to controller-asset quirks.
    ///
    /// States: Idle / Run / JumpStart / Airborne / Land / Slide(roll) /
    /// Dodge L/R / Death / Start(wake-up) plus menu-showcase extras
    /// (Cheer / PickUp / Interact / UnarmedIdle).
    /// Layered on top: forward lean, lane roll, landing squash, dust trail.
    /// </summary>
    public class MannequinAnimator : MonoBehaviour, IRunnerAnim
    {
        [Header("Wiring (builder)")]
        public Animator anim;
        public Transform leanPivot;      // gets lean/roll rotation
        public Transform squashPivot;    // gets landing squash scale
        public ParticleSystem dust;      // optional run-dust trail
        public bool menuShowcase;        // menu scene: cycle showcase clips

        [Header("Clips (builder)")]
        public AnimationClip clipIdle;
        public AnimationClip clipRun;
        public AnimationClip clipJumpStart;
        public AnimationClip clipAirborne;
        public AnimationClip clipLand;
        public AnimationClip clipSlide;      // Dodge_Forward roll
        public AnimationClip clipStrafeL;    // Dodge_Left
        public AnimationClip clipStrafeR;    // Dodge_Right
        public AnimationClip clipDeath;
        public AnimationClip clipStart;      // Lie_StandUp — run intro
        public AnimationClip clipIdleB;      // Unarmed_Idle variation
        public AnimationClip clipCheer;
        public AnimationClip clipPickUp;
        public AnimationClip clipInteract;
        public AnimationClip clipHit;
        public AnimationClip clipReady;      // 2H_Melee_Idle — combat stance while waiting

        [Header("Feel")]
        [SerializeField] float forwardLeanDeg = 7f;
        [SerializeField] float laneRollDeg = 9f;
        [SerializeField] float landDip = 0.72f;
        [SerializeField] float strafeHold = 0.42f;
        [SerializeField] float slideSpeed = 0.62f;
        [SerializeField] float turnYawDeg = 24f;   // torso twists toward the exit mid-corner
        [SerializeField] float turnLeanDeg = 14f;  // body banks into the bend

        const int S_Idle = 0, S_Run = 1, S_Jump = 2, S_Air = 3, S_Land = 4,
                  S_Slide = 5, S_DodgeL = 6, S_DodgeR = 7, S_Death = 8,
                  S_Start = 9, S_IdleB = 10, S_Cheer = 11, S_PickUp = 12,
                  S_Interact = 13, S_Hit = 14, S_Ready = 15, Count = 16;

        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        AnimationClipPlayable[] plays = new AnimationClipPlayable[Count];
        float[] fadeFrom = new float[Count];
        float fadeT, fadeDur = 0.001f;
        int current = -1, target = -1;

        CharacterController cc;
        PlayerController pc;

        bool dead;
        bool airborne;
        bool starting;          // Start clip is playing out
        float dodgeT;
        int dodgeDir;
        float landTimer;
        float rollZ, targetRoll;
        float leanX;
        float turnYaw, turnRoll;
        float squash = 1f;
        float flip;                 // remaining degrees of the double-jump tumble
        const float flipSpeed = 620f;
        bool wasGrounded = true;

        // zip-hang pose — arm bones steered onto the trolley grip every frame
        // (FromToRotation is axis-free, so no assumption about rig conventions)
        Transform uaL, uaR, laL, laR, spineB, headB, legUL, legUR;
        float zipPose;
        bool bonesSearched;

        // menu showcase cycle
        float showcaseT;
        int showcaseIdx;

        static readonly int[] ShowcaseCycle = { S_Idle, S_Cheer, S_IdleB, S_PickUp, S_Idle, S_Interact };

        void Awake()
        {
            if (anim == null) anim = GetComponentInChildren<Animator>();
            cc = GetComponentInParent<CharacterController>();
            pc = GetComponentInParent<PlayerController>();
            BuildGraph();
        }

        void BuildGraph()
        {
            if (anim == null) return;
            var clipArr = new[] { clipIdle, clipRun, clipJumpStart, clipAirborne, clipLand, clipSlide,
                                  clipStrafeL, clipStrafeR, clipDeath, clipStart, clipIdleB, clipCheer,
                                  clipPickUp, clipInteract, clipHit, clipReady };
            graph = PlayableGraph.Create("WarriorRunPlayer");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            mixer = AnimationMixerPlayable.Create(graph, Count);
            var output = AnimationPlayableOutput.Create(graph, "RunnerAnim", anim);
            output.SetSourcePlayable(mixer);
            for (int i = 0; i < Count; i++)
            {
                if (clipArr[i] == null) continue;
                plays[i] = AnimationClipPlayable.Create(graph, clipArr[i]);
                graph.Connect(plays[i], 0, mixer, i);
                mixer.SetInputWeight(i, 0f);
            }
            graph.Play();
        }

        void Start()
        {
            // Ready pose: combat stance — sword up, weight shifting, not asleep
            current = menuShowcase ? S_Idle : S_Ready;
            for (int i = 0; i < Count; i++)
                if (plays[i].IsValid()) mixer.SetInputWeight(i, i == current ? 1f : 0f);
            if (!menuShowcase && plays[S_Ready].IsValid())
                plays[S_Ready].SetSpeed(0.6f);
            showcaseT = 2.5f;
        }

        void OnDestroy()
        {
            if (graph.IsValid()) graph.Destroy();
        }

        public void OnRunStart()
        {
            if (dead || menuShowcase) return;
            starting = true;
            if (plays[S_Start].IsValid())
            {
                plays[S_Start].SetPlayState(PlayState.Playing);
                plays[S_Start].SetTime(0);
                plays[S_Start].SetSpeed(1.25); // brisk wake-up
                FadeTo(S_Start, 0.12f);
            }
            else FadeTo(S_Run, 0.1f);
            squash = 1.12f;
        }

        public void OnJump()
        {
            if (dead || starting) return;
            squash = 1.18f;
            airborne = true;
            landTimer = 0f;
            plays[S_Jump].SetTime(0);
            plays[S_Jump].SetPlayState(PlayState.Playing);
            FadeTo(S_Jump, 0.06f);
        }

        /// <summary>Air hop: replay the jump clip while the body frontflips.</summary>
        public void OnDoubleJump()
        {
            if (dead || starting) return;
            flip = 360f;
            squash = 1.14f;
            airborne = true;
            landTimer = 0f;
            plays[S_Jump].SetTime(0);
            plays[S_Jump].SetPlayState(PlayState.Playing);
            FadeTo(S_Jump, 0.05f);
        }

        public void OnSlide(bool on)
        {
            if (dead || starting) return;
            if (on)
            {
                plays[S_Slide].SetTime(0);
                plays[S_Slide].SetPlayState(PlayState.Playing);
                plays[S_Slide].SetSpeed(slideSpeed);
                FadeTo(S_Slide, 0.10f);
                squash = 0.85f;
            }
        }

        public void OnLaneSwitch(int dir)
        {
            if (dead || starting) return;
            targetRoll = -dir * laneRollDeg;
            dodgeDir = dir;
            dodgeT = strafeHold;
            int s = dir < 0 ? S_DodgeL : S_DodgeR;
            if (plays[s].IsValid())
            {
                plays[s].SetTime(0);
                plays[s].SetPlayState(PlayState.Playing);
            }
        }

        public void OnLand() => squash = landDip;

        /// <summary>
        /// Grab/release the zipline — hold the airborne hang clip for the
        /// whole ride; release just rejoins the normal airborne→land flow.
        /// </summary>
        public void OnZip(bool on)
        {
            if (dead || starting || menuShowcase) return;
            if (on)
            {
                airborne = true;
                landTimer = 0f;
                if (plays[S_Air].IsValid())
                {
                    plays[S_Air].SetTime(0);
                    plays[S_Air].SetPlayState(PlayState.Playing);
                    FadeTo(S_Air, 0.18f);
                }
            }
            else squash = landDip; // little dip as the feet come back under
        }

        public void OnDeath()
        {
            dead = true;
            if (dust != null) dust.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            plays[S_Death].SetTime(0);
            plays[S_Death].SetPlayState(PlayState.Playing);
            FadeTo(S_Death, 0.08f);
        }

        void Update()
        {
            if (!graph.IsValid()) return;
            UpdateFeel();

            if (menuShowcase) { UpdateShowcase(); TickFade(); return; }
            if (dead) { TickFade(); return; }

            var gm = GameManager.Instance;
            bool running = gm != null && gm.State == RunState.Running;
            bool grounded = cc == null || cc.isGrounded;
            bool sliding = pc != null && pc.IsSliding;

            // start clip plays out, then hands off to run
            if (starting)
            {
                if (clipStart == null || plays[S_Start].GetTime() >= clipStart.length / 1.25 * 0.92)
                    starting = false;
                else { TickFade(); return; }
            }

            // landing: squash + brief land clip then back to run
            if (grounded && !wasGrounded && airborne)
            {
                airborne = false;
                landTimer = 0.22f;
                squash = landDip;
                plays[S_Land].SetTime(0);
                plays[S_Land].SetPlayState(PlayState.Playing);
                FadeTo(S_Land, 0.05f);
                if (dust != null) dust.Emit(6);
            }
            wasGrounded = grounded;

            // rise clip finished while still airborne -> hold the fall loop
            if (!grounded && airborne && current == S_Jump && clipJumpStart != null)
            {
                if (plays[S_Jump].GetTime() >= clipJumpStart.length * 0.92)
                    FadeTo(S_Air, 0.10f);
            }

            if (!grounded) { TickFade(); return; }
            if (landTimer > 0f) { landTimer -= Time.deltaTime; if (landTimer > 0f) { TickFade(); return; } }

            if (sliding)
            {
                Ensure(S_Slide, 0.12f);
                // hold the low point of the roll if slide outlasts the clip
                if (clipSlide != null && plays[S_Slide].GetTime() >= clipSlide.length / slideSpeed * 0.8)
                    plays[S_Slide].SetTime(clipSlide.length / slideSpeed * 0.62);
                SetDust(false);
                TickFade();
                return;
            }

            if (dodgeT > 0f && running)
            {
                dodgeT -= Time.deltaTime;
                Ensure(dodgeDir < 0 ? S_DodgeL : S_DodgeR, 0.10f);
                if (dodgeT > 0f) { TickFade(); return; }
            }

            SetDust(running);
            if (running)
                Ensure(S_Run, 0.20f);
            else
                Ensure(S_Start, 0.2f); // back to the waiting pose
            TickFade();
        }

        /// <summary>
        /// Post-graph bone steering for the zipline: the airborne clip supplies
        /// the base pose, then the arm bones aim onto the trolley grip (hands
        /// genuinely wrapped overhead), the spine arches back, chin up, legs
        /// trail — the classic hanging-ride silhouette.
        /// </summary>
        void LateUpdate()
        {
            bool want = pc != null && pc.IsZipping && !dead && !menuShowcase && !starting;
            zipPose = Mathf.MoveTowards(zipPose, want ? 1f : 0f, Time.deltaTime * 3.5f);
            if (zipPose < 0.01f) return;
            EnsureZipBones();
            if (uaL == null || pc == null) return;

            var root = pc.transform;
            Vector3 gripL = root.TransformPoint(new Vector3(-0.11f, 1.97f, 0.17f));
            Vector3 gripR = root.TransformPoint(new Vector3(0.11f, 1.97f, 0.17f));
            // upper arm carries the reach, forearm follows with a hint of bend
            Aim(uaL, laL.position, gripL, zipPose);
            Aim(uaR, laR.position, gripR, zipPose);
            if (laL.childCount > 0) Aim(laL, laL.GetChild(0).position, gripL, zipPose * 0.6f);
            if (laR.childCount > 0) Aim(laR, laR.GetChild(0).position, gripR, zipPose * 0.6f);
            // chest opens back under the cable, chin up watching the line
            if (spineB != null) spineB.rotation =
                Quaternion.AngleAxis(-8f * zipPose, root.right) * spineB.rotation;
            if (headB != null) headB.rotation =
                Quaternion.AngleAxis(-13f * zipPose, root.right) * headB.rotation;
            // legs dangle forward a touch — momentum trailing the swing
            if (legUL != null) legUL.rotation =
                Quaternion.AngleAxis(9f * zipPose, root.right) * legUL.rotation;
            if (legUR != null) legUR.rotation =
                Quaternion.AngleAxis(15f * zipPose, root.right) * legUR.rotation;
        }

        /// <summary>Rotate bone so its child direction points at target — no axis guesswork.</summary>
        static void Aim(Transform bone, Vector3 tip, Vector3 target, float w)
        {
            var q = Quaternion.FromToRotation(tip - bone.position, target - bone.position);
            bone.rotation = Quaternion.Slerp(Quaternion.identity, q, w) * bone.rotation;
        }

        void EnsureZipBones()
        {
            if (bonesSearched || anim == null) return;
            bonesSearched = true;
            foreach (var t in anim.GetComponentsInChildren<Transform>(true))
                switch (t.name)
                {
                    case "upperarm.l": uaL = t; break;
                    case "upperarm.r": uaR = t; break;
                    case "lowerarm.l": laL = t; break;
                    case "lowerarm.r": laR = t; break;
                    case "spine":      spineB = t; break;
                    case "head":       headB = t; break;
                    case "upperleg.l": legUL = t; break;
                    case "upperleg.r": legUR = t; break;
                }
        }

        void UpdateShowcase()
        {
            showcaseT -= Time.deltaTime;
            if (showcaseT > 0f || (target >= 0 && current != target)) return;
            showcaseIdx = (showcaseIdx + 1) % ShowcaseCycle.Length;
            int s = ShowcaseCycle[showcaseIdx];
            if (plays[s].IsValid())
            {
                plays[s].SetTime(0);
                plays[s].SetPlayState(PlayState.Playing);
            }
            FadeTo(s, 0.45f);
            // longer linger on idles, snappier on actions
            showcaseT = (s == S_Idle || s == S_IdleB) ? 3.2f : 2.4f;
        }

        void UpdateFeel()
        {
            var gm = GameManager.Instance;
            bool running = gm != null && gm.State == RunState.Running;
            bool grounded = cc == null || cc.isGrounded;
            bool sliding = pc != null && pc.IsSliding;
            float dt = Time.deltaTime;

            float leanTarget = running ? forwardLeanDeg : 0f;
            if (pc != null && pc.IsZipping) leanTarget = -15f; // reclined hang under the cable
            else if (sliding) leanTarget = -14f;
            else if (!grounded) leanTarget = -5f;
            leanX = Mathf.Lerp(leanX, leanTarget, 9f * dt);

            rollZ = Mathf.Lerp(rollZ, targetRoll, 11f * dt);
            targetRoll = Mathf.Lerp(targetRoll, 0f, 7f * dt);

            // corner body language — shoulders lead the exit while the hips
            // bank into the bend; both relax back to zero on the straight
            bool turningNow = pc != null && pc.IsTurning;
            int tdir = pc != null ? pc.TurnDir : 0;
            turnYaw = Mathf.Lerp(turnYaw, turningNow ? tdir * turnYawDeg : 0f, 7f * dt);
            turnRoll = Mathf.Lerp(turnRoll, turningNow ? -tdir * turnLeanDeg : 0f, 7f * dt);

            if (leanPivot != null)
                leanPivot.localEulerAngles = new Vector3(leanX, turnYaw, rollZ + turnRoll);

            squash = Mathf.Lerp(squash, 1f, 11f * dt);
            if (squashPivot != null)
            {
                float w = 1f + (1f - squash) * 0.45f;
                squashPivot.localScale = new Vector3(w, squash, w);

                // double-jump frontflip — squashPivot only carries scale, so the
                // tumble rides its rotation without fighting the landing dip
                if (flip > 0f)
                {
                    squashPivot.localRotation = Quaternion.Euler(360f - flip, 0f, 0f);
                    flip = Mathf.Max(0f, flip - flipSpeed * dt);
                    if (flip <= 0f) squashPivot.localRotation = Quaternion.identity;
                }
            }

            // run-clip speed follows game speed
            if (plays[S_Run].IsValid() && clipRun != null)
                plays[S_Run].SetSpeed(running ? Mathf.Clamp(gm.CurrentSpeed / 7f, 0.9f, 1.6f) : 1.0);
        }

        void SetDust(bool on)
        {
            if (dust == null) return;
            var em = dust.emission;
            bool emitting = em.rateOverTime.constant > 0f;
            if (on && !emitting) { em.rateOverTime = 14f; if (!dust.isPlaying) dust.Play(); }
            else if (!on && emitting) { em.rateOverTime = 0f; }
        }

        void Ensure(int s, float fade)
        {
            if (current == s && (target < 0 || target == s)) return;
            if (target == s) return;
            FadeTo(s, fade);
        }

        void FadeTo(int s, float fade)
        {
            if (!plays[s].IsValid()) return;
            if (plays[s].GetPlayState() == PlayState.Paused)
                plays[s].SetPlayState(PlayState.Playing);
            SnapshotWeights();
            target = s;
            fadeT = 0f;
            fadeDur = Mathf.Max(0.001f, fade);
        }

        void SnapshotWeights()
        {
            for (int i = 0; i < Count; i++)
                fadeFrom[i] = plays[i].IsValid() ? (float)mixer.GetInputWeight(i) : 0f;
        }

        void TickFade()
        {
            if (target < 0) return;
            fadeT += Time.deltaTime;
            float t = Mathf.Clamp01(fadeT / fadeDur);
            float e = t * t * (3f - 2f * t); // smoothstep
            for (int i = 0; i < Count; i++)
            {
                if (!plays[i].IsValid()) continue;
                float w = Mathf.Lerp(fadeFrom[i], i == target ? 1f : 0f, e);
                mixer.SetInputWeight(i, w);
            }
            if (t >= 1f) { current = target; target = -1; }
        }
    }
}
