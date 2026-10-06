using UnityEngine;
using UnityEngine.SceneManagement;

namespace WarriorRun.Core
{
    public enum RunState { Ready, Running, Paused, Dead }

    /// <summary>
    /// Owns the run loop: state machine, score/distance, speed ramp and persistence.
    /// Lives in the Game scene only.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        static GameManager inst;
        public static GameManager Instance =>
            inst != null ? inst : (inst = FindAnyObjectByType<GameManager>(FindObjectsInactive.Include));

        [Header("Speed ramp")]
        [SerializeField] float startSpeed = 13f;
        [SerializeField] float maxSpeed = 28f;
        [SerializeField] float acceleration = 0.32f;

        [Header("Scoring")]
        [SerializeField] int coinScoreValue = 5;

        [Header("Powerups")]
        [SerializeField] float magnetDuration = 16f;
        [SerializeField] float boostDuration = 12f;
        [SerializeField] float boostSpeedAdd = 7f;
        [SerializeField] float ghostDuration = 7f;
        [SerializeField] float springDuration = 11f;
        [SerializeField] float giantDuration = 9f;
        [SerializeField] float springJumpMult = 1.55f;
        [SerializeField] float springGravityMult = 0.72f;
        [SerializeField] float warnTime = 2.2f; // HUD/aura flicker window before expiry

        [Header("Vehicles")]
        [SerializeField] float carDuration = 10f;
        [SerializeField] float carSpeedAdd = 20f;
        [SerializeField] float planeDuration = 12f;
        [SerializeField] float planeSpeedAdd = 6f;
        [SerializeField] float planeLandGrace = 1.6f;

        public RunState State { get; private set; } = RunState.Ready;
        public float MaxSpeed => maxSpeed;
        public float CurrentSpeed { get; private set; }
        public float Distance { get; private set; }
        public int Score { get; private set; }
        public int Coins { get; private set; }
        public int BestScore { get; private set; }
        public float ElapsedTime { get; private set; }
        public Transform PlayerTf => player;

        public float MagnetUntil { get; private set; }
        public float BoostUntil { get; private set; }
        public bool ShieldActive { get; private set; }
        public bool MagnetActive => Time.time < MagnetUntil;
        public bool BoostActive => Time.time < BoostUntil;

        public float GhostUntil { get; private set; }
        public float SpringUntil { get; private set; }
        public float GiantUntil { get; private set; }
        public bool GhostActive => Time.time < GhostUntil;
        public bool SpringActive => Time.time < SpringUntil;
        public bool GiantActive => Time.time < GiantUntil;
        public float JumpMult => SpringActive ? springJumpMult : 1f;
        public float GravityMult => SpringActive ? springGravityMult : 1f;

        public float CarUntil { get; private set; }
        public float PlaneUntil { get; private set; }
        public bool CarActive => Time.time < CarUntil;
        public bool PlaneActive => Time.time < PlaneUntil;
        public bool VehicleActive => CarActive || PlaneActive;

        // paint palette carried from the pickup to the mounted vehicle
        public Color VehicleA { get; private set; } = new Color(1f, 0.35f, 0.2f);
        public Color VehicleB { get; private set; } = new Color(1f, 0.75f, 0.15f);

        public event System.Action<WarriorRun.World.PowerUpKind> PowerUpChanged;

        public event System.Action<RunState> StateChanged;
        public event System.Action ScoreChanged;

        Transform player;
        float baseSpeed;
        float airGraceUntil;
        bool planeWasActive;

        void Awake()
        {
            inst = this;
            Time.timeScale = 1f;
            BestScore = SaveSystem.BestScore;
            Application.targetFrameRate = 60;
        }

        void Start()
        {
            player = FindFirstObjectByType<Player.PlayerController>()?.transform;
            SetState(RunState.Ready);
        }

        void Update()
        {
            if (State != RunState.Running) return;

            ElapsedTime += Time.deltaTime;
            baseSpeed = Mathf.Min(maxSpeed, baseSpeed + acceleration * Time.deltaTime);
            CurrentSpeed = baseSpeed
                + (BoostActive ? boostSpeedAdd : 0f)
                + (CarActive ? carSpeedAdd : 0f)
                + (PlaneActive ? planeSpeedAdd : 0f);

            // brief invincibility while the plane drops back to the track
            if (planeWasActive && !PlaneActive) airGraceUntil = Time.time + planeLandGrace;
            planeWasActive = PlaneActive;

            // path distance — the runner always advances exactly speed*dt along
            // the spine (straights and corner arcs alike), so accumulate it
            Distance += CurrentSpeed * Time.deltaTime;
            {
                int newScore = Mathf.FloorToInt(Distance) + Coins * coinScoreValue;
                if (newScore != Score)
                {
                    Score = newScore;
                    ScoreChanged?.Invoke();
                }
            }
        }

