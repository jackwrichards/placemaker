// Placemaker — decal dressing: the complement pass (s21, docs/DECALS.md). GPL-3.0.
//
// "Decals are not a thing you sprinkle around. They complement every other feature." After the whole
// recipe is composed (and before zoning cutouts and building lots are enforced), this pass looks at
// what was planned and adds the wear and litter that thing would collect:
//   Trees    leaf litter under the crown, mulch/needle beds under conifers and near trunks
//   Paths    leaves drifted along the edges near trees, worn verges, cracks, moss in the shade,
//            corner-cutting wear at junctions, scuffed entrances
//   Paved    stains, cracks and wet patches over plazas; scuffs in front of benches, litter by bins,
//            splash rings round fountains, stains round kiosks and stalls
//   Parking  oil blots in the bays (along the bay, where the engine stands), tyre marks at stall mouths
//            and aisle turns and entrances, repaired patches, glass at the back of edge rows (gritty)
//   Play     worn dirt under play items, under picnic tables and in front of benches; litter by bins
//   Sports   worn goalmouths, scuffs under hoops and fitness stations
//   Ground   leaves and worn patches in lawns, mud and grit on dirt, stones on natural ground
//   Shore    wet sand and wrack lines along the waterline (along the shore), shells, footprints
//   Works    tyre tracks along every drive line (both wheel lines), mud at gates and in ruts, oil under
//            machinery, dust round heaps
//   Ruins    cracks and patches over slabs, rubble grit round heaps, moss at wall feet, graffiti, glass
//   Garden   mulch under shrubs and beds, leaves drifted under hedges
// Decals take NO space: nothing here asks ReservedSpace or footprints. They may lie under objects, on
// paths and over each other within reason (same-kind decals keep part of their size apart), never on
// water, outside the outline, on building lots (SiteSafety) or zoning cutouts (Cutouts) — those run
// after this pass. Amounts: Decals (master) × the family's amount × the style's weight for that kind.
// Master 0 also removes every decorative decal the layers placed themselves; functional decals
// (markings, stalls, field lines, paving patterns) are never touched. Deterministic: every decision is a
// hash of the plan seed and the host's position.

