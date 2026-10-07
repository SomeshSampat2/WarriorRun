using UnityEngine;
using WarriorRun.Core;
using WarriorRun.Input;
using WarriorRun.World;

namespace WarriorRun.Player
{
    /// <summary>
    /// 3-lane runner on a wandering path: a virtual anchor rides the track
    /// spine (position + heading) while the capsule follows at a lateral lane
    /// offset. At a corner junction a swipe in the turn direction banks the
    /// heading through the arc; missing it means the dead-end wall — or the
    /// void — decides. Obstacle/coin pickups come through triggers.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Lanes")]
        [SerializeField] float laneWidth = 2.2f;
        [SerializeField] float laneChangeSpeed = 14f;

        [Header("Jump/Slide")]
        [SerializeField] float jumpVelocity = 8.5f;
        [SerializeField] float airJumpMult = 1.12f; // second hop kicks a bit higher
        [SerializeField] int maxAirJumps = 1;
        [SerializeField] float gravity = -26f;
        [SerializeField] float slideDuration = 0.8f;
        [SerializeField] float standingHeight = 1.8f;
        [SerializeField] float slidingHeight = 0.9f;

        [Header("Plane flight")]
        [SerializeField] float flyHeight = 5.2f;
        [SerializeField] float flyClimbSpeed = 6.5f;

        [Header("Turns")]
        [SerializeField] float armDistance = 30f; // swipe window + HUD warning opens this far before the bend
        [SerializeField] float missSlack = 4.5f;  // a fraction-late swipe still claws around the corner
        [SerializeField] float fallKillY = -9f;   // ran off the world — below this it's a fall death

        [Header("Zipline")]
        [SerializeField] float zipSpeedMult = 1.12f; // the ride runs a touch hotter than the sprint
        [SerializeField] float zipCatchTime = 0.28f; // blend onto the line after the grab

        public int CurrentLane { get; private set; } = 1;
        public bool IsGrounded { get; private set; }
        public bool IsSliding { get; private set; }

        /// <summary>Current travel heading in degrees (rotates through a turn).</summary>
        public float HeadingYaw { get; private set; }
        /// <summary>Lateral offset from the spine, in meters.</summary>
        public float Lateral => lateral;
        /// <summary>+1/-1 while a corner is in swipe range, else 0 — drives the HUD arrow.</summary>
        public int ArmedTurnDir { get; private set; }
        /// <summary>A turn swipe is queued and waiting for the bend.</summary>
        public bool TurnQueued { get; private set; }
        /// <summary>Currently riding the corner arc — the animator leans into it.</summary>
        public bool IsTurning => turning;
        /// <summary>Hanging from a zipline cable through the gorge.</summary>
        public bool IsZipping => zipping;
        /// <summary>Direction of the active corner (+1 right / -1 left / 0 none).</summary>
        public int TurnDir => turning ? turnDir : 0;

        public Vector3 Forward => Quaternion.Euler(0f, HeadingYaw, 0f) * Vector3.forward;
        public Vector3 RightDir => Quaternion.Euler(0f, HeadingYaw, 0f) * Vector3.right;

        CharacterController cc;
        RunnerInput input;
        IRunnerAnim animator;
        RunnerFx fx;
        RunnerCamera cam;
        TrackManager track;
        float verticalVelocity;
        float slideTimer;

