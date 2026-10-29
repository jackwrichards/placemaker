// Placemaker — game runner lab: the shared decal library laid out on a test field (Game/Decals/SharedDecals.cs;
// docs/DYNAMIC-DECALS.md, "The shared library"). GPL-3.0.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Placemaker.Core.Decals.Dynamic;
using Placemaker.Game.Decals;
using Unity.Mathematics;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteRunner
    {
        private readonly Dictionary<string, float2[]> _libraryCells = new Dictionary<string, float2[]>(StringComparer.Ordinal);

        /// <summary>
        /// {"do":"lab","op":"library","kinds":["leafOak","cracks",…] | "all","variants":3,"cell":8,"cols":9,"at":[x,z]}: lays
        /// variants of library kinds (or whole families) on a grid of cells, row after row, each scaled to fit its cell
        /// (strips: two repeats end to end; chains: two pieces), waits until all are drawn and records instances, variants,
        /// GPU memory and how many came from the disk cache. "focus":"leafOak" frames that kind's cells for the next
        /// camera step with target "build"; "clear": true removes the lab's instances.
        /// </summary>
        private IEnumerator LabLibrary(ScenarioState st, JObject step, JObject rec)
        {
            if ((bool?)step["clear"] == true) { rec["cleared"] = SharedDecals.Clear("lab"); _libraryCells.Clear(); yield break; }
            if (step["focus"] != null)
            {
                string f = (string)step["focus"];
                if (!_libraryCells.TryGetValue(f, out var cell)) { rec["error"] = "lab: library kind " + f + " was not laid out"; yield break; }
                float margin = (float?)step["margin"] ?? 1f;
                var lo = cell[0] - margin; var hi = cell[1] + margin;
                st.Ring = new[] { lo, new float2(hi.x, lo.y), hi, new float2(lo.x, hi.y) };
                rec["focus"] = f;
                Event("build");
                yield break;
            }
            if (DecalLibrary.Current.Count == 0) DynamicDecalSystem.LoadLibrary();
            var lib = DecalLibrary.Current;
            var wanted = new List<DecalKind>();
            var names = step["kinds"] is JArray ka ? ka.Select(x => (string)x).ToList() : new List<string> { (string)step["kinds"] ?? "all" };
            foreach (var n in names)
                foreach (var k in lib.Kinds.OrderBy(k => k.Family).ThenBy(k => k.Id))
                    if (k.Family.Length > 0 && (n == "all" || n == k.Id || n == k.Family) && !wanted.Contains(k)) wanted.Add(k);
            int perKind = (int?)step["variants"] ?? 3;
            float cellM = (float?)step["cell"] ?? 8f;
            int cols = (int?)step["cols"] ?? 9;
            var origin = DynAt(st, step);
            int c = 0, r = 0, placed = 0;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var layout = new JArray();
            foreach (var k in wanted)
            {
                var cells = new List<float2>();
                for (int i = 0; i < math.min(perKind, k.Variants.Count); i++)
                {
                    var v = DecalVariants.Get(k, i);
                    float pad = k.Warp * 1.3f + 0.3f;
                    bool twice = k.Variants.Tile == VariantTiling.X || k.Variants.Tile == VariantTiling.Chain;
                    var span = k.Variants.Tile == VariantTiling.None ? v.Size + 2f * pad : v.Size;
                    float len = span.x * (twice ? 2f : 1f);
                    float scale = math.min(1f, (cellM - 1f) / math.max(len, span.y));
                    var centre = origin + new float2(c * cellM, -r * cellM);
                    if (twice)
                    {
                        var off = new float2(span.x * 0.5f * scale, 0f);
                        SharedDecals.Place(DecalVariants.Id(k.Id, i), "lab", centre - off, 0f, scale);
                        // a strip repeats with itself; a chain joins the next piece
                        SharedDecals.Place(DecalVariants.Id(k.Id, k.Variants.Tile == VariantTiling.Chain ? (i + 1) % k.Variants.Count : i), "lab", centre + off, 0f, scale);
                        placed += 2;
                    }
                    else { SharedDecals.Place(DecalVariants.Id(k.Id, i), "lab", centre, 0f, scale); placed++; }
                    cells.Add(centre);
                    layout.Add(new JArray(k.Id + "#" + i, Math.Round(centre.x, 1), Math.Round(centre.y, 1), Math.Round(scale, 2)));
                    if (++c >= cols) { c = 0; r++; }
                }
                var lo = cells.Aggregate(new float2(float.MaxValue), (a, b) => math.min(a, b)) - cellM * 0.5f;
                var hi = cells.Aggregate(new float2(float.MinValue), (a, b) => math.max(a, b)) + cellM * 0.5f;
                _libraryCells[k.Id] = new[] { lo, hi };
            }
            float timeout = (float?)step["timeout"] ?? 180f;
            yield return Frames(10);
            yield return Until(() => SharedDecals.Pending == 0, timeout, "decal library");
            var fieldLo = origin + new float2(-cellM * 0.5f, -(r + 0.5f) * cellM);
            var fieldHi = origin + new float2((cols - 0.5f) * cellM, cellM * 0.5f);
            st.Ring = new[] { fieldLo, new float2(fieldHi.x, fieldLo.y), fieldHi, new float2(fieldLo.x, fieldHi.y) };
            rec["kinds"] = wanted.Count; rec["placed"] = placed; rec["instances"] = SharedDecals.InstanceCount;
            rec["variants"] = SharedDecals.VariantCount; rec["fromDisk"] = SharedDecals.FromDisk;
            rec["gpuMiB"] = Math.Round(SharedDecals.GpuBytes / 1048576.0, 1); rec["makeMs"] = Math.Round(SharedDecals.MakeMs);
            rec["waitedMs"] = Math.Round(clock.Elapsed.TotalMilliseconds); rec["pending"] = SharedDecals.Pending;
            rec["layout"] = layout;
            Mod.Log.Info($"Placemaker lab: library: {wanted.Count} kinds, {placed} instances of {SharedDecals.VariantCount} variants ({SharedDecals.FromDisk} from the disk cache), " +
                $"{SharedDecals.GpuBytes / 1048576.0:F1} MiB on the GPU, {SharedDecals.MakeMs / 1000.0:F1} s of worker time, drawn after {clock.Elapsed.TotalSeconds:F1} s; layout {layout.ToString(Newtonsoft.Json.Formatting.None)}");
            Event("build");
        }
    }
}
