using System.Collections.Generic;
using UnityEngine;
using WarriorRun.Core;

namespace WarriorRun.World
{
    /// <summary>
    /// Recycles a fixed window of track tiles ahead of the player, populating
    /// each with a seeded pattern of obstacles, coins and side decor.
    /// At least one lane is always guaranteed free of blocking obstacles.
    /// </summary>
    public class TrackManager : MonoBehaviour
    {
        [Header("Layout")]
        [SerializeField] float tileLength = 24f;
        [SerializeField] int tilesAhead = 7;
        [SerializeField] float despawnBehind = 30f;
        [SerializeField] float laneWidth = 2.2f;

        [Header("Prefabs (wired by builder)")]
        public GameObject tilePrefab;
        public GameObject lowBarrierPrefab;
        public GameObject highBarrierPrefab;
        public GameObject wallBlockPrefab;
        public GameObject spikePrefab;
        public GameObject coinPrefab;
        public GameObject[] powerUpPrefabs;   // magnet / shield / boost pickups
        public GameObject[] decorPrefabs;   // ruins props beyond the walls
        public GameObject[] wallFeaturePrefabs; // banners/torches/crests on the wall face

        [Header("Environment zones")]
        public Zone[] zones;                 // cycled biome tiles; empty = legacy single tile
        [SerializeField] int tilesPerZone = 5;
        [SerializeField] float fogLerpSpeed = 0.35f;
        [SerializeField] float farGroundDim = 0.62f; // far slab tinted toward the zone's horizon

        [Header("Difficulty")]
        [SerializeField] int safeTiles = 2;
        [SerializeField] float baseObstacleChance = 0.22f;
        [SerializeField] float maxObstacleChance = 0.40f;
        [SerializeField] float coinRowChance = 0.55f;
        [SerializeField] float powerUpChance = 0.07f;

        readonly LinkedList<GameObject> liveTiles = new();
        readonly Dictionary<GameObject, Queue<GameObject>> pools = new();

        Transform player;
        float nextTileZ;
        int tileIndex;
        int runSeed;
        int[] zoneOrder;
        int zoneIdx;
        Color fogTarget;
        Material farGroundMat;

        /// <summary>A biome: tile skin + its own decor/features + fog mood.</summary>
        [System.Serializable]
        public class Zone
        {
            public string name;
            public GameObject tilePrefab;
            public GameObject[] tilePrefabs;      // layout variants — picked at random; falls back to tilePrefab
            public GameObject[] decorPrefabs;
            public GameObject[] featurePrefabs;
            public float decorXMin = 6.2f;
            public float decorXMax = 9.7f;
            public int decorCount = 3;
            public int featureCount = 2;
            public Color fogColor = new Color(0.84f, 0.90f, 0.88f);
            public GameObject[] obstaclePrefabs;  // zone-flavoured obstacles; null = default pool
        }

        Zone CurrentZone
        {
            get
            {
                if (zones == null || zones.Length == 0) return null;
                return zones[zoneOrder[Mathf.Abs(zoneIdx) % zoneOrder.Length]];
            }
        }

        void Start()
        {
            var pc = FindFirstObjectByType<Player.PlayerController>();
            player = pc != null ? pc.transform : null;
            runSeed = Random.Range(0, int.MaxValue);
            nextTileZ = -tileLength; // one tile behind the start line
            tileIndex = 0;
            zoneIdx = 0;
            // shuffle zone order per run — different journey each time, temple first
            if (zones != null && zones.Length > 0)
            {
                zoneOrder = new int[zones.Length];
                for (int i = 0; i < zoneOrder.Length; i++) zoneOrder[i] = i;
                // shuffle zones 1..n — the opening stretch always starts in the temple
                for (int i = zoneOrder.Length - 1; i > 1; i--)
                {
                    int j = Random.Range(1, i + 1);
                    (zoneOrder[i], zoneOrder[j]) = (zoneOrder[j], zoneOrder[i]);
                }
                fogTarget = zones[0].fogColor;
            }
            // the distant fallback slab is tinted per-zone so no blank void
            // shows past the tile beds while running between biomes
            var fg = GameObject.Find("FarGround");
            var fr = fg != null ? fg.GetComponent<Renderer>() : null;
            if (fr != null) farGroundMat = fr.material; // instantiates a private copy
            Prewarm();
        }

