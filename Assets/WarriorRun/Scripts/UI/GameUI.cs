using UnityEngine;
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
