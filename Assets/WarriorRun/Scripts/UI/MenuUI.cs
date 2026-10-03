using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using WarriorRun.Core;

namespace WarriorRun.UI
{
    /// <summary>Main menu bindings: play button, best score, sound toggle.</summary>
    public class MenuUI : MonoBehaviour
    {
        public TMP_Text bestText;
        public TMP_Text soundLabel;

        void Awake() => Debug.Log("[WR] MenuUI.Awake");

        void Start()
        {
            Debug.Log("[WR] MenuUI.Start");
            AudioManager.Instance?.PlayMusic(); // menu groove — carries into gameplay
            if (bestText != null)
                bestText.text = SaveSystem.BestScore > 0 ? "BEST  " + SaveSystem.BestScore : "";
            RefreshSoundLabel();
        }

        public void OnPlayClicked()
        {
            AudioManager.Instance?.Play(Sfx.Start);
            LoadingOverlay.RunToScene(1);   // async load behind the loading screen — no freeze-frame transition
        }

        public void OnSoundClicked()
        {
            SaveSystem.SoundOn = !SaveSystem.SoundOn;
            AudioManager.Instance?.Play(Sfx.Click);
            if (SaveSystem.SoundOn) AudioManager.Instance?.PlayMusic();
            else AudioManager.Instance?.StopMusic();
            RefreshSoundLabel();
        }

        void RefreshSoundLabel()
        {
            if (soundLabel != null)
                soundLabel.text = SaveSystem.SoundOn ? "SOUND  ON" : "SOUND  OFF";
        }
    }
}