        /// <summary>
        /// Instantiation is the expensive part of spawning — every pool starts
        /// empty, so the first copies of each tile/decor/obstacle would hitch the
        /// frame the moment they spawn mid-run (and again at every zone change).
        /// Pre-instantiate a small reserve of every prefab and push the meshes to
        /// the GPU up front; the cost lands during the loading screen instead.
        /// </summary>
        void Prewarm()
        {
            var seen = new HashSet<GameObject>();
            var list = new List<(GameObject prefab, int count)>();
            void Add(GameObject p, int n) { if (p != null && seen.Add(p)) list.Add((p, n)); }
            void AddMany(GameObject[] arr, int n) { if (arr != null) foreach (var p in arr) Add(p, n); }

            Add(tilePrefab, 2);
            Add(coinPrefab, 14);
            Add(spikePrefab, 3);
            Add(lowBarrierPrefab, 3);
            Add(highBarrierPrefab, 3);
            Add(wallBlockPrefab, 3);
            AddMany(powerUpPrefabs, 2);
            AddMany(decorPrefabs, 4);
            AddMany(wallFeaturePrefabs, 3);
            if (zones != null)
                foreach (var z in zones)
                {
                    if (z == null) continue;
                    Add(z.tilePrefab, 2);
                    AddMany(z.tilePrefabs, 2);
                    AddMany(z.decorPrefabs, 4);
                    AddMany(z.featurePrefabs, 3);
                    AddMany(z.obstaclePrefabs, 3);
                }

            var meshes = new HashSet<Mesh>();
            foreach (var (prefab, count) in list)
            {
                foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null && meshes.Add(mf.sharedMesh))
                        mf.sharedMesh.UploadMeshData(false);
                for (int i = 0; i < count; i++)
                    ReturnToPool(GetFromPool(prefab));
            }
        }

        void Update()
        {
            if (player == null) return;
            float pz = player.position.z;

            while (nextTileZ < pz + tilesAhead * tileLength)
                SpawnTile();

            while (liveTiles.Count > 0 &&
                   liveTiles.First.Value.transform.position.z + tileLength < pz - despawnBehind)
                RecycleOldest();

            // ease the fog toward the active zone's mood
            var z = CurrentZone;
            if (z != null)
            {
                if (RenderSettings.fog)
                    RenderSettings.fogColor = Color.Lerp(RenderSettings.fogColor, z.fogColor,
                        Time.deltaTime * fogLerpSpeed);
                if (farGroundMat != null)
                    farGroundMat.SetColor("_BaseColor",
                        Color.Lerp(farGroundMat.GetColor("_BaseColor"), z.fogColor * farGroundDim,
                            Time.deltaTime * fogLerpSpeed));
            }
        }

        void SpawnTile()
        {
            var zone = CurrentZone;
            var prefab = tilePrefab;
            if (zone != null)
            {
                if (zone.tilePrefabs != null && zone.tilePrefabs.Length > 0)
                    prefab = zone.tilePrefabs[Random.Range(0, zone.tilePrefabs.Length)];
                else if (zone.tilePrefab != null)
                    prefab = zone.tilePrefab;
            }
            var tile = GetFromPool(prefab);
            tile.transform.position = new Vector3(0f, 0f, nextTileZ);
            tile.SetActive(true);

            var chunk = tile.GetComponent<TrackChunk>();
            if (chunk == null) chunk = tile.AddComponent<TrackChunk>();
            chunk.Clear();

            var rng = new System.Random(runSeed + tileIndex);
            Populate(tile.transform, tileIndex, rng, chunk, zone);

            liveTiles.AddLast(tile);
            nextTileZ += tileLength;
            tileIndex++;
            zoneIdx = tileIndex / tilesPerZone;
        }

        void RecycleOldest()
        {
            var tile = liveTiles.First.Value;
            liveTiles.RemoveFirst();

            var chunk = tile.GetComponent<TrackChunk>();
            if (chunk != null)
            {
                foreach (var go in chunk.Spawned)
                    ReturnToPool(go);
                chunk.Clear();
            }
            ReturnToPool(tile);
        }

        // ---- population ----

