using UnityEngine;
using UnityEngine.UI;
using TMPro;
using WarriorRun.Core;

namespace WarriorRun.UI
{
    /// <summary>
    /// Binds HUD labels and panels to GameManager state.
    /// All references are wired by the editor builder.
    /// </summary>
    public class GameUI : MonoBehaviour
    {
        [Header("HUD")]
        public TMP_Text scoreText;
        public TMP_Text coinText;
        public GameObject hudRoot;
        public GameObject pauseButton;
        public RectTransform turnArrow;     // chevron container — flips X for left turns
        public Image[] turnChevrons;        // tinted: gold when armed, green once swiped

        [Header("Panels")]
        public GameObject readyPanel;
        public GameObject pausePanel;
        public GameObject deadPanel;

        [Header("Ready panel")]
        public TMP_Text readyBestText;

        [Header("Dead panel")]
        public TMP_Text deadScoreText;
        public TMP_Text deadBestText;
        public TMP_Text deadCoinText;

        void Start()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            gm.StateChanged += OnState;
            gm.ScoreChanged += RefreshHud;
            OnState(gm.State);
            RefreshHud();
        }

        void OnDestroy()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            gm.StateChanged -= OnState;
            gm.ScoreChanged -= RefreshHud;
        }

        int lastScore = -1, lastCoins = -1;
        Player.PlayerController pc;
        static readonly Color ChevArmed = new Color(1f, 0.72f, 0.24f);  // warning gold
        static readonly Color ChevQueued = new Color(0.42f, 0.96f, 0.45f); // locked-in green

        /// <summary>Polls the runner for an armed corner and flashes the HUD arrow.</summary>
        void Update()
        {
            int dir = 0;
            bool queued = false;
            var gm = GameManager.Instance;
            if (gm != null && gm.State == RunState.Running)
            {
                if (pc == null) pc = FindFirstObjectByType<Player.PlayerController>();
                if (pc != null) { dir = pc.ArmedTurnDir; queued = pc.TurnQueued; }
            }

            bool on = dir != 0;
            if (turnArrow != null && turnArrow.gameObject.activeSelf != on)
                turnArrow.gameObject.SetActive(on);
            if (!on || turnArrow == null) return;

            // flip each chevron for left turns — the container itself is
            // scale-animated by Pulse, so the flip lives on the children
            float fx = dir < 0 ? -1f : 1f;
            if (turnChevrons != null)
            {
                var col = queued ? ChevQueued : ChevArmed;
                foreach (var img in turnChevrons)
                {
                    if (img == null) continue;
                    img.color = col;
                    var ls = img.rectTransform.localScale;
                    if (ls.x != fx) img.rectTransform.localScale = new Vector3(fx, ls.y, ls.z);
                }
            }
        }

        void RefreshHud()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            // only rewrite when a value actually changed — per-frame text churn
            // allocates strings and forces TMP re-layout every frame
            if (gm.Score != lastScore)
            {
                lastScore = gm.Score;
                if (scoreText != null) scoreText.text = lastScore.ToString();
            }
            if (gm.Coins != lastCoins)
            {
                lastCoins = gm.Coins;
                if (coinText != null) coinText.text = lastCoins.ToString();
            }
        }

        void OnState(RunState s)
        {
            var gm = GameManager.Instance;
            SetActive(readyPanel, s == RunState.Ready);
            SetActive(pausePanel, s == RunState.Paused);
            SetActive(deadPanel, s == RunState.Dead);
            SetActive(hudRoot, s == RunState.Running || s == RunState.Paused);
            SetActive(pauseButton, s == RunState.Running);

            if (s == RunState.Ready && gm != null && readyBestText != null)
                readyBestText.text = gm.BestScore > 0 ? "BEST  " + gm.BestScore : "";

            if (s == RunState.Dead && gm != null)
            {
                if (deadScoreText != null) deadScoreText.text = gm.Score.ToString();
                if (deadBestText != null) deadBestText.text = "BEST  " + gm.BestScore;
                if (deadCoinText != null) deadCoinText.text = gm.Coins.ToString();
            }
        }

        static void SetActive(GameObject go, bool on)
        {
            if (go != null && go.activeSelf != on) go.SetActive(on);
        }

        // ---- button handlers (wired in builder) ----
        public void OnPauseClicked() { GameManager.Instance?.Pause(); AudioManager.Instance?.Play(Sfx.Click); }
        public void OnResumeClicked() { GameManager.Instance?.Resume(); AudioManager.Instance?.Play(Sfx.Click); }
        public void OnRestartClicked() { AudioManager.Instance?.Play(Sfx.Click); GameManager.Instance?.Restart(); }
        public void OnMenuClicked() { AudioManager.Instance?.Play(Sfx.Click); GameManager.Instance?.GoToMenu(); }
    }
}