        public void BeginRun()
        {
            if (State != RunState.Ready) return;
            baseSpeed = startSpeed;
            CurrentSpeed = startSpeed;
            Distance = 0f;
            SetState(RunState.Running);
            if (player != null)
                player.GetComponentInChildren<WarriorRun.Player.IRunnerAnim>()?.OnRunStart();
            AudioManager.Instance?.Play(Sfx.Start);
            AudioManager.Instance?.PlayRandomMusic(); // new run, new beat
        }

        public void Pause()
        {
            if (State != RunState.Running) return;
            Time.timeScale = 0f;
            SetState(RunState.Paused);
        }

        public void Resume()
        {
            if (State != RunState.Paused) return;
            Time.timeScale = 1f;
            SetState(RunState.Running);
        }

        public void GrantPowerUp(WarriorRun.World.PowerUpKind kind, Color? tintA = null, Color? tintB = null)
        {
            switch (kind)
            {
                case WarriorRun.World.PowerUpKind.Magnet: MagnetUntil = Time.time + magnetDuration; break;
                case WarriorRun.World.PowerUpKind.Shield: ShieldActive = true; break;
                case WarriorRun.World.PowerUpKind.Boost: BoostUntil = Time.time + boostDuration; break;
                // vehicles are exclusive — grabbing one drops the other
                case WarriorRun.World.PowerUpKind.Car:   CarUntil = Time.time + carDuration;     PlaneUntil = 0f; SetVehicleTint(tintA, tintB); break;
                case WarriorRun.World.PowerUpKind.Plane: PlaneUntil = Time.time + planeDuration; CarUntil = 0f;   SetVehicleTint(tintA, tintB); break;
                case WarriorRun.World.PowerUpKind.Ghost:  GhostUntil = Time.time + ghostDuration;   break;
                case WarriorRun.World.PowerUpKind.Spring: SpringUntil = Time.time + springDuration; break;
                case WarriorRun.World.PowerUpKind.Giant:  GiantUntil = Time.time + giantDuration;   break;
            }
            PowerUpChanged?.Invoke(kind);
            // vehicles play their own engine stinger; the newer pickups each
            // have a voice of their own — everything else gets the chime
            switch (kind)
            {
                case WarriorRun.World.PowerUpKind.Car:
                case WarriorRun.World.PowerUpKind.Plane: break;
                case WarriorRun.World.PowerUpKind.Ghost:  AudioManager.Instance?.Play(Sfx.Ghost);  break;
                case WarriorRun.World.PowerUpKind.Spring: AudioManager.Instance?.Play(Sfx.Spring); break;
                case WarriorRun.World.PowerUpKind.Giant:  AudioManager.Instance?.Play(Sfx.Stomp);  break;
                default: AudioManager.Instance?.Play(Sfx.PowerUp); break;
            }
        }

        void SetVehicleTint(Color? a, Color? b)
        {
            var p = a.HasValue ? (a.Value, b ?? a.Value) : WarriorRun.World.VehicleTint.RandomPalette();
            VehicleA = p.Item1;
            VehicleB = p.Item2;
        }

        /// <summary>Obstacle hit — returns true if the run actually ends.</summary>
        public bool TryCrash()
        {
            if (State != RunState.Running) return false;
            if (BoostActive || VehicleActive || GiantActive) return false; // boost/giant/vehicles plow through
            if (Time.time < airGraceUntil) return false;    // still settling after a flight
            if (ShieldActive)
            {
                ShieldActive = false;
                PowerUpChanged?.Invoke(WarriorRun.World.PowerUpKind.Shield);
                AudioManager.Instance?.Play(Sfx.ShieldBreak);
                return false;
            }
            Die();
            return true;
        }

        public void AddCoin(int count = 1)
        {
            if (State != RunState.Running) return;
            Coins += count;
            ScoreChanged?.Invoke();
            AudioManager.Instance?.Play(count > 1 ? Sfx.Gem : Sfx.Coin);
        }

        // ---- powerup HUD/FX queries ----

