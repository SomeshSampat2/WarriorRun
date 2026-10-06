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
        /// <summary>Direction of the active corner (+1 right / -1 left / 0 none).</summary>
        public int TurnDir => turning ? turnDir : 0;

        public Vector3 Forward => Quaternion.Euler(0f, HeadingYaw, 0f) * Vector3.forward;
        public Vector3 RightDir => Quaternion.Euler(0f, HeadingYaw, 0f) * Vector3.right;

        CharacterController cc;
        RunnerInput input;
        IRunnerAnim animator;
        RunnerCamera cam;
        TrackManager track;
        float verticalVelocity;
        float slideTimer;

        Vector3 laneAnchor;   // virtual point on the path spine we chase
        float lateral;        // current lateral offset from the spine
        bool turning;
        int turnDir;
        float turnYawTarget;
        Vector3 turnExit;
        float turnRadius = 4.6f;
        int queuedDir;

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            animator = GetComponentInChildren<IRunnerAnim>();
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
            if (input == null) return;
            input.SwipeLeft -= OnLeft;
            input.SwipeRight -= OnRight;
            input.SwipeUp -= OnJump;
            input.SwipeDown -= OnSlide;
            input.Tap -= OnTap;
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
            if (!CanSteer()) return;
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
            if (!CanJumpSlide()) return;
            EndSlide();
            if (cc.isGrounded)
            {
                verticalVelocity = jumpVelocity;
                animator?.OnJump();
                AudioManager.Instance?.Play(Sfx.Jump);
            }
        }

        void OnSlide()
        {
            if (!CanJumpSlide()) return;
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
            // side swipes do nothing there
            if (track != null && track.PlayerSingleLane) return;
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
            laneAnchor += Forward * speed * dt;

            if (!turning) UpdateApproach();

            // single-lane zones pin the runner to the spine's centre lane —
            // entering from a side lane glides them back to the middle
            if (track != null && track.PlayerSingleLane) CurrentLane = 1;
            float targetLat = (CurrentLane - 1) * laneWidth;
            lateral = Mathf.MoveTowards(lateral, targetLat, laneChangeSpeed * dt);

            float yDelta;
            if (gm.PlaneActive)
            {
                // climb to cruise height and hold it — gravity suspended while flying
                verticalVelocity = 0f;
                float ny = Mathf.MoveTowards(transform.position.y, flyHeight, flyClimbSpeed * dt);
                yDelta = ny - transform.position.y;
            }
            else
            {
                verticalVelocity += gravity * dt;
                if (cc.isGrounded && verticalVelocity < -2f) verticalVelocity = -2f;
                yDelta = verticalVelocity * dt;
            }

            Vector3 desired = laneAnchor + RightDir * lateral;
            var move = new Vector3(desired.x - transform.position.x, yDelta, desired.z - transform.position.z);
            cc.Move(move);
            transform.rotation = Quaternion.Euler(0f, HeadingYaw, 0f);
            IsGrounded = cc.isGrounded;

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

        void ApplyGravityOnly()
        {
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
                gm.AddCoin();
            }
            else if (other.TryGetComponent(out PowerUp pu))
            {
                // vehicle pickups carry their own paint palette — the mounted
                // vehicle gets the exact colors you saw floating on the track
                var tint = pu.GetComponent<VehicleTint>();
                gm.GrantPowerUp(pu.kind, tint?.colorA, tint?.colorB);
                pu.Collect();
            }
            else if (other.TryGetComponent(out Obstacle ob))
            {
                if (gm.TryCrash())
                {
                    animator?.OnDeath();
                    enabled = false; // stop steering; GameManager drives the death flow
                }
                else
                {
                    ob.gameObject.SetActive(false); // shield/boost/vehicles smash straight through
                    if (gm.VehicleActive)
                    {
                        if (cam == null) cam = FindFirstObjectByType<RunnerCamera>();
                        cam?.Shake(0.45f);
                    }
                }
            }
        }
    }
}
