using System.Collections.Generic;
using UnityEngine;
using WarriorRun.Core;

namespace WarriorRun.World
{
    /// <summary>
    /// Recycles a fixed window of track tiles ahead of the player along a
    /// wandering path — straight runs punctuated by 90° corner junctions the
    /// player swipes to take (Temple Run style). Tiles are positioned by a
    /// path cursor (position + heading); everything spawned on a tile is
    /// placed in the tile's LOCAL frame so the whole world bends with it.
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
        public GameObject gemPrefab;        // rare — worth a stack of coins
        public GameObject turnLeftPrefab;
        public GameObject turnRightPrefab;
        public GameObject[] powerUpPrefabs;   // magnet / shield / boost pickups
        public GameObject[] decorPrefabs;   // ruins props beyond the walls
        public GameObject[] wallFeaturePrefabs; // banners/torches/crests on the wall face
        public GameObject[] extraObstaclePrefabs; // shared surprise mix-ins on top of zone pools

        [Header("Environment zones")]
        public Zone[] zones;                 // cycled biome tiles; empty = legacy single tile
        [SerializeField] int tilesPerZone = 14;
        [SerializeField] float fogLerpSpeed = 0.35f;
        [SerializeField] float farGroundDim = 0.62f; // far slab tinted toward the zone's horizon

        [Header("Turns")]
        [SerializeField] int firstTurnTile = 8;    // opening stretch stays straight
        [SerializeField] int minTurnGap = 4;       // straight tiles between corners
        [SerializeField] int maxTurnGap = 9;
        [SerializeField] float doubleTurnChance = 0.14f; // chance of an immediate second corner (S-bend)
        public float turnRadius = 4.6f;            // arc the player follows mid-corner

        [Header("Difficulty")]
        [SerializeField] int safeTiles = 2;
        [SerializeField] float baseObstacleChance = 0.18f;
        [SerializeField] float maxObstacleChance = 0.33f;
        [SerializeField] float coinRowChance = 0.55f;
        [SerializeField] float powerUpChance = 0.07f;
        [SerializeField] float gemChance = 0.05f; // per-coin chance to upgrade to a gem

        /// <summary>A corner the runner is approaching — read by PlayerController.</summary>
        public struct TurnPlan
        {
            public Vector3 arcStart;   // world point where the bend begins
            public Vector3 exitPoint;  // where the straight resumes, on the new heading
            public float yawIn, yawOut;
            public int dir;            // +1 = right, -1 = left
            public float radius;
        }

        /// <summary>One spawned tile's slice of the path, for distance→world sampling.</summary>
        struct PathLeg
        {
            public Vector3 start, end;          // entry / exit edge centres
            public float yawIn, yawOut;
            public bool corner;
            public int dir;
            public float dist0, len;            // path-distance span covered
            public Vector3 arcStart, exitPoint; // corner geometry (corners only)
        }

        readonly LinkedList<GameObject> liveTiles = new();
        readonly LinkedList<TurnPlan> turns = new();
        readonly List<PathLeg> legs = new();   // kept 1:1 with liveTiles
        readonly Dictionary<GameObject, Queue<GameObject>> pools = new();

