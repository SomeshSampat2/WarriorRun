using UnityEngine;

namespace WarriorRun.World
{
    public enum ObstacleKind
    {
        LowBarrier,   // jump over
        HighBarrier,  // slide under
        WallBlock,    // full block, change lane
        Lava          // molten channel — environmental hazard, no shield or smash-through
    }

    /// <summary>Marker for a deadly obstacle. The trigger volume defines the kill zone.</summary>
    public class Obstacle : MonoBehaviour
    {
        public ObstacleKind kind = ObstacleKind.LowBarrier;
    }
}