        void Populate(Transform tileRoot, int index, System.Random rng, TrackChunk chunk, Zone zone)
        {
            var decor = zone != null && zone.decorPrefabs != null && zone.decorPrefabs.Length > 0
                ? zone.decorPrefabs : decorPrefabs;
            var features = zone != null && zone.featurePrefabs != null && zone.featurePrefabs.Length > 0
                ? zone.featurePrefabs : wallFeaturePrefabs;
            float xMin = zone != null ? zone.decorXMin : 6.2f;
            float xMax = zone != null ? zone.decorXMax : 9.7f;
            int decorCount = zone != null ? zone.decorCount : 3;
            int featureCount = zone != null ? zone.featureCount : 2;

            // side props — houses / trees / rocks / ruins, depending on the zone
            for (int i = 0; i < decorCount; i++)
            {
                if (decor == null || decor.Length == 0) break;
                var prefab = decor[rng.Next(decor.Length)];
                float side = rng.Next(2) == 0 ? -1f : 1f;
                float x = side * (xMin + (float)rng.NextDouble() * (xMax - xMin));
                float z = (float)rng.NextDouble() * tileLength;
                var d = GetFromPool(prefab);
                float wx = tileRoot.position.x + x;
                float wz = tileRoot.position.z + z;
                // snap the prop's base onto whatever surface lies beneath it —
                // meadow, canyon bed, sidewalk, temple undercroft all differ
                d.transform.SetPositionAndRotation(
                    new Vector3(wx, SurfaceAt(tileRoot, wx, wz), wz),
                    Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
                d.SetActive(true);
                chunk.Spawned.Add(d);
            }

            // wall features — banners / torches / crests hugging the inner face
            if (features != null && features.Length > 0)
            {
                for (int i = 0; i < featureCount; i++)
                {
                    var prefab = features[rng.Next(features.Length)];
                    float side = rng.Next(2) == 0 ? -1f : 1f;
                    float z = 2f + (float)rng.NextDouble() * (tileLength - 4f);
                    var f = GetFromPool(prefab);
                    f.transform.SetPositionAndRotation(
                        tileRoot.position + new Vector3(side * 3.78f, 0f, z),
                        Quaternion.Euler(0f, side < 0 ? 90f : -90f, 0f));
                    f.SetActive(true);
                    chunk.Spawned.Add(f);
                }
            }

            if (index < safeTiles) return; // opening stretch stays clear

            float difficulty = Mathf.Clamp01((player.position.z + index * tileLength) / 1500f);
            float obstacleChance = Mathf.Lerp(baseObstacleChance, maxObstacleChance, difficulty);

            // one candidate row per tile — coins or a single obstacle row
            for (int row = 0; row < 1; row++)
            {
                float z = 8f + (float)rng.NextDouble() * 8f;
                double roll = rng.NextDouble();

                if (roll < powerUpChance && powerUpPrefabs != null && powerUpPrefabs.Length > 0)
                    SpawnPowerUp(tileRoot.position + new Vector3(0f, 0f, z), rng, chunk);
                else if (roll < powerUpChance + coinRowChance)
                    SpawnCoinRow(tileRoot.position + new Vector3(0f, 0f, z), rng, chunk);
                else if (roll < powerUpChance + coinRowChance + obstacleChance)
                    SpawnObstacleRow(tileRoot.position + new Vector3(0f, 0f, z), rng, chunk, difficulty, zone);
            }
        }

        void SpawnObstacleRow(Vector3 rowPos, System.Random rng, TrackChunk chunk, float difficulty, Zone zone)
        {
            // block 1-2 lanes, always keep >= 1 free lane
            int blocked = rng.NextDouble() < 0.35 + difficulty * 0.3 ? 2 : 1;

            var lanes = new List<int> { 0, 1, 2 };
            Shuffle(lanes, rng);

            for (int i = 0; i < blocked; i++)
            {
                int lane = lanes[i];
                GameObject prefab = PickObstacle(rng, zone);
                var go = GetFromPool(prefab);
                float ox = rowPos.x + (lane - 1) * laneWidth;
                float oz = rowPos.z;
                go.transform.SetPositionAndRotation(
                    new Vector3(ox, SurfaceAt(chunk.transform, ox, oz), oz),
                    prefab.transform.rotation);
                go.SetActive(true);
                chunk.Spawned.Add(go);
            }

            // sprinkle coins on a free lane
            if (rng.NextDouble() < 0.5)
            {
                int freeLane = lanes[blocked];
                for (int c = 0; c < 3; c++)
                    SpawnCoin(rowPos + new Vector3((freeLane - 1) * laneWidth, 0f, -2f + c * 2f), chunk); // chunk carries the tile transform
            }
        }

        GameObject PickObstacle(System.Random rng, Zone zone)
        {
            if (zone != null && zone.obstaclePrefabs != null && zone.obstaclePrefabs.Length > 0)
                return zone.obstaclePrefabs[rng.Next(zone.obstaclePrefabs.Length)];
            double r = rng.NextDouble();
            if (spikePrefab != null && r < 0.18) return spikePrefab;
            if (r < 0.45) return lowBarrierPrefab;
            if (r < 0.78) return highBarrierPrefab;
            return wallBlockPrefab;
        }

        void SpawnPowerUp(Vector3 rowPos, System.Random rng, TrackChunk chunk)
        {
            var prefab = powerUpPrefabs[rng.Next(powerUpPrefabs.Length)];
            var go = GetFromPool(prefab);
            int lane = rng.Next(3);
            float px = rowPos.x + (lane - 1) * laneWidth;
            go.transform.SetPositionAndRotation(
                new Vector3(px, SurfaceAt(chunk.transform, px, rowPos.z) + 1.15f, rowPos.z), Quaternion.identity);
            go.SetActive(true);
            chunk.Spawned.Add(go);
        }

        void SpawnCoinRow(Vector3 rowPos, System.Random rng, TrackChunk chunk)
        {
            int lane = rng.Next(3);
            for (int c = 0; c < 5; c++)
                SpawnCoin(rowPos + new Vector3((lane - 1) * laneWidth, 0f, c * 1.6f), chunk);
        }

        void SpawnCoin(Vector3 pos, TrackChunk chunk)
        {
            var go = GetFromPool(coinPrefab);
            float y = SurfaceAt(chunk.transform, pos.x, pos.z) + 0.78f; // hover close above the real floor
            go.transform.SetPositionAndRotation(new Vector3(pos.x, y, pos.z), Quaternion.identity);
            go.SetActive(true);
            chunk.Spawned.Add(go);
        }

        /// <summary>
        /// Height of the solid surface under world (x,z), limited to colliders
        /// that belong to the tile itself. Every tile slab — path, meadow,
        /// canyon bed, sidewalk, sand bed — keeps its collider, so pickups and
        /// props sit on the *visible* ground in every biome instead of floating
        /// above flat zones or sinking into raised ones. Previously spawned
        /// decor/obstacles on the same tile are ignored.
        /// </summary>
        static float SurfaceAt(Transform tile, float x, float z, float fallback = 0.02f)
        {
            var hits = Physics.RaycastAll(new Vector3(x, 14f, z), Vector3.down, 34f,
                ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            foreach (var h in hits)
                if (h.collider.transform.IsChildOf(tile) && h.point.y > best)
                    best = h.point.y;
            return float.IsNegativeInfinity(best) ? fallback : best;
        }

        static void Shuffle<T>(List<T> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        // ---- pooling ----

        GameObject GetFromPool(GameObject prefab)
        {
            if (pools.TryGetValue(prefab, out var q) && q.Count > 0)
                return q.Dequeue();
            var go = Instantiate(prefab);
            go.GetComponent<PooledObject>()?.SetPrefab(prefab);
            if (go.GetComponent<PooledObject>() == null)
                go.AddComponent<PooledObject>().SetPrefab(prefab);
            return go;
        }

        void ReturnToPool(GameObject go)
        {
            if (go == null) return;
            var p = go.GetComponent<PooledObject>();
            var prefab = p != null ? p.Prefab : null;
            go.SetActive(false);
            if (prefab == null) { Destroy(go); return; }
            if (!pools.TryGetValue(prefab, out var q)) pools[prefab] = q = new Queue<GameObject>();
            q.Enqueue(go);
        }
    }

    /// <summary>Marker attached to pooled instances to remember their prefab.</summary>
    public class PooledObject : MonoBehaviour
    {
        public GameObject Prefab { get; private set; }
        public void SetPrefab(GameObject p) => Prefab = p;
    }

    /// <summary>Per-tile record of everything spawned on it, for clean recycling.</summary>
    public class TrackChunk : MonoBehaviour
    {
        public List<GameObject> Spawned { get; } = new();
        public void Clear() => Spawned.Clear();
    }
}
