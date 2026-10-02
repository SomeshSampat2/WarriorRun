using UnityEngine;
using WarriorRun.Core;

namespace WarriorRun.World
{
    /// <summary>
    /// Distant scenery (mountains, clouds): children drift toward the camera
    /// at a fraction of run speed and wrap around, giving cheap depth parallax.
    /// </summary>
    public class ParallaxScroller : MonoBehaviour
    {
        [SerializeField] float speedFactor = 0.10f;
        [SerializeField] float loopLength = 160f;
        [SerializeField] float killZ = -30f;
        [SerializeField] float drift = 0f;   // constant extra drift (clouds)

        Transform[] items;

        void Start()
        {
            int n = transform.childCount;
            items = new Transform[n];
            for (int i = 0; i < n; i++) items[i] = transform.GetChild(i);
        }

        void Update()
        {
            if (items == null) return;
            var gm = GameManager.Instance;
            float speed = (gm != null && gm.State == RunState.Running ? gm.CurrentSpeed : 1.5f);
            float dz = (speed * speedFactor + drift) * Time.deltaTime;
            foreach (var t in items)
            {
                if (t == null) continue;
                var p = t.position;
                p.z -= dz;
                if (p.z < killZ) p.z += loopLength;
                t.position = p;
            }
        }
    }
}