        Vector3 laneAnchor;   // virtual point on the path spine we chase
        float lateral;        // current lateral offset from the spine
        int airJumps;         // consumed while airborne; refunded on touchdown
        int edgeFall;         // ±1 once a side swipe in a one-lane zone commits to the lava
        bool recentering;     // hovering back to centre after entering a one-lane zone on a side lane
        bool inSingleLane;    // last frame's zone flag — detects the biome seam
        bool turning;
        int turnDir;
        float turnYawTarget;
        Vector3 turnExit;
        float turnRadius = 4.6f;
        int queuedDir;
        bool zipping;
        ZipLine zip;
        float zipDist;        // meters along the current rope leg
        float zipSpeed;       // snapshotted at the grab — ride pace is smooth
        float zipT;           // catch-blend 0→1
        Vector3 zipFrom;      // where the runner left the platform
        Transform zipTrolley; // the wheel+handle prop riding the cable overhead
        bool zipLanding;      // stunt-flip dismount arc — rope ran out
        Vector3 zipLandFrom;
        Vector3 zipLandTo;
        float zipLandT;
        const float zipLandDur = 0.58f;   // matches the 620°/s tumble → lands as the flip completes
        const float zipLandPeak = 1.0f;   // arc height over the lip

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            animator = GetComponentInChildren<IRunnerAnim>();
            fx = GetComponentInChildren<RunnerFx>();
            input = FindFirstObjectByType<RunnerInput>();
            track = FindFirstObjectByType<TrackManager>();
            laneAnchor = transform.position;
        }

        void OnEnable()
        {
            if (input == null) return;
            input.SwipeLeft += OnLeft;
            input.SwipeRight += OnRight;
            input.SwipeUp += OnJump;
            input.SwipeDown += OnSlide;
            input.Tap += OnTap;
        }

        void OnDisable()
        {
            if (input != null)
            {
                input.SwipeLeft -= OnLeft;
                input.SwipeRight -= OnRight;
                input.SwipeUp -= OnJump;
                input.SwipeDown -= OnSlide;
                input.Tap -= OnTap;
            }
            // never leave the trolley hanging on a dead/disabled runner
            zipping = false;
            zipLanding = false;
            zip = null;
            if (zipTrolley != null) zipTrolley.gameObject.SetActive(false);
        }

        void OnTap() { if (GameManager.Instance.State == RunState.Ready) GameManager.Instance.BeginRun(); }
        void OnLeft() => Steer(-1);
        void OnRight() => Steer(+1);

        /// <summary>
        /// A sideways swipe means "turn" while a matching corner is in the
        /// window — otherwise it's a normal lane change.
        /// </summary>
        void Steer(int dir)
        {
            if (!CanSteer() || zipping || zipLanding) return;
            if (ArmedTurnDir == dir)
            {
                queuedDir = dir;
                TurnQueued = true;
                animator?.OnLaneSwitch(dir); // lean into the coming corner
                AudioManager.Instance?.Play(Sfx.Swipe);
            }
            else ChangeLane(dir);
        }

        void OnJump()
        {
            if (!CanJumpSlide() || zipping || zipLanding) return;
            var gmj = GameManager.Instance;
            if (cc.isGrounded)
            {
                EndSlide();
                verticalVelocity = jumpVelocity * (gmj != null ? gmj.JumpMult : 1f);
                animator?.OnJump();
                AudioManager.Instance?.Play(gmj != null && gmj.SpringActive ? Sfx.Spring : Sfx.Jump);
            }
            else if (airJumps < maxAirJumps)
            {
                // stunt hop — one per flight, frontflips the rig
                airJumps++;
                EndSlide();
                verticalVelocity = jumpVelocity * airJumpMult * (gmj != null ? gmj.JumpMult : 1f);
                animator?.OnDoubleJump();
                AudioManager.Instance?.Play(Sfx.Jump);
            }
        }

        void OnSlide()
        {
            if (!CanJumpSlide() || zipping || zipLanding) return;
            if (IsSliding) { slideTimer = slideDuration; return; }

            IsSliding = true;
            slideTimer = slideDuration;
            cc.height = slidingHeight;
            cc.center = new Vector3(0f, slidingHeight * 0.5f, 0f);
            animator?.OnSlide(true);
            AudioManager.Instance?.Play(Sfx.Slide);

            // slam down if airborne so slide can be used to drop fast
            if (!cc.isGrounded) verticalVelocity = Mathf.Min(verticalVelocity, -12f);
        }

        static bool CanSteer() =>
            GameManager.Instance != null && GameManager.Instance.State == RunState.Running;

        // cars don't jump or slide, and the plane steers only between lanes
        static bool CanJumpSlide() =>
            CanSteer() && !GameManager.Instance.VehicleActive;

        void ChangeLane(int dir)
        {
            // one-lane zones (the lava chasm) have no lane to change into —
            // a side swipe commits the runner off the ledge into the melt
            if (track != null && track.PlayerSingleLane)
            {
                if (recentering || edgeFall != 0) return;
                edgeFall = dir;
                animator?.OnLaneSwitch(dir);
                AudioManager.Instance?.Play(Sfx.Swipe);
                return;
            }
            int next = Mathf.Clamp(CurrentLane + dir, 0, 2);
            if (next == CurrentLane) return;
            CurrentLane = next;
            animator?.OnLaneSwitch(dir);
            AudioManager.Instance?.Play(Sfx.Swipe);
        }

        void EndSlide()
        {
            if (!IsSliding) return;
            IsSliding = false;
            cc.height = standingHeight;
            cc.center = new Vector3(0f, standingHeight * 0.5f, 0f);
            animator?.OnSlide(false);
        }

        void Update()
        {
            var gm = GameManager.Instance;
            if (gm == null || gm.State != RunState.Running) { ApplyGravityOnly(); return; }

            if (gm.VehicleActive && IsSliding) EndSlide();
            if (IsSliding && (slideTimer -= Time.deltaTime) <= 0f) EndSlide();

            float dt = Time.deltaTime;
            float speed = gm.CurrentSpeed;

            // mid-corner the heading swings through the arc at ω = v / r
            if (turning)
            {
                float step = speed / turnRadius * Mathf.Rad2Deg * dt;
                HeadingYaw = Mathf.MoveTowards(HeadingYaw, turnYawTarget, step);
                if (Mathf.Approximately(HeadingYaw, turnYawTarget))
                {
                    HeadingYaw = turnYawTarget;
                    FinishTurn();
                }
            }
            if (zipping || zipLanding)
            {
                // the dismount: rope ran out — a front-flip arc that carries the
                // runner past the deck lip and plants them mid-landing-pad
                if (zipLanding)
                {
                    zipLandT += dt / zipLandDur;
                    float lt = Mathf.Min(1f, zipLandT);
                    // LINEAR forward motion — the arc covers exactly what the
                    // ride speed carries in this time, so momentum flows
                    // straight through the flip with no stop-restart hitch
                    var lp = Vector3.Lerp(zipLandFrom, zipLandTo, lt);
                    lp.y += 4f * zipLandPeak * lt * (1f - lt);
                    lateral = Mathf.MoveTowards(lateral, 0f, laneChangeSpeed * dt);
                    cc.Move(lp - transform.position);
                    transform.rotation = Quaternion.Euler(0f, HeadingYaw, 0f);
                    IsGrounded = false;
                    if (zipLandT >= 1f)
                    {
                        zipLanding = false;
                        // re-seat the path spine exactly under the touchdown —
                        // the arc overshoots the anchor, so hand it the landing
                        // spot or the first normal frame snaps the runner ahead
                        laneAnchor = new Vector3(zipLandTo.x, laneAnchor.y, zipLandTo.z);
                        lateral = 0f;
                        verticalVelocity = -3f; // settle the last centimeters
                    }
                    return;
                }

                // hang off the cable and ride it down the gorge — the spine
                // anchor keeps pace underneath so the landing hands back to
                // normal movement without a snap
                zipDist += zipSpeed * dt;
                laneAnchor += Forward * zipSpeed * dt;
                lateral = Mathf.MoveTowards(lateral, 0f, laneChangeSpeed * dt);

                Vector3 ropePt = zip.PointAt(zipDist) - Vector3.up * zip.hangDepth;
                if (zipT < 1f)
                {
                    zipT = Mathf.Min(1f, zipT + dt / zipCatchTime);
                    ropePt = Vector3.Lerp(zipFrom, ropePt, zipT * zipT * (3f - 2f * zipT));
                }
                cc.Move(ropePt - transform.position);
                transform.rotation = Quaternion.Euler(0f, HeadingYaw, 0f);
                IsGrounded = false;

                if (zipDist >= zip.Length)
                {
                    // chain into the next rope segment — or flip off on the last
                    var nxt = zip.isExit ? null
                        : (track != null ? track.ZipLineContinuing(zip) : null);
                    if (nxt != null) { zip = nxt; zipDist = 0f; }
                    else BeginZipLand();
                }
                return;
            }

            laneAnchor += Forward * speed * dt;

            if (!turning) UpdateApproach();

            // biome seam check — the lava ledge is one lane wide: crossing
            // in on a side lane hovers the runner back to centre instead of
            // dropping them straight into the melt
            bool single = track != null && track.PlayerSingleLane;
            if (single != inSingleLane)
            {
                inSingleLane = single;
                edgeFall = 0;
                recentering = single && CurrentLane != 1;
            }
            if (recentering)
            {
                CurrentLane = 1;
                if (Mathf.Abs(lateral) < 0.2f) recentering = false;
            }
            float targetLat = edgeFall != 0 ? edgeFall * laneWidth * 2f : (CurrentLane - 1) * laneWidth;
            lateral = Mathf.MoveTowards(lateral, targetLat, laneChangeSpeed * dt);

            float yDelta;
            if (gm.PlaneActive)
            {
                // climb to cruise height and hold it — gravity suspended while flying
                verticalVelocity = 0f;
                float ny = Mathf.MoveTowards(transform.position.y, flyHeight, flyClimbSpeed * dt);
                yDelta = ny - transform.position.y;
            }
            else if (recentering)
            {
                // mid-glide over the lava gap — hold height until the ledge is underfoot
                verticalVelocity = 0f;
                yDelta = 0f;
            }
            else
            {
                verticalVelocity += gravity * gm.GravityMult * dt;
                if (cc.isGrounded && verticalVelocity < -2f) verticalVelocity = -2f;
                yDelta = verticalVelocity * dt;
            }

            Vector3 desired = laneAnchor + RightDir * lateral;
            var move = new Vector3(desired.x - transform.position.x, yDelta, desired.z - transform.position.z);
            cc.Move(move);
            transform.rotation = Quaternion.Euler(0f, HeadingYaw, 0f);
            IsGrounded = cc.isGrounded;
            if (IsGrounded) airJumps = 0;

            // ran past a corner into the void
            if (transform.position.y < fallKillY)
            {
                animator?.OnDeath();
                gm.Die();
                enabled = false;
            }
        }

        /// <summary>
        /// Watches the next corner on the path: opens the swipe window, fires
        /// a queued turn at the bend, and releases stale plans the runner
        /// has already blown past.
        /// </summary>
        void UpdateApproach()
        {
            ArmedTurnDir = 0;
            if (track == null || !track.PeekTurn(out var tp))
            {
                TurnQueued = false;
                return;
            }

            float dist = Vector3.Dot(tp.arcStart - laneAnchor, Forward);
            if (dist <= armDistance && dist >= -missSlack)
            {
                ArmedTurnDir = tp.dir;
                // vehicles steer themselves — a car/plane that blows past a
                // junction just sails into the void, so they take every corner
                if (GameManager.Instance.VehicleActive) { queuedDir = tp.dir; TurnQueued = true; }
                if (TurnQueued && queuedDir == tp.dir && dist <= 0f)
                    BeginTurn(tp);
            }
            else if (dist < -missSlack)
            {
                track.SkipTurn(); // sailed past — the dead-end wall settles it
                TurnQueued = false;
            }
        }

        void BeginTurn(TrackManager.TurnPlan tp)
        {
            turning = true;
            turnDir = tp.dir;
            turnRadius = tp.radius;
            turnYawTarget = tp.yawOut;
            turnExit = tp.exitPoint;
            ArmedTurnDir = 0;
            TurnQueued = false;
            track.ConsumeTurn();
            animator?.OnLaneSwitch(tp.dir);
            AudioManager.Instance?.Play(Sfx.Swipe);
        }

        /// <summary>
        /// Re-seat the anchor on the outgoing spine and absorb any bend
        /// overshoot into the lateral offset so the capsule never teleports.
        /// </summary>
        void FinishTurn()
        {
            Vector3 fwd = Forward;
            Vector3 right = RightDir;
            Vector3 d = laneAnchor - turnExit;
            laneAnchor = turnExit + fwd * Vector3.Dot(d, fwd);
            lateral = Mathf.Clamp(lateral + Vector3.Dot(d, right),
                -laneWidth * 1.5f, laneWidth * 1.5f);
            turning = false;
        }

        /// <summary>
        /// The launch-platform grab fired — sling the runner onto the cable.
        /// Movement switches to rope-following; lane/jump/slide inputs are
        /// ignored for the ride (committed, like Temple Run's ropeway).
        /// </summary>
        void BeginZip(ZipLine z)
        {
            zipping = true;
            zip = z;
            zipDist = 0f;
            zipT = 0f;
            zipFrom = transform.position;
            var gm = GameManager.Instance;
            zipSpeed = Mathf.Max(8f, gm != null ? gm.CurrentSpeed : 8f) * zipSpeedMult;
            // landing clean-up — whatever motion the grab interrupted is done
            edgeFall = 0;
            recentering = false;
            turning = false;
            TurnQueued = false;
            ArmedTurnDir = 0;
            CurrentLane = 1;   // the cable is the centre lane — land centred
            EndSlide();
            gm?.ClearMountables(); // car/plane/giant can't hang from a rope
            // face down the cable — the grab can fire while a corner is armed
            var dir = z.PointAt(2f) - z.PointAt(0f);
            if (dir.sqrMagnitude > 0.01f)
                HeadingYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            if (zipTrolley == null) zipTrolley = transform.Find("ZipTrolley");
            if (zipTrolley != null) zipTrolley.gameObject.SetActive(true);
            animator?.OnZip(true);
            AudioManager.Instance?.Play(Sfx.Swipe);
            if (cam == null) cam = FindFirstObjectByType<RunnerCamera>();
            cam?.Shake(0.14f);
        }

        /// <summary>
        /// Rope ran out — stunt dismount: let go of the cable and arc forward
        /// in a front-flip that plants the runner mid-deck. Scripted rather
        /// than ballistic so the landing spot is guaranteed past the lip.
        /// </summary>
        void BeginZipLand()
        {
            zipping = false;
            zipLanding = true;
            zipLandT = 0f;
            zipLandFrom = transform.position;
            // momentum-continuous: the arc travels zipSpeed × duration, so the
            // flip is pure ballistic — no slow-down at release, no snap on land
            Vector3 pad = laneAnchor + Forward * (zipSpeed * zipLandDur);
            zipLandTo = new Vector3(pad.x, 0.03f, pad.z);
            zip = null;
            if (zipTrolley != null) zipTrolley.gameObject.SetActive(false);
            animator?.OnZip(false);      // release the hang pose
            animator?.OnDoubleJump();    // reuse the tumble — reads as the flip
            AudioManager.Instance?.Play(Sfx.Jump);
        }

        void ApplyGravityOnly()
        {
            // died or got reset mid-ride — the rope is gone
            if ((zipping || zipLanding) && (GameManager.Instance == null
                || GameManager.Instance.State != RunState.Paused))
            {
                zipping = false;
                zipLanding = false;
                zip = null;
                if (zipTrolley != null) zipTrolley.gameObject.SetActive(false);
            }
            if (cc == null || !cc.enabled) return;
            if (!cc.isGrounded)
            {
                verticalVelocity += gravity * Time.deltaTime;
                cc.Move(new Vector3(0f, verticalVelocity * Time.deltaTime, 0f));
            }
            IsGrounded = cc.isGrounded;
        }

        void OnTriggerEnter(Collider other)
        {
            var gm = GameManager.Instance;
            if (gm == null || gm.State != RunState.Running) return;

            if (other.TryGetComponent(out Coin coin))
            {
                coin.Collect();
                gm.AddCoin(coin.value);
            }
            else if (other.TryGetComponent(out PowerUp pu))
            {
                // vehicle pickups carry their own paint palette — the mounted
                // vehicle gets the exact colors you saw floating on the track
                var tint = pu.GetComponent<VehicleTint>();
                gm.GrantPowerUp(pu.kind, tint?.colorA, tint?.colorB);
                pu.Collect();
            }
            else if (!zipping && !zipLanding && other.TryGetComponent(out ZipGrab grab) && grab.line != null)
            {
                BeginZip(grab.line);
            }
            else if (other.TryGetComponent(out Obstacle ob))
            {
                if (ob.kind == ObstacleKind.Lava)
                {
                    // molten hazard, not a blocker — no shield, boost or
                    // vehicle saves a lava bath. The controller stays live so
                    // gravity keeps sinking the runner into the melt.
                    animator?.OnDeath();
                    if (cam == null) cam = FindFirstObjectByType<RunnerCamera>();
                    cam?.Shake(0.4f);
                    gm.Die();
                }
                else if (gm.GhostActive)
                {
                    // ghost phase — drift through untouched, the blocker survives
                    fx?.OnPhaseThrough();
                    AudioManager.Instance?.Play(Sfx.Phase);
                }
                else if (gm.TryCrash())
                {
                    animator?.OnDeath();
                    enabled = false; // stop steering; GameManager drives the death flow
                }
                else
                {
                    ob.gameObject.SetActive(false); // shield/boost/vehicles/giant smash straight through
                    if (gm.VehicleActive)
                    {
                        if (cam == null) cam = FindFirstObjectByType<RunnerCamera>();
                        cam?.Shake(0.45f);
                    }
                    else if (gm.GiantActive)
                    {
                        if (cam == null) cam = FindFirstObjectByType<RunnerCamera>();
                        cam?.Shake(0.55f);
                        AudioManager.Instance?.Play(Sfx.Stomp);
                    }
                }
            }
        }
    }
}