        /// <summary>Seconds left on a timed powerup; Shield reports 1 while held.</summary>
        public float PowerUpRemaining(World.PowerUpKind k)
        {
            switch (k)
            {
                case World.PowerUpKind.Magnet: return Mathf.Max(0f, MagnetUntil - Time.time);
                case World.PowerUpKind.Boost:  return Mathf.Max(0f, BoostUntil - Time.time);
                case World.PowerUpKind.Ghost:  return Mathf.Max(0f, GhostUntil - Time.time);
                case World.PowerUpKind.Spring: return Mathf.Max(0f, SpringUntil - Time.time);
                case World.PowerUpKind.Giant:  return Mathf.Max(0f, GiantUntil - Time.time);
                case World.PowerUpKind.Car:    return Mathf.Max(0f, CarUntil - Time.time);
                case World.PowerUpKind.Plane:  return Mathf.Max(0f, PlaneUntil - Time.time);
                case World.PowerUpKind.Shield: return ShieldActive ? 1f : 0f;
                default: return 0f;
            }
        }

        /// <summary>Remaining time as a 0..1 slice of the original duration.</summary>
        public float PowerUpFraction(World.PowerUpKind k)
        {
            float dur;
            switch (k)
            {
                case World.PowerUpKind.Magnet: dur = magnetDuration; break;
                case World.PowerUpKind.Boost:  dur = boostDuration; break;
                case World.PowerUpKind.Ghost:  dur = ghostDuration; break;
                case World.PowerUpKind.Spring: dur = springDuration; break;
                case World.PowerUpKind.Giant:  dur = giantDuration; break;
                case World.PowerUpKind.Car:    dur = carDuration; break;
                case World.PowerUpKind.Plane:  dur = planeDuration; break;
                case World.PowerUpKind.Shield: return ShieldActive ? 1f : 0f;
                default: return 0f;
            }
            return Mathf.Clamp01(PowerUpRemaining(k) / dur);
        }

        /// <summary>Inside the flicker window — the powerup is about to lapse.</summary>
        public bool PowerUpWarning(World.PowerUpKind k)
            => k != World.PowerUpKind.Shield && IsPowerUpActive(k) && PowerUpRemaining(k) <= warnTime;

        public bool IsPowerUpActive(World.PowerUpKind k)
        {
            switch (k)
            {
                case World.PowerUpKind.Magnet: return MagnetActive;
                case World.PowerUpKind.Boost:  return BoostActive;
                case World.PowerUpKind.Ghost:  return GhostActive;
                case World.PowerUpKind.Spring: return SpringActive;
                case World.PowerUpKind.Giant:  return GiantActive;
                case World.PowerUpKind.Car:    return CarActive;
                case World.PowerUpKind.Plane:  return PlaneActive;
                case World.PowerUpKind.Shield: return ShieldActive;
                default: return false;
            }
        }

        public void Die()
        {
            if (State == RunState.Dead) return;
            SetState(RunState.Dead);
            AudioManager.Instance?.Play(Sfx.Crash);

            if (Score > BestScore)
            {
                BestScore = Score;
                SaveSystem.BestScore = BestScore;
            }
            SaveSystem.TotalCoins += Coins;
            ScoreChanged?.Invoke();
        }

        public void Restart() => LoadScene(SceneManager.GetActiveScene().buildIndex);
        public void GoToMenu() => LoadScene(0);

        void LoadScene(int index)
        {
            Time.timeScale = 1f;
            WarriorRun.UI.LoadingOverlay.RunToScene(index,
                index == 0 ? "RETURNING TO CAMP" : "RE-ENTERING THE ARENA");
        }

        void SetState(RunState next)
        {
            State = next;
            StateChanged?.Invoke(next);
        }
    }

    /// <summary>Thin PlayerPrefs wrapper.</summary>
    public static class SaveSystem
    {
        const string BestKey = "pd_best_score";
        const string CoinsKey = "pd_total_coins";
        const string SoundKey = "pd_sound";

        public static int BestScore
        {
            get => PlayerPrefs.GetInt(BestKey, 0);
            set { PlayerPrefs.SetInt(BestKey, value); PlayerPrefs.Save(); }
        }

        public static int TotalCoins
        {
            get => PlayerPrefs.GetInt(CoinsKey, 0);
            set { PlayerPrefs.SetInt(CoinsKey, value); PlayerPrefs.Save(); }
        }

        public static bool SoundOn
        {
            get => PlayerPrefs.GetInt(SoundKey, 1) == 1;
            set { PlayerPrefs.SetInt(SoundKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }
    }
}
