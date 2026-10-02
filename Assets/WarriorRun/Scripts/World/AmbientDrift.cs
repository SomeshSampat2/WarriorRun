using UnityEngine;
using WarriorRun.Core;

namespace WarriorRun.World
{
    /// <summary>
    /// Keeps an ambient particle emitter floating ahead of the runner so the
    /// dust-mote field travels with the player. In scenes with no run
    /// (main menu) it simply holds its authored position.
    /// </summary>
    public class AmbientDrift : MonoBehaviour
    {
        [SerializeField] float aheadZ = 22f;
        [SerializeField] float height = 3f;

        Transform player;

        void Update()
        {
            if (player == null)
            {
                player = GameManager.Instance != null ? GameManager.Instance.PlayerTf : null;
                if (player == null) return;
            }
            transform.position = new Vector3(0f, height, player.position.z + aheadZ);
        }
    }
}