        Transform player;
        Transform farGround;
        float farGroundY;
        Vector3 cursorPos;    // next tile's entry-edge centre
        float cursorYaw;      // heading (deg) at the entry edge
        float spawnDist;      // path distance spawned so far
        int sinceTurn;
        int nextTurnGap;
        int tileIndex;
        int runSeed;
        int[] zoneOrder;
        int zoneIdx;
        int lastBlockedLane = -1;
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
            public bool singleLane;             // one centre lane only — no left/right (lava chasm)
            public GameObject[] tileSequence;   // ordered tile layout (zip gorge); overrides tilePrefabs
            public bool noTurns;                // never bend the path inside this zone
        }

        Zone CurrentZone
        {
            get
            {
                if (zones == null || zones.Length == 0) return null;
                return zones[zoneOrder[Mathf.Abs(zoneIdx) % zoneOrder.Length]];
            }
        }

        static Vector3 Heading(float yawDeg)
        {
            float r = yawDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
        }

        /// <summary>Force a named biome into a fixed slot of the shuffled rotation.</summary>
        void PinZone(string zoneName, int slot)
        {
            if (zones == null || zoneOrder == null || zoneOrder.Length <= slot) return;
            for (int i = 0; i < zones.Length; i++)
                if (zones[i] != null && zones[i].name == zoneName && zoneOrder[slot] != i)
                {
                    int at = System.Array.IndexOf(zoneOrder, i);
                    if (at > slot) (zoneOrder[at], zoneOrder[slot]) = (zoneOrder[slot], zoneOrder[at]);
                    break;
                }
        }

        void Start()
        {
            var pc = FindFirstObjectByType<Player.PlayerController>();
            player = pc != null ? pc.transform : null;
            runSeed = Random.Range(0, int.MaxValue);
            cursorPos = new Vector3(0f, 0f, -tileLength); // one tile behind the start line
            cursorYaw = 0f;
            spawnDist = -tileLength;
            tileIndex = 0;
            zoneIdx = 0;
            sinceTurn = 0;
            nextTurnGap = Random.Range(minTurnGap, maxTurnGap + 1);
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
                // the lava chasm is the signature biome — pin it third in the
                // rotation so every run reaches it within ~700 m instead of
                // possibly waiting through a full ten-zone shuffle
                // the zip gorge is the second biome — right after the temple
                // opener (~336 m in) so the rope ride is reached fast; the
                // lava chasm follows as the third stop in the rotation
                PinZone("Gorge", 1);
                PinZone("Volcano", 2);
                fogTarget = zones[0].fogColor;
            }
            // the distant fallback slab is tinted per-zone and follows the
            // runner so no blank void shows past the tile beds after a corner
            farGround = GameObject.Find("FarGround")?.transform;
            var fr = farGround != null ? farGround.GetComponent<Renderer>() : null;
            if (fr != null) farGroundMat = fr.material; // instantiates a private copy
            if (farGround != null) farGroundY = farGround.position.y;
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
            Add(turnLeftPrefab, 1);
            Add(turnRightPrefab, 1);
            Add(coinPrefab, 14);
            Add(gemPrefab, 2);
            Add(spikePrefab, 3);
            Add(lowBarrierPrefab, 3);
            Add(highBarrierPrefab, 3);
            Add(wallBlockPrefab, 3);
            AddMany(powerUpPrefabs, 2);
            AddMany(extraObstaclePrefabs, 3);
            AddMany(decorPrefabs, 4);
            AddMany(wallFeaturePrefabs, 3);
            if (zones != null)
                foreach (var z in zones)
                {
                    if (z == null) continue;
                    Add(z.tilePrefab, 2);
                    AddMany(z.tilePrefabs, 2);
                    AddMany(z.tileSequence, 2);
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

        float PlayerDist => GameManager.Instance != null ? GameManager.Instance.Distance : 0f;

        /// <summary>The biome under the runner right now — drives per-zone lane rules.</summary>
        public Zone PlayerZone
        {
            get
            {
                float pd = PlayerDist;
                foreach (var t in liveTiles)
                {
                    var c = t.GetComponent<TrackChunk>();
                    if (c != null && c.endDist > pd) return c.zone;
                }
                return null;
            }
        }

        /// <summary>True while the runner is inside a one-lane zone (lava chasm).</summary>
        public bool PlayerSingleLane => PlayerZone != null && PlayerZone.singleLane;

        void Update()
        {
            if (player == null) return;
            float pd = PlayerDist;

            while (spawnDist < pd + tilesAhead * tileLength)
                SpawnTile();

            while (liveTiles.Count > 0)
            {
                var c = liveTiles.First.Value.GetComponent<TrackChunk>();
                if (c == null || c.endDist >= pd - despawnBehind) break;
                RecycleOldest();
            }

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

            if (farGround != null)
                farGround.position = new Vector3(player.position.x, farGroundY, player.position.z);
        }

        // ---- path helpers ----

        /// <summary>The next unconsumed corner on the path, if any.</summary>
        public bool PeekTurn(out TurnPlan plan)
        {
            if (turns.Count > 0)
            {
                plan = turns.First.Value;
                return true;
            }
            plan = default;
            return false;
        }

        /// <summary>The player took (or blew past) the front corner — pop it.</summary>
        public void ConsumeTurn()
        {
            if (turns.Count > 0) turns.RemoveFirst();
        }

        /// <summary>Missed-corner bookkeeping — same pop, clearer call site.</summary>
        public void SkipTurn() => ConsumeTurn();

        /// <summary>
        /// World position + tangent at a path distance (meters along the spine
        /// from the run start). Past the spawned window it extrapolates the
        /// final leg, so it is always safe to call for "ahead" distances.
        /// </summary>
        public bool SamplePath(float dist, out Vector3 pos, out Vector3 tangent)
        {
            pos = Vector3.zero;
            tangent = Vector3.forward;
            if (legs.Count == 0) return false;

            int i = 0;
            while (i < legs.Count - 1 && dist >= legs[i].dist0 + legs[i].len) i++;
            var l = legs[i];
            float s = dist - l.dist0;
            Vector3 dirIn = Heading(l.yawIn);
            Vector3 dirOut = Heading(l.yawOut);

            if (!l.corner)
            {
                tangent = dirIn;
                pos = l.start + dirIn * s;
                return true;
            }

            float half = tileLength * 0.5f;
            float sIn = half - turnRadius;               // straight lead-in
            float arcLen = turnRadius * Mathf.PI * 0.5f; // quarter arc
            if (s <= sIn)
            {
                tangent = dirIn;
                pos = l.start + dirIn * s;
            }
            else if (s <= sIn + arcLen)
            {
                float phi = (s - sIn) / turnRadius;
                Vector3 centre = l.arcStart + dirOut * turnRadius;
                pos = centre + turnRadius * (dirIn * Mathf.Sin(phi) - dirOut * Mathf.Cos(phi));
                tangent = dirIn * Mathf.Cos(phi) + dirOut * Mathf.Sin(phi);
            }
            else
            {
                tangent = dirOut;
                pos = l.exitPoint + dirOut * (s - sIn - arcLen);
            }
            return true;
        }

        // ---- spawning ----

        void SpawnTile()
        {
            var zone = CurrentZone;
            bool corner = turnLeftPrefab != null && turnRightPrefab != null
                          && tileIndex >= firstTurnTile && sinceTurn >= nextTurnGap
                          && !(zone != null && zone.noTurns);
            if (corner)
                SpawnCorner(Random.value < 0.5f ? -1 : 1, zone);
            else
                SpawnStraight(zone);
        }

        TrackChunk GetChunk(GameObject tile)
        {
            var chunk = tile.GetComponent<TrackChunk>();
            if (chunk == null) chunk = tile.AddComponent<TrackChunk>();
            chunk.Clear();
            return chunk;
        }

        void SpawnStraight(Zone zone)
        {
            var prefab = tilePrefab;
            if (zone != null)
            {
                if (zone.tileSequence != null && zone.tileSequence.Length > 0)
                    // authored order — the zip gorge runs entry → gorge → landing
                    prefab = zone.tileSequence[tileIndex % tilesPerZone % zone.tileSequence.Length];
                else if (zone.tilePrefabs != null && zone.tilePrefabs.Length > 0)
                    prefab = zone.tilePrefabs[Random.Range(0, zone.tilePrefabs.Length)];
                else if (zone.tilePrefab != null)
                    prefab = zone.tilePrefab;
            }
            var tile = GetFromPool(prefab);
            tile.transform.SetPositionAndRotation(cursorPos, Quaternion.Euler(0f, cursorYaw, 0f));
            tile.SetActive(true);

            var chunk = GetChunk(tile);
            chunk.endDist = spawnDist + tileLength;
            chunk.zone = zone;
            legs.Add(new PathLeg
            {
                start = cursorPos, end = cursorPos + Heading(cursorYaw) * tileLength,
                yawIn = cursorYaw, yawOut = cursorYaw,
                dist0 = spawnDist, len = tileLength,
            });

            var rng = new System.Random(runSeed + tileIndex);
            // the tile right before a junction keeps the approach readable —
            // soften its obstacle odds so a blocker can't hide the corner
            bool cornerNext = turnLeftPrefab != null && turnRightPrefab != null
                              && tileIndex + 1 >= firstTurnTile && sinceTurn + 1 >= nextTurnGap;
            Populate(tile.transform, tileIndex, rng, chunk, zone, cornerNext);

            liveTiles.AddLast(tile);
            cursorPos += Heading(cursorYaw) * tileLength;
            spawnDist += tileLength;
            tileIndex++;
            // a noTurns zone (the zip gorge) must not bank junction credit —
            // otherwise a corner lands on the very first tile past the rope
            // and ambushes the runner the moment they touch down
            sinceTurn = zone != null && zone.noTurns ? 0 : sinceTurn + 1;
            zoneIdx = tileIndex / tilesPerZone;
        }

        void SpawnCorner(int dir, Zone zone)
        {
            var prefab = dir > 0 ? turnRightPrefab : turnLeftPrefab;
            var tile = GetFromPool(prefab);
            tile.transform.SetPositionAndRotation(cursorPos, Quaternion.Euler(0f, cursorYaw, 0f));
            tile.SetActive(true);

            // the dead-end kill trigger is baked in — a shield/vehicle smash
            // disables it, so re-arm it every time the junction is reused
            var de = tile.transform.Find("DeadEnd");
            if (de != null) de.gameObject.SetActive(true);

            float half = tileLength * 0.5f;
            float yawIn = cursorYaw;
            float yawOut = cursorYaw + 90f * dir;
            Vector3 dirIn = Heading(yawIn);
            Vector3 dirOut = Heading(yawOut);
            Vector3 centre = cursorPos + dirIn * half;
            Vector3 arcStart = centre - dirIn * turnRadius;
            Vector3 exitPoint = centre + dirOut * turnRadius;
            Vector3 exitEdge = centre + dirOut * half;

            var plan = new TurnPlan
            {
                arcStart = arcStart, exitPoint = exitPoint,
                yawIn = yawIn, yawOut = yawOut,
                dir = dir, radius = turnRadius,
            };
            turns.AddLast(plan);

            float len = 2f * (half - turnRadius) + turnRadius * Mathf.PI * 0.5f;
            legs.Add(new PathLeg
            {
                start = cursorPos, end = exitEdge,
                yawIn = yawIn, yawOut = yawOut,
                corner = true, dir = dir,
                dist0 = spawnDist, len = len,
                arcStart = arcStart, exitPoint = exitPoint,
            });

            var chunk = GetChunk(tile);
            chunk.endDist = spawnDist + len;
            chunk.zone = zone;
            PopulateCorner(tile.transform, plan, new System.Random(runSeed + tileIndex), chunk, zone);

            liveTiles.AddLast(tile);
            cursorPos = exitEdge;
            cursorYaw = yawOut;
            spawnDist += len;
            tileIndex++;
            sinceTurn = 0;
            nextTurnGap = Random.value < doubleTurnChance
                ? 1
                : Random.Range(minTurnGap, maxTurnGap + 1);
            zoneIdx = tileIndex / tilesPerZone;
        }

        void RecycleOldest()
        {
            var tile = liveTiles.First.Value;
            liveTiles.RemoveFirst();
            if (legs.Count > 0) legs.RemoveAt(0);

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
        // Everything below works in TILE-LOCAL space (x = lateral, z = along
        // travel) so content lands correctly on straight AND rotated tiles.

        void Populate(Transform tileRoot, int index, System.Random rng, TrackChunk chunk, Zone zone, bool cornerNext)
        {
            var decor = zone != null && zone.decorPrefabs != null && zone.decorPrefabs.Length > 0
                ? zone.decorPrefabs : decorPrefabs;
            var features = zone != null && zone.featurePrefabs != null && zone.featurePrefabs.Length > 0
                ? zone.featurePrefabs : wallFeaturePrefabs;
            float xMin = zone != null ? zone.decorXMin : 6.2f;
            float xMax = zone != null ? zone.decorXMax : 9.7f;
            int decorCount = zone != null ? zone.decorCount : 3;
            int featureCount = zone != null ? zone.featureCount : 2;
            var tileRot = tileRoot.rotation;

            // zip tiles carry their own world — the rope is the content
            var zip = tileRoot.GetComponentInChildren<ZipLine>(true);
            if (zip != null) { PopulateZip(zip, chunk); return; }

            // side props — houses / trees / rocks / ruins, depending on the zone
            for (int i = 0; i < decorCount; i++)
            {
                if (decor == null || decor.Length == 0) break;
                var prefab = decor[rng.Next(decor.Length)];
                float side = rng.Next(2) == 0 ? -1f : 1f;
                float x = side * (xMin + (float)rng.NextDouble() * (xMax - xMin));
                float z = (float)rng.NextDouble() * tileLength;
                var d = GetFromPool(prefab);
                var wp = tileRoot.TransformPoint(new Vector3(x, 0f, z));
                // snap the prop's base onto whatever surface lies beneath it —
                // meadow, canyon bed, sidewalk, temple undercroft all differ
                d.transform.SetPositionAndRotation(
                    new Vector3(wp.x, SurfaceAt(tileRoot, wp.x, wp.z), wp.z),
                    tileRot * Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
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
                        tileRoot.TransformPoint(new Vector3(side * 3.78f, 0f, z)),
                        tileRot * Quaternion.Euler(0f, side < 0 ? 90f : -90f, 0f));
                    f.SetActive(true);
                    chunk.Spawned.Add(f);
                }
            }

            if (index < safeTiles) return; // opening stretch stays clear

            float difficulty = Mathf.Clamp01(spawnDist / 1500f);
            float obstacleChance = Mathf.Lerp(baseObstacleChance, maxObstacleChance, difficulty);
            if (cornerNext) obstacleChance *= 0.35f;

            // one candidate row per tile — coins or a single obstacle row
            float rowZ = 8f + (float)rng.NextDouble() * 8f;
            double roll = rng.NextDouble();

            if (roll < powerUpChance && powerUpPrefabs != null && powerUpPrefabs.Length > 0)
                SpawnPowerUp(tileRoot, rowZ, rng, chunk, zone);
            else if (roll < powerUpChance + coinRowChance)
                SpawnCoinRow(tileRoot, rowZ, rng, chunk, zone);
            else if (roll < powerUpChance + coinRowChance + obstacleChance)
                SpawnObstacleRow(tileRoot, rowZ, rng, chunk, difficulty, zone);
        }

        /// <summary>
        /// Junction dressing: coins tracing the bend, zone decor filling the
        /// blocked quadrant so the corner blends into the current biome.
        /// The walls, gate, sign, chevron and dead-end trigger are baked into
        /// the corner prefab itself.
        /// </summary>
        void PopulateCorner(Transform tileRoot, TurnPlan plan, System.Random rng, TrackChunk chunk, Zone zone)
        {
            var tileRot = tileRoot.rotation;
            var decor = zone != null && zone.decorPrefabs != null && zone.decorPrefabs.Length > 0
                ? zone.decorPrefabs : decorPrefabs;
            int count = Mathf.Max(4, (zone != null ? zone.decorCount : 6) / 2);

            // outer quadrant — the dead side of the plaza, opposite the exit
            for (int i = 0; i < count; i++)
            {
                if (decor == null || decor.Length == 0) break;
                var prefab = decor[rng.Next(decor.Length)];
                float lx = -plan.dir * (5.9f + (float)rng.NextDouble() * 4.6f);
                float lz = 1.5f + (float)rng.NextDouble() * 21f;
                var d = GetFromPool(prefab);
                var wp = tileRoot.TransformPoint(new Vector3(lx, 0f, lz));
                d.transform.SetPositionAndRotation(
                    new Vector3(wp.x, SurfaceAt(tileRoot, wp.x, wp.z), wp.z),
                    tileRot * Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
                d.SetActive(true);
                chunk.Spawned.Add(d);
            }

            // coins riding the bend — a reward line that shows the way around
            float half = tileLength * 0.5f;
            var arcCentreL = new Vector3(plan.dir * turnRadius, 0f, half - turnRadius);
            var dirOutL = new Vector3(plan.dir, 0f, 0f);
            for (int i = 0; i < 6; i++)
            {
                float phi = (i + 0.5f) / 6f * Mathf.PI * 0.5f;
                var lp = arcCentreL + turnRadius * (Vector3.forward * Mathf.Sin(phi) - dirOutL * Mathf.Cos(phi));
                SpawnCoin(tileRoot.TransformPoint(lp), chunk);
            }
        }

        void SpawnObstacleRow(Transform tileRoot, float z, System.Random rng, TrackChunk chunk, float difficulty, Zone zone)
        {
            bool single = zone != null && zone.singleLane;
            // block 1-2 lanes, always keep >= 1 free lane — single-lane zones
            // get exactly one centre blocker that must be jumped or slid under
            int blocked = !single && rng.NextDouble() < 0.30 + difficulty * 0.25 ? 2 : 1;

            var lanes = single ? new List<int> { 1 } : new List<int> { 0, 1, 2 };
            Shuffle(lanes, rng);
            if (blocked == 1 && !single)
            {
                // never wall the same lane twice in a row, and ease the
                // centre lane off — a streak of middle blocks reads as spam
                if (lanes[0] == lastBlockedLane ||
                    (lanes[0] == 1 && rng.NextDouble() < 0.4))
                {
                    int j = 1 + rng.Next(2);
                    (lanes[0], lanes[j]) = (lanes[j], lanes[0]);
                }
            }
            lastBlockedLane = blocked == 1 ? lanes[0] : -1;

            for (int i = 0; i < blocked; i++)
            {
                int lane = lanes[i];
                GameObject prefab = PickObstacle(rng, zone);
                var go = GetFromPool(prefab);
                var wp = tileRoot.TransformPoint(new Vector3((lane - 1) * laneWidth, 0f, z));
                go.transform.SetPositionAndRotation(
                    new Vector3(wp.x, SurfaceAt(tileRoot, wp.x, wp.z), wp.z),
                    tileRoot.rotation * prefab.transform.rotation);
                go.SetActive(true);
                chunk.Spawned.Add(go);
            }

            // sprinkle coins on a free lane — over the blocker when there's
            // only one lane, so the row arcs over the thing you jump
            if (rng.NextDouble() < 0.5)
            {
                int freeLane = lanes.Count > blocked ? lanes[blocked] : 1;
                for (int c = 0; c < 3; c++)
                    SpawnCoin(tileRoot.TransformPoint(
                        new Vector3((freeLane - 1) * laneWidth, 0f, z - 2f + c * 2f)), chunk);
            }
        }

        GameObject PickObstacle(System.Random rng, Zone zone)
        {
            bool single = zone != null && zone.singleLane;
            for (int tries = 0; tries < 6; tries++)
            {
                var p = PickObstacleInner(rng, zone);
                // a walled lane-blocker in a one-lane zone is unfair — re-roll
                if (!single) return p;
                var ob = p != null ? p.GetComponentInChildren<Obstacle>(true) : null;
                if (ob == null || ob.kind != ObstacleKind.WallBlock) return p;
            }
            return lowBarrierPrefab != null ? lowBarrierPrefab : spikePrefab;
        }

        GameObject PickObstacleInner(System.Random rng, Zone zone)
        {
            if (zone != null && zone.obstaclePrefabs != null && zone.obstaclePrefabs.Length > 0)
            {
                // occasionally pull a wildcard from the shared pool so zones
                // don't feel locked to the same three blockers
                if (extraObstaclePrefabs != null && extraObstaclePrefabs.Length > 0
                    && rng.NextDouble() < 0.22)
                    return extraObstaclePrefabs[rng.Next(extraObstaclePrefabs.Length)];
                return zone.obstaclePrefabs[rng.Next(zone.obstaclePrefabs.Length)];
            }
            double r = rng.NextDouble();
            if (extraObstaclePrefabs != null && extraObstaclePrefabs.Length > 0 && r < 0.20)
                return extraObstaclePrefabs[rng.Next(extraObstaclePrefabs.Length)];
            if (spikePrefab != null && r < 0.18) return spikePrefab;
            if (r < 0.45) return lowBarrierPrefab;
            if (r < 0.78) return highBarrierPrefab;
            return wallBlockPrefab;
        }

        void SpawnPowerUp(Transform tileRoot, float z, System.Random rng, TrackChunk chunk, Zone zone)
        {
            var prefab = powerUpPrefabs[rng.Next(powerUpPrefabs.Length)];
            var go = GetFromPool(prefab);
            int lane = zone != null && zone.singleLane ? 1 : rng.Next(3);
            var wp = tileRoot.TransformPoint(new Vector3((lane - 1) * laneWidth, 0f, z));
            go.transform.SetPositionAndRotation(
                new Vector3(wp.x, SurfaceAt(tileRoot, wp.x, wp.z) + 1.15f, wp.z), Quaternion.identity);
            go.SetActive(true);
            chunk.Spawned.Add(go);
        }

        void SpawnCoinRow(Transform tileRoot, float z, System.Random rng, TrackChunk chunk, Zone zone)
        {
            int lane = zone != null && zone.singleLane ? 1 : rng.Next(3);
            for (int c = 0; c < 5; c++)
                SpawnCoin(tileRoot.TransformPoint(
                    new Vector3((lane - 1) * laneWidth, 0f, z + c * 1.6f)), chunk);
        }

        void SpawnCoin(Vector3 pos, TrackChunk chunk)
        {
            // occasionally upgrade the pickup to a gem — same fly-in rules
            var go = GetFromPool(gemPrefab != null && Random.value < gemChance ? gemPrefab : coinPrefab);
            float y = SurfaceAt(chunk.transform, pos.x, pos.z) + 0.78f; // hover close above the real floor
            go.transform.SetPositionAndRotation(new Vector3(pos.x, y, pos.z), Quaternion.identity);
            go.SetActive(true);
            chunk.Spawned.Add(go);
        }

        /// <summary>
        /// Zip tiles are self-contained (cliffs, river, rope are baked into the
        /// prefab) — the only dressing needed is the coin line strung along
        /// the cable at grab height. A rare gem rides mid-rope.
        /// </summary>
        void PopulateZip(ZipLine zip, TrackChunk chunk)
        {
            float len = zip.Length;
            int n = Mathf.Max(3, Mathf.FloorToInt(len / zip.coinSpacing));
            for (int i = 0; i < n; i++)
            {
                var lp = zip.LocalPointAt((i + 0.5f) * len / n) + Vector3.down * zip.coinDrop;
                SpawnCoinAt(zip.transform.TransformPoint(lp), chunk);
            }
        }

        /// <summary>Pooled coin at an exact position — rope coins ride the cable, not the floor.</summary>
        void SpawnCoinAt(Vector3 pos, TrackChunk chunk)
        {
            var go = GetFromPool(gemPrefab != null && Random.value < gemChance ? gemPrefab : coinPrefab);
            go.transform.SetPositionAndRotation(pos, Quaternion.identity);
            go.SetActive(true);
            chunk.Spawned.Add(go);
        }

        /// <summary>
        /// The rope continuing past the current one's end — rope segments chain
        /// across tiles, so the next line's start sits where this one finishes.
        /// </summary>
        public ZipLine ZipLineContinuing(ZipLine cur)
        {
            if (cur == null) return null;
            Vector3 end = cur.PointAt(cur.Length);
            foreach (var t in liveTiles)
            {
                var z = t.GetComponentInChildren<ZipLine>(true);
                if (z != null && z != cur && (z.PointAt(0f) - end).sqrMagnitude < 4f)
                    return z;
            }
            return null;
        }

        /// <summary>
        /// Height of the solid surface under world (x,z), limited to colliders
        /// that belong to the tile itself. Every tile slab — path, meadow,
        /// canyon bed, sidewalk, temple undercroft — keeps its collider, so
        /// pickups and props sit on the *visible* ground in every biome
        /// instead of floating above flat zones or sinking into raised ones.
        /// Previously spawned decor/obstacles on the same tile are ignored.
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
        public float endDist;   // path distance at this tile's exit edge
        public TrackManager.Zone zone;   // biome this tile was spawned under
        public void Clear() => Spawned.Clear();
    }
}
