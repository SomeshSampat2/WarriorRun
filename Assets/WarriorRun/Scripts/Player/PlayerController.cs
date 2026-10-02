using UnityEngine;
using WarriorRun.Core;
using WarriorRun.Input;
using WarriorRun.World;

namespace WarriorRun.Player
{
    /// <summary>
    /// 3-lane runner: forward speed from GameManager, lane changes, jump & slide
    /// via a CharacterController. Obstacle/coin pickups come through triggers.
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

        public int CurrentLane { get; private set; } = 1;
        public bool IsGrounded { get; private set; }
        public bool IsSliding { get; private set; }

        CharacterController cc;
        RunnerInput input;
        IRunnerAnim animator;
        RunnerCamera cam;
        float verticalVelocity;
        float slideTimer;

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            animator = GetComponentInChildren<IRunnerAnim>();
            input = FindFirstObjectByType<RunnerInput>();
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
        void OnLeft() { if (CanSteer()) ChangeLane(-1); }
        void OnRight() { if (CanSteer()) ChangeLane(+1); }

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

            float speed = gm.CurrentSpeed;
            float targetX = (CurrentLane - 1) * laneWidth;
            float x = Mathf.MoveTowards(transform.position.x, targetX, laneChangeSpeed * Time.deltaTime);

            float yDelta;
            if (gm.PlaneActive)
            {
                // climb to cruise height and hold it — gravity suspended while flying
                verticalVelocity = 0f;
                float ny = Mathf.MoveTowards(transform.position.y, flyHeight, flyClimbSpeed * Time.deltaTime);
                yDelta = ny - transform.position.y;
            }
            else
            {
                verticalVelocity += gravity * Time.deltaTime;
                if (cc.isGrounded && verticalVelocity < -2f) verticalVelocity = -2f;
                yDelta = verticalVelocity * Time.deltaTime;
            }

            var move = new Vector3(x - transform.position.x, yDelta, speed * Time.deltaTime);
            cc.Move(move);
            IsGrounded = cc.isGrounded;
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
