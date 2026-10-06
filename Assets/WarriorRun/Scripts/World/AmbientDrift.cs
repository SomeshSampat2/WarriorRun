using UnityEngine;
using WarriorRun.Core;

namespace WarriorRun.World
{
    /// <summary>
    /// Keeps an ambient particle emitter floating ahead of the runner — along
    /// the current travel heading, so the dust-mote field stays in front of
    /// the player through corners too. In scenes with no run (main menu) it
    /// simply holds its authored position.
    /// </summary>
    public class AmbientDrift : MonoBehaviour
    {
        [SerializeField] float aheadZ = 22f;
        [SerializeField] float height = 3f;

        Player.PlayerController pc;

        void Update()
        {
            if (pc == null)
            {
                pc = FindFirstObjectByType<Player.PlayerController>();
                if (pc == null) return;
            }
            transform.position = pc.transform.position
                + pc.Forward * aheadZ + Vector3.up * height;
        }
    }
}
