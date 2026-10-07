using UnityEngine;

namespace WarriorRun.World
{
    /// <summary>
    /// A rope segment baked into a zip tile. Points are child markers in
    /// tile-local space, so the rope works under any tile yaw. The runner
    /// slings under the line at hangDepth and rides it along the polyline;
    /// segments chain across tiles (a segment whose start touches the
    /// previous rope's end) until an isExit rope releases the ride.
    /// </summary>
    public class ZipLine : MonoBehaviour
    {
        public Transform[] points;      // rope polyline markers (children of the tile)
        public float hangDepth = 2.0f;  // runner's feet this far under the cable
        public bool isExit;             // rope end = release and land
        public float coinSpacing = 1.5f;
        public float coinDrop = 1.15f;  // coins hang at chest height below the cable

        float[] cum;    // cumulative segment lengths
        float total = -1f;

        void Ensure()
        {
            if (total >= 0f || points == null || points.Length < 2) return;
            cum = new float[points.Length];
            for (int i = 1; i < points.Length; i++)
            {
                total += Vector3.Distance(points[i - 1].position, points[i].position);
                cum[i] = total;
            }
        }

        public float Length { get { Ensure(); return Mathf.Max(0f, total); } }

        /// <summary>World-space point dist meters along the rope.</summary>
        public Vector3 PointAt(float dist)
        {
            Ensure();
            if (points == null || points.Length == 0) return transform.position;
            if (points.Length == 1 || dist <= 0f) return points[0].position;
            if (dist >= total) return points[points.Length - 1].position;
            int i = 1;
            while (i < cum.Length - 1 && cum[i] < dist) i++;
            float s = dist - cum[i - 1];
            float l = cum[i] - cum[i - 1];
            return Vector3.Lerp(points[i - 1].position, points[i].position, l > 0f ? s / l : 0f);
        }

        /// <summary>Tile-local point dist meters along the rope (coin rows).</summary>
        public Vector3 LocalPointAt(float dist)
        {
            return transform.InverseTransformPoint(PointAt(dist));
        }
    }
}
