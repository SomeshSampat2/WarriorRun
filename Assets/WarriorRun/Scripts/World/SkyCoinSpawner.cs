using System.Collections.Generic;
using UnityEngine;
using WarriorRun.Core;

namespace WarriorRun.World
{
    /// <summary>
    /// While the plane ability is airborne this drops coin patterns into the
    /// sky ahead of the runner — lane streams, weaves, double rows and arcs.
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
        float timer;
        int patternIdx;
        bool wasFlying;

        void Start()
        {
            var pc = FindFirstObjectByType<Player.PlayerController>();
            player = pc != null ? pc.transform : null;
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

            float behindZ = player.position.z - despawnBehind;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var c = live[i];
                if (c == null || !c.activeSelf || c.transform.position.z < behindZ)
                {
                    live.RemoveAt(i);
                    Recycle(c);
                }
            }
        }

        void SpawnPattern(float baseY)
        {
            float baseZ = player.position.z + spawnAhead;
            switch (patternIdx++ % 4)
            {
                case 0: // clean stream down one lane
                {
                    int lane = Random.Range(0, 3);
                    for (int i = 0; i < 6; i++)
                        Spawn(new Vector3((lane - 1) * laneWidth, baseY, baseZ + i * 1.9f));
                    break;
                }
                case 1: // weave that crosses every lane
                {
                    for (int i = 0; i < 9; i++)
                        Spawn(new Vector3(Mathf.Sin(i * 0.85f) * laneWidth, baseY, baseZ + i * 1.7f));
                    break;
                }
                case 2: // two lanes at once — generous haul
                {
                    int a = Random.Range(0, 3);
                    int b = (a + Random.Range(1, 3)) % 3;
                    for (int i = 0; i < 5; i++)
                    {
                        Spawn(new Vector3((a - 1) * laneWidth, baseY, baseZ + i * 1.9f));
                        Spawn(new Vector3((b - 1) * laneWidth, baseY, baseZ + i * 1.9f));
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
                        Spawn(new Vector3(x, y, baseZ + i * 1.6f));
                    }
                    break;
                }
            }
        }

        void Spawn(Vector3 pos)
        {
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
