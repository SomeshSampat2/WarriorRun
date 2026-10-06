using UnityEngine;
using UnityEngine.UI;
using WarriorRun.Core;
using WarriorRun.World;

namespace WarriorRun.UI
{
    /// <summary>
    /// Active-powerup chips on the HUD: icon + radial countdown that
    /// strobes inside the expiry warning window. Entries are built and
    /// wired by the editor builder — this only polls GameManager.
    /// </summary>
    public class PowerUpHud : MonoBehaviour
    {
        [System.Serializable]
        public class Entry
        {
            public PowerUpKind kind;
            public GameObject root;
            public Image fill;      // radial sweep — 1 = full duration left
            public CanvasGroup cg;  // strobes while expiring
        }

        public Entry[] entries;

        void Update()
        {
            var gm = GameManager.Instance;
            bool running = gm != null && gm.State == RunState.Running;
            foreach (var e in entries)
            {
                if (e == null || e.root == null) continue;
                bool on = running && gm.IsPowerUpActive(e.kind);
                if (e.root.activeSelf != on) e.root.SetActive(on);
                if (!on) continue;
                if (e.fill != null)
                    e.fill.fillAmount = gm.PowerUpFraction(e.kind);
                if (e.cg != null)
                    e.cg.alpha = gm.PowerUpWarning(e.kind)
                        ? (Mathf.PingPong(Time.time * 8f, 2f) < 1f ? 1f : 0.35f) : 1f;
            }
        }
    }
}
