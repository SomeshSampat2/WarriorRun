using UnityEngine;
using WarriorRun.Core;

namespace WarriorRun.World
{
    /// <summary>
    /// Distant scenery (mountains, clouds): children drift toward the camera
    /// at a fraction of run speed and wrap around, giving cheap depth parallax.
    /// Positions are kept in the runner's heading frame so the horizon bends
    /// with the path instead of scrolling off sideways on corners.
    /// </summary>
    public class ParallaxScroller : MonoBehaviour
    {
        [SerializeField] float speedFactor = 0.10f;
        [SerializeField] float loopLength = 160f;
        [SerializeField] float killZ = -30f;
        [SerializeField] float drift = 0f;   // constant extra drift (clouds)

        Transform[] items;
        Player.PlayerController pc;
        bool searched;

        void Start()
        {
            int n = transform.childCount;
            items = new Transform[n];
            for (int i = 0; i < n; i++) items[i] = transform.GetChild(i);
        }

        void Update()
        {
            if (items == null) return;
            if (!searched) { pc = FindFirstObjectByType<Player.PlayerController>(); searched = true; }
            var gm = GameManager.Instance;
            float speed = (gm != null && gm.State == RunState.Running ? gm.CurrentSpeed : 1.5f);
            float dz = (speed * speedFactor + drift) * Time.deltaTime;

            if (pc == null)
            {
                foreach (var t in items)
                {
                    if (t == null) continue;
                    var p = t.position;
                    p.z -= dz;
                    if (p.z < killZ) p.z += loopLength;
                    t.position = p;
                }
                return;
            }

            // work in the runner's heading frame: +z is always "ahead"
            var rot = Quaternion.Euler(0f, pc.HeadingYaw, 0f);
            var inv = Quaternion.Inverse(rot);
            Vector3 refPos = pc.transform.position;
            foreach (var t in items)
            {
                if (t == null) continue;
                Vector3 rel = inv * (t.position - refPos);
                rel.z -= dz;
                if (rel.z < killZ) rel.z += loopLength;
                t.position = refPos + rot * rel;
            }
        }
    }
}
