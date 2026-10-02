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

        void Start()
        {
            AudioManager.Instance?.PlayMusic(); // menu groove — carries into gameplay
            if (bestText != null)
                bestText.text = SaveSystem.BestScore > 0 ? "BEST  " + SaveSystem.BestScore : "";
            RefreshSoundLabel();
        }

        public void OnPlayClicked()
        {
            AudioManager.Instance?.Play(Sfx.Start);
            SceneManager.LoadScene(1);
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