using System;
using System.Collections.Generic;
using System.Threading;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Decals
{
    public static class Dressing
    {
        /// <summary>Decal budget per hectare at Decals = 100 % (scales with the amount). Decals have their own budget, never the object cap.</summary>
        public const float BudgetPerHa = 1800f;
        public const int MinBudget = 300;
        public const string StatKey = "Placemaker.Stat.Decals";

        public sealed class Report
        {
            public int Added, Removed;
            /// <summary>(s28) Terrain-paint ground wear added by this pass.</summary>
            public GroundWear.Report Paint;
            public readonly Dictionary<string, int> ByPool = new Dictionary<string, int>(StringComparer.Ordinal);
            public double Ms;
        }

        /// <summary>Kinds the style weights (Clean / Lived-in / Gritty).</summary>
        internal enum Kind { Leaves, Wear, Crack, Stain, Oil, Litter, Glass, Graffiti, Puddle, Moss, Mud, Wrack, Rocks, Tyre }

        private static readonly float[,] StyleWeights =
        {
            //            Leaves Wear  Crack Stain Oil   Litter Glass Graff Puddle Moss  Mud   Wrack Rocks Tyre
            /* clean   */ { 0.75f, 0.4f, 0.12f, 0.3f, 0.35f, 0f,  0f,    0f,   0.25f, 0.35f, 0.45f, 0.6f, 0.6f, 0.4f },
            /* livedIn */ { 1f,    1f,   0.7f,  1f,   1f,    0.3f, 0.08f, 0f,   0.6f, 0.8f,  1f,   1f,   1f,   1f },
            /* gritty  */ { 1.2f,  1.4f, 1.8f,  1.6f, 1.6f,  1.3f, 1f,    1f,   1.1f,  1.4f,  1.5f, 1.4f, 1.3f, 1.5f },
        };

        internal static float Weight(DressStyle s, Kind k) => StyleWeights[(int)s, (int)k];

        /// <summary>
        /// (s23, user: "poop all over the floor … you shouldn't consciously notice any single decal"). Clean and Lived-in dress
        /// only with what genuinely blends: faint leaf litter under crowns, faint dirt dust where feet go, sand dust at sand
        /// edges, wet sand at the waterline, faint oil in parking bays and faint stains by benches and bins. Cracks, repairs,
        /// tyre marks, mud, puddles, moss, mulch, stones, litter, glass, wrack and graffiti are Gritty only (still placeable
        /// by hand with the Decals feature).
        /// </summary>
        public static bool Allowed(string pool, DressStyle style)
        {
            if (style == DressStyle.Gritty) return pool != DecalPools.Footprints;
            return pool == AssetCategories.DecalLeaves || pool == DecalPools.Wear || pool == DecalPools.Sand || pool == DecalPools.WetSand
                || pool == DecalPools.Oil || pool == DecalPools.Stain;
        }

        /// <summary>
        /// Runs the pass over a composed plan. (issue #2, 2026-10-19) Decals from decal packs are no longer placed: every
        /// decal of a kind the base game does not have is dropped from every layer, whoever placed it (PackDecals), and the
        /// complement rules below no longer add any. What stays is the ground wear painted into the terrain texture
        /// (GroundWear, no packs needed). <paramref name="dress"/> maps each layer plan to the dressing settings of the fill
        /// step it belongs to (features share their parent's instance); layers without settings get no ground wear.
        /// </summary>
        public static Report Apply(ComposedPlan plan, AreaShape area, SiteContext site, IReadOnlyDictionary<AreaPlan, DressSettings> dress, CancellationToken cancel = default)
        {
            var report = new Report();
            if (plan?.Layers == null) return report;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var l in plan.Layers) report.Removed += PackDecals.Drop(l);
            if (dress == null || dress.Count == 0) { report.Ms = sw.Elapsed.TotalMilliseconds; return report; }
            foreach (var s0 in dress.Values) if (s0 != null) s0.MainTaken = false;

            var world = new DressingWorld(plan, area, site);
            if (world.Root == null) return report;
            // s28: ground wear painted into the terrain texture (needs no decal packs).
            report.Paint = GroundWear.Apply(plan, world, site, dress);
            report.Ms = sw.Elapsed.TotalMilliseconds;
            return report;
        }

        /// <summary>Decorative decals a layer placed itself, thinned to <paramref name="keep"/> (0 = all removed). Even stride, deterministic.</summary>
        public static int ThinLayerDecals(AreaPlan l, float keep)
        {
            if (l?.Objects == null) return 0;
            keep = math.saturate(keep);
            float acc = 0f;
            return l.Objects.RemoveAll(o =>
            {
                if (o == null || o.Role != ObjectRole.Decal || DecalCatalog.IsFunctionalCategory(o.Category)) return false;
                if (keep <= 0f) return true;
                acc += keep;
                if (acc >= 1f) { acc -= 1f; return false; }
                return true;
            });
        }

        private static float Height(AreaPlan l, SiteContext site, float2 p)
        {
            try
            {
                if (l.HeightAfter != null) return l.HeightAfter(p);
                return site?.HeightAt != null ? site.HeightAt(p) : 0f;
            }
            catch (Exception) { return 0f; }
        }

        // ───────────────────────── acceptance ─────────────────────────

        internal struct Candidate
        {
            public float2 P;
            public float Yaw;
            public string Pool;
            public float Priority;
            public uint Hash;
            public int Layer;
            /// <summary>Ground the decal must NOT end on (bit per GroundKind), beyond water which is always refused.</summary>
            public int Forbid;
        }

        private sealed class Placed
        {
            private readonly DressingWorld.Grid _grid = new DressingWorld.Grid(6f);
            private readonly List<(float2 p, string pool, float r)> _items = new List<(float2, string, float)>();

            public void Add(in Candidate c)
            {
                float r = DecalPools.Typical(c.Pool) * 0.5f;
                _grid.Add(c.P, r, _items.Count);
                _items.Add((c.P, c.Pool, r));
            }

            /// <summary>Same-kind decals keep ~half their size apart; any two never share a centre.</summary>
            public bool Crowded(float2 p, string pool)
            {
                float r = DecalPools.Typical(pool) * 0.5f;
                foreach (int i in _grid.Near(p, r))
                {
                    var it = _items[i];
                    float d = math.distance(it.p, p);
                    float min = it.pool == pool ? (it.r + r) * 0.5f : math.min(it.r, r) * 0.35f;
                    if (d < min) return true;
                }
                return false;
            }
        }

        private static bool Accept(DressingWorld w, Placed placed, in Candidate c)
        {
            if (!math.all(math.isfinite(c.P)) || !math.isfinite(c.Yaw)) return false;
            // Centre well inside the outline: half a typical decal or 1.5 m, whichever is smaller.
            float inset = math.min(1.5f, DecalPools.Typical(c.Pool) * 0.35f);
            if (!w.Root.Contains(c.P) || w.Root.BoundaryDistance(c.P, inset + 0.1f) < inset) return false;
            var g = w.GroundAt(c.P);
            if (g == GroundKind.Water) return false;
            if ((c.Forbid & (1 << (int)g)) != 0) return false;
            if (w.Cutouts.Count > 0 && w.NearCutout(c.P, math.min(1.5f, DecalPools.Typical(c.Pool) * 0.4f))) return false;
            if (placed.Crowded(c.P, c.Pool)) return false;
            return true;
        }
    }
}
