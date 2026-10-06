using System.Collections.Generic;
using UnityEngine;
using WarriorRun.Core;

namespace WarriorRun.World
{
    /// <summary>
    /// While the plane ability is airborne this drops coin patterns into the
    /// sky ahead of the runner — lane streams, weaves, double rows and arcs.
    /// Patterns are stamped along the track spine (TrackManager.SamplePath),
    /// so they bend through corners exactly like the floor does.
    /// Coins are pooled and cleared the moment the flight ends.
    /// </summary>
    public class SkyCoinSpawner : MonoBehaviour
    {
        public GameObject coinPrefab;

        [SerializeField] float spawnAhead = 58f;
        [SerializeField] float despawnBehind = 16f;
        [SerializeField] float coinYOffset = 0.9f;   // coin height above the flying player pivot
        [SerializeField] float laneWidth = 2.2f;
        [SerializeField] float interval = 0.6f;

        readonly List<GameObject> live = new();
        readonly Queue<GameObject> pool = new();

        Transform player;
        Player.PlayerController pc;
        TrackManager track;
        float timer;
        int patternIdx;
        bool wasFlying;

        void Start()
        {
            pc = FindFirstObjectByType<Player.PlayerController>();
            player = pc != null ? pc.transform : null;
            track = FindFirstObjectByType<TrackManager>();
            // fill the coin pool up front — pattern bursts would otherwise
            // Instantiate mid-frame and hitch when flight begins
            if (coinPrefab != null)
                for (int i = 0; i < 16; i++) Recycle(Instantiate(coinPrefab));
        }

        void Update()
        {
            if (player == null || coinPrefab == null) return;
            var gm = GameManager.Instance;
            // stay "flying" through pause so coins aren't cleared/resumed oddly
            bool flying = gm != null && gm.PlaneActive
                          && gm.State != RunState.Dead && gm.State != RunState.Ready;

            if (flying && !wasFlying) { timer = 0.15f; patternIdx = 0; }
            if (!flying && wasFlying) ClearAll();
            wasFlying = flying;
            if (!flying) return;

            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                SpawnPattern(player.position.y + coinYOffset);
                timer = interval;
            }

            Vector3 fwd = pc != null ? pc.Forward : Vector3.forward;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var c = live[i];
                if (c == null || !c.activeSelf ||
                    Vector3.Dot(c.transform.position - player.position, fwd) < -despawnBehind)
                {
                    live.RemoveAt(i);
                    Recycle(c);
                }
            }
        }

        void SpawnPattern(float baseY)
        {
            float d0 = (GameManager.Instance != null ? GameManager.Instance.Distance : 0f) + spawnAhead;
            switch (patternIdx++ % 4)
            {
                case 0: // clean stream down one lane
                {
                    int lane = Random.Range(0, 3);
                    for (int i = 0; i < 6; i++)
                        Spawn(d0 + i * 1.9f, (lane - 1) * laneWidth, baseY);
                    break;
                }
                case 1: // weave that crosses every lane
                {
                    for (int i = 0; i < 9; i++)
                        Spawn(d0 + i * 1.7f, Mathf.Sin(i * 0.85f) * laneWidth, baseY);
                    break;
                }
                case 2: // two lanes at once — generous haul
                {
                    int a = Random.Range(0, 3);
                    int b = (a + Random.Range(1, 3)) % 3;
                    for (int i = 0; i < 5; i++)
                    {
                        Spawn(d0 + i * 1.9f, (a - 1) * laneWidth, baseY);
                        Spawn(d0 + i * 1.9f, (b - 1) * laneWidth, baseY);
                    }
                    break;
                }
                default: // smile arc — coins dip and rise across the lanes
                {
                    for (int i = 0; i < 7; i++)
                    {
                        float t = i / 6f;
                        float x = Mathf.Lerp(-laneWidth, laneWidth, t);
                        float y = baseY + Mathf.Sin(t * Mathf.PI) * 0.85f;
                        Spawn(d0 + i * 1.6f, x, y);
                    }
                    break;
                }
            }
        }

        /// <summary>Drop a coin at path-distance <paramref name="pdist"/> + lateral offset.</summary>
        void Spawn(float pdist, float latOff, float y)
        {
            Vector3 pos;
            if (track != null && track.SamplePath(pdist, out var p, out var tan))
            {
                var right = new Vector3(tan.z, 0f, -tan.x);
                pos = p + right * latOff;
            }
            else
            {
                pos = player.position + (pc != null ? pc.Forward : Vector3.forward) * spawnAhead
                      + (pc != null ? pc.RightDir : Vector3.right) * latOff;
            }
            pos.y = y;
            var c = pool.Count > 0 ? pool.Dequeue() : Instantiate(coinPrefab);
            c.transform.SetPositionAndRotation(pos, Quaternion.identity);
            c.SetActive(true);
            live.Add(c);
        }

        void Recycle(GameObject c)
        {
            if (c == null) return;
            c.SetActive(false);
            pool.Enqueue(c);
        }

        void ClearAll()
        {
            foreach (var c in live) Recycle(c);
            live.Clear();
        }
    }
}
