using System.Collections.Generic;
using UnityEngine;

namespace WarriorRun.Player
{
    /// <summary>
    /// One frozen snapshot of the runner spawned by RunnerFx during Boost.
    /// Fades to nothing, destroys itself, and hands baked meshes back to
    /// the shared pool so BakeMesh allocations stay bounded.
    /// </summary>
    public class GhostAfterimage : MonoBehaviour
    {
        float life, t, alpha0;
        Color rgb;
        Mesh baked;          // null for shared (unbaked) meshes
        Queue<Mesh> pool;
        MaterialPropertyBlock mpb;
        Renderer rend;

        public void Init(Mesh mesh, Material mat, float lifetime, Color tint, Queue<Mesh> pool, Mesh baked)
        {
            life = lifetime;
            this.baked = baked;
            this.pool = pool;
            rgb = new Color(tint.r, tint.g, tint.b);
            alpha0 = tint.a;

            var mf = gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            rend = gameObject.AddComponent<MeshRenderer>();
            rend.sharedMaterial = mat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            mpb = new MaterialPropertyBlock();
            Apply(alpha0);
        }

        void Apply(float a)
        {
            mpb.SetColor("_BaseColor", new Color(rgb.r, rgb.g, rgb.b, a));
            rend.SetPropertyBlock(mpb);
        }

        void Update()
        {
            t += Time.deltaTime;
            if (t >= life)
            {
                if (baked != null && pool != null) pool.Enqueue(baked);
                Destroy(gameObject);
                return;
            }
            float k = 1f - t / life;
            Apply(alpha0 * k * k); // quadratic fade — pops then dissolves
        }
    }
}
