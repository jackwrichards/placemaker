// Placemaker — declutter: make planned objects fit with their REAL footprints (GPL-3.0).
//
// FEEDBACK-4: a memorial park ghost logged "553 parts, 480 optional objects dropped (collisions)".
// Generators space objects with guessed radii; the game tests the prefab's collision box
// (ObjectGeometryData.m_Bounds = AssetInfo.BoundsMin/Max) and rejects every overlap (rules in
// docs/RESEARCH-NOTES-ADDENDUM.md §"Object collisions"): in the ghost BOTH optional objects of an
// overlapping pair are cancelled, in a real build one of them is hidden ("Overridden") after Apply.
//
// This pass runs once per plan right after automatic assets are resolved (AssetCatalogSystem.
// ResolveAutomatic — the single resolver the tool, the ghost and the build all call), so preview,
// price and build see the same objects. Pure and deterministic:
//   1. Rows (hedge/fence pieces of one prefab and yaw on one line) that overlap are re-spaced
//      evenly over the same span with as many pieces as fit without touching.
//   2. Objects are accepted greedily by role priority (centerpieces > furniture > trees > bushes;
//      plan order within a tier). Decals take no space (s21): they never take part, are never nudged
//      or dropped and never cause a "didn't fit" issue; the build sinks them so the game does not
//      collide them either (BuildService.EmitObjectDefinition, docs/DECALS.md). An object that overlaps an accepted object or a planned
//      net (real width) is nudged (small radial search, ≤ ~1/2 its size, max 2 m) or dropped.
//   3. Drops are reported per role as PlanIssues ("12 bushes didn't fit") — never silent.
// Legal overlaps the game allows are honoured: markers/net-attached objects are ignored, objects
// whose collision boxes do not meet in height pass, an object never collides with the net it is
// attached to, and surfaces never block objects (they are not physical areas). Plants are already
// small: the catalog's plant bounds are the game's shrunk collision core, so bushes under a tree
// canopy are fine as long as the cores do not touch.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Build
{
    public static class Declutter
    {
        /// <summary>(s55) Setting "Ignore collisions in my builds" (pushed by the settings): lots with stall markings only drop exact duplicates.</summary>
        public static bool Anarchy;

        /// <summary>Two objects on (almost) the same spot: centres closer than 0.4 m or a third of the smaller box.</summary>
        public static bool NearDuplicate(in Footprint a, in Footprint b)
            => math.distance(a.Center, b.Center) < math.max(0.4f, 0.35f * math.min(math.cmin(a.Half), math.cmin(b.Half)));

        /// <summary>Clearance between two collision boxes (the game counts touching as overlapping).</summary>
        public const float Gap = 0.05f;
        /// <summary>Largest nudge distance (m).</summary>
        public const float MaxNudge = 2f;
        private const float CellSize = 4f;

        /// <summary>Oriented collision rectangle of one object (world XZ) plus its local height range.</summary>
        public struct Footprint
        {
            public float2 Center, AxisX, AxisZ, Half;
            public float MinY, MaxY;
            public float Radius => math.length(Half);
        }

        public sealed class Report
        {
            public readonly Dictionary<ObjectRole, int> Dropped = new Dictionary<ObjectRole, int>();
            public readonly Dictionary<ObjectRole, int> Moved = new Dictionary<ObjectRole, int>();
            public readonly Dictionary<ObjectRole, float3> FirstDrop = new Dictionary<ObjectRole, float3>();
            /// <summary>Objects with a known footprint that were checked.</summary>
            public int Checked;
            public int TotalDropped, TotalMoved, Respaced;
            public bool Skipped;

            internal void Drop(ObjectRole r, float3 at)
            {
                Dropped[r] = (Dropped.TryGetValue(r, out var n) ? n : 0) + 1;
                if (!FirstDrop.ContainsKey(r)) FirstDrop[r] = at;
                TotalDropped++;
            }

            internal void Move(ObjectRole r)
            {
                Moved[r] = (Moved.TryGetValue(r, out var n) ? n : 0) + 1;
                TotalMoved++;
            }

            public override string ToString()
            {
                var parts = new List<string>();
                foreach (var kv in Dropped) parts.Add(kv.Key + " -" + kv.Value);
                return $"checked {Checked}, moved {TotalMoved}, dropped {TotalDropped}, respaced {Respaced}" + (parts.Count > 0 ? " (" + string.Join(", ", parts) + ")" : "");
            }
        }

        /// <summary>Issue key per role: "Placemaker.Issue.Declutter.Bush" etc. (arg 0 = count).</summary>
        public static string IssueKey(ObjectRole role) => "Placemaker.Issue.Declutter." + role;

        /// <summary>Roles that take part (everything else — markers, caps, spawners — never collides or is net-owned).</summary>
        public static bool Participates(ObjectRole r) => Tier(r) >= 0;

        /// <summary>0 = centerpiece … 3 = bushes; 5 = fixed obstacle (parking pieces), -1 = ignored (markers, decals).</summary>
        public static int Tier(ObjectRole r)
        {
            switch (r)
            {
                case ObjectRole.Centerpiece: return 0;
                case ObjectRole.Bench: case ObjectRole.Lamp: case ObjectRole.Bin: case ObjectRole.Furniture: case ObjectRole.FencePiece: return 1;
                case ObjectRole.Tree: return 2;
                case ObjectRole.Bush: case ObjectRole.Planter: return 3;
                case ObjectRole.Decal: return -1;   // s21: decals take no space
                case ObjectRole.ParkingBay: case ObjectRole.Charger: case ObjectRole.Arrow: return 5;
                default: return -1;
            }
        }

        private static bool IsFixed(ObjectRole r) => Tier(r) == 5;

        /// <summary>Decorative drops are informational; losing furniture or a centerpiece is a warning.</summary>
        public static IssueSeverity SeverityFor(ObjectRole r) => Tier(r) >= 2 ? IssueSeverity.Info : IssueSeverity.Warning;

        /// <summary>Footprint of an object from its asset bounds; false when the asset (or its size) is unknown.</summary>
        public static bool TryFootprint(PlannedObject o, IReadOnlyDictionary<string, AssetInfo> assets, out Footprint f)
        {
            f = default;
            if (o == null || assets == null || string.IsNullOrEmpty(o.Asset.Id) || !assets.TryGetValue(o.Asset.Id, out var info)) return false;
            if (!TryFootprint(o.Position, o.Yaw, o.Scale, info.BoundsMin, info.BoundsMax, out f)) return false;
            // (Industrial) a prop stacked on another stands higher: their boxes meet in plan, not in height.
            if (math.abs(math.abs(o.Roll) - math.PI) < 0.1f) { float lo = o.Elevation - f.MaxY, hi = o.Elevation - f.MinY; f.MinY = lo; f.MaxY = hi; }
            else if (o.Elevation != 0f) { f.MinY += o.Elevation; f.MaxY += o.Elevation; }   // raised, or sunk into the ground (negative)
            return true;
        }

        public static bool TryFootprint(float3 position, float yaw, float scale, float3 min, float3 max, out Footprint f)
        {
            f = default;
            if (!(max.x > min.x) || !(max.z > min.z) || !math.all(math.isfinite(position))) return false;
            float s = math.isfinite(scale) && scale > 0.05f ? scale : 1f;
            math.sincos(math.isfinite(yaw) ? yaw : 0f, out float sin, out float cos);
            // Unity quaternion.RotateY(yaw): local +X → (cos, −sin), local +Z → (sin, cos).
            f.AxisX = new float2(cos, -sin);
            f.AxisZ = new float2(sin, cos);
            var c = (min + max) * 0.5f * s;
            f.Half = (max - min).xz * 0.5f * s;
            f.Center = position.xz + f.AxisX * c.x + f.AxisZ * c.z;
            f.MinY = math.min(min.y, max.y) * s;
            f.MaxY = math.max(min.y, max.y) * s;
            return true;
        }

        /// <summary>True when two collision boxes overlap (within <paramref name="gap"/>) in XZ and in height.</summary>
        public static bool Overlaps(in Footprint a, in Footprint b, float gap = Gap)
        {
            if (a.MaxY < b.MinY || b.MaxY < a.MinY) return false;   // e.g. a hanging object above a bench
            float2 d = b.Center - a.Center;
            float r = a.Radius + b.Radius + gap;
            if (math.lengthsq(d) > r * r) return false;
            // SAT on the four axes.
            return !Separated(a.AxisX, d, a, b, gap) && !Separated(a.AxisZ, d, a, b, gap)
                && !Separated(b.AxisX, d, a, b, gap) && !Separated(b.AxisZ, d, a, b, gap);
        }

        private static bool Separated(float2 axis, float2 d, in Footprint a, in Footprint b, float gap)
        {
            float ra = a.Half.x * math.abs(math.dot(a.AxisX, axis)) + a.Half.y * math.abs(math.dot(a.AxisZ, axis));
            float rb = b.Half.x * math.abs(math.dot(b.AxisX, axis)) + b.Half.y * math.abs(math.dot(b.AxisZ, axis));
            return math.abs(math.dot(d, axis)) > ra + rb + gap;
        }

        /// <summary>Distance between a segment and a footprint rectangle (0 when they intersect).</summary>
        public static float Distance(in Footprint f, float2 p0, float2 p1)
        {
            // Into the rectangle's frame: an axis-aligned box [−Half, Half].
            float2 a = Local(f, p0), b = Local(f, p1);
            if (SegmentHitsBox(a, b, f.Half)) return 0f;
            float best = math.min(BoxDistance(a, f.Half), BoxDistance(b, f.Half));
            var h = f.Half;
            best = math.min(best, PointSegment(new float2(-h.x, -h.y), a, b));
            best = math.min(best, PointSegment(new float2(h.x, -h.y), a, b));
            best = math.min(best, PointSegment(new float2(h.x, h.y), a, b));
            best = math.min(best, PointSegment(new float2(-h.x, h.y), a, b));
            return best;
        }

        private static float2 Local(in Footprint f, float2 p)
        {
            var d = p - f.Center;
            return new float2(math.dot(d, f.AxisX), math.dot(d, f.AxisZ));
        }

        private static float BoxDistance(float2 p, float2 h) => math.length(math.max(math.abs(p) - h, 0f));

        private static float PointSegment(float2 p, float2 a, float2 b)
        {
            var ab = b - a;
            float l2 = math.lengthsq(ab);
            float t = l2 > 1e-9f ? math.saturate(math.dot(p - a, ab) / l2) : 0f;
            return math.distance(p, a + ab * t);
        }

        private static bool SegmentHitsBox(float2 a, float2 b, float2 h)
        {
            // Liang–Barsky clip against the box.
            float t0 = 0f, t1 = 1f;
            var d = b - a;
            for (int axis = 0; axis < 2; axis++)
            {
                float p = axis == 0 ? d.x : d.y, q0 = axis == 0 ? a.x : a.y, lim = axis == 0 ? h.x : h.y;
                if (math.abs(p) < 1e-9f) { if (q0 < -lim || q0 > lim) return false; continue; }
                float ta = (-lim - q0) / p, tb = (lim - q0) / p;
                if (ta > tb) { var t = ta; ta = tb; tb = t; }
                t0 = math.max(t0, ta); t1 = math.min(t1, tb);
                if (t0 > t1) return false;
            }
            return true;
        }

        // ───────────────────────── the pass ─────────────────────────

        private struct NetSeg { public float2 A, B; public float Half; public int Layer, Net; }

        private sealed class Item
        {
            public int Layer, Index, Tier;
            public PlannedObject Obj;
            public Footprint Fp;
            public bool HasFp, Drop, Row;
        }

        /// <summary>
        /// Runs once per plan (ComposedPlan.Decluttered): removes or nudges objects the game would reject and adds
        /// one PlanIssue per role with drops to <see cref="ComposedPlan.Issues"/>. Assets must already be resolved.
        /// </summary>
        public static Report Run(ComposedPlan plan, IReadOnlyDictionary<string, AssetInfo> assets)
        {
            bool first = plan != null && !plan.Decluttered;
            var report = RunInner(plan, assets);
            // (s15) Nudged objects, and objects whose real footprint is only known now, must still keep
            // off existing buildings (SiteSafety).
            if (first && assets != null && plan.Protected != null && plan.Protected.Count > 0)
            {
                int dropped = SiteSafety.FilterObjects(plan, assets);
                if (dropped > 0)
                    plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Info, Key = SiteSafety.IssueLeftOut, Args = new object[] { dropped } });
            }
            return report;
        }

        private static Report RunInner(ComposedPlan plan, IReadOnlyDictionary<string, AssetInfo> assets)
        {
            var report = new Report();
            if (plan?.Layers == null || assets == null || plan.Decluttered) { report.Skipped = true; return report; }
            plan.Decluttered = true;

            // Objects.
            var items = new List<Item>();
            for (int li = 0; li < plan.Layers.Count; li++)
            {
                var l = plan.Layers[li];
                if (l?.Objects == null) continue;
                for (int oi = 0; oi < l.Objects.Count; oi++)
                {
                    var o = l.Objects[oi];
                    if (o == null || !Participates(o.Role)) continue;
                    // (Industrial) a prop set into the ground carries the game's negative Elevation: its collision mask is then
                    // Underground only and it meets nothing (seen in the game runner: a car on sunk rubble, both drawn).
                    if (o.Elevation < -0.01f) continue;
                    var it = new Item { Layer = li, Index = oi, Obj = o, Tier = Tier(o.Role) };
                    it.HasFp = TryFootprint(o, assets, out it.Fp);
                    if (it.HasFp) items.Add(it);
                }
            }
            report.Checked = items.Count;
            if (items.Count == 0) return report;

            // Nets (real width; fence nets of prop pieces: the piece thickness).
            var segs = new List<NetSeg>();
            for (int li = 0; li < plan.Layers.Count; li++)
            {
                var l = plan.Layers[li];
                if (l?.Nets == null) continue;
                for (int ni = 0; ni < l.Nets.Count; ni++)
                {
                    var n = l.Nets[ni];
                    if (n == null || n.Role == NetRole.DriveAisle || n.Role == NetRole.DriveConnector) continue;   // invisible lanes
                    float half = NetHalfWidth(n, assets);
                    if (!(half > 0f)) continue;
                    AddSegments(segs, n, half, li, ni);
                }
            }
            // (s59) Existing city nets (captured roads/paths with their sidewalks, ComposedPlan.ExistingNets): the game rejects a
            // prop that pokes into a street's sidewalk exactly like one on our own path. Planning them out here keeps the build
            // free of those collisions (quality — the build no longer refuses anything for them under anarchy).
            if (plan.ExistingNets != null)
                foreach (var en in plan.ExistingNets)
                {
                    float half = en.Width * 0.5f;
                    if (!(half > 0f)) continue;
                    AddSegments(segs, en.A, en.B, en.C, en.D, half, -1, -1);
                }
            var netGrid = new Dictionary<long, List<int>>();
            for (int i = 0; i < segs.Count; i++)
            {
                var s = segs[i];
                Insert(netGrid, math.min(s.A, s.B) - s.Half, math.max(s.A, s.B) + s.Half, i);
            }

            report.Respaced = RespaceRows(items, report);

            // Greedy acceptance by tier, then plan order.
            var order = new List<Item>(items);
            order.Sort((a, b) =>
            {
                int ta = IsFixed(a.Obj.Role) ? -1 : a.Tier, tb = IsFixed(b.Obj.Role) ? -1 : b.Tier;
                if (ta != tb) return ta.CompareTo(tb);
                if (a.Layer != b.Layer) return a.Layer.CompareTo(b.Layer);
                return a.Index.CompareTo(b.Index);
            });
            var accepted = new List<Footprint>();
            var grid = new Dictionary<long, List<int>>();
            var stamp = new Dictionary<int, int>();
            int query = 0;

            // (s55) "Ignore collisions in my builds" on a lot with stall markings: the build gives every prop the parking carrier as owner,
            // so the game never pairs our own pieces. Then only a near-exact duplicate (same spot) is a reason to drop an object; the
            // overlaps with stall markings and neighbouring props that dropped 85 bushes, lamps and trees (playtest 89) are fine.
            bool relax = Anarchy;   // s58: every plan (every build shares one group owner now)

            bool Free(in Footprint f, Item it)
            {
                query++;
                foreach (var idx in Near(grid, f))
                {
                    if (stamp.TryGetValue(idx, out var q) && q == query) continue;
                    stamp[idx] = query;
                    if (Overlaps(f, accepted[idx]) && (!relax || NearDuplicate(f, accepted[idx]))) return false;
                }
                if (IsFixed(it.Obj.Role)) return true;   // parking pieces are laid out on their own nets
                foreach (var si in Near(netGrid, f))
                {
                    var s = segs[si];
                    if (s.Layer == it.Layer && s.Net == it.Obj.AttachedNet) continue;   // its own net
                    if (Distance(f, s.A, s.B) < s.Half + Gap) return false;
                }
                return true;
            }

            void Accept(in Footprint f)
            {
                accepted.Add(f);
                Insert(grid, f.Center - f.Radius, f.Center + f.Radius, accepted.Count - 1);
            }

            foreach (var it in order)
            {
                if (it.Drop) continue;
                if (IsFixed(it.Obj.Role) || Free(it.Fp, it)) { Accept(it.Fp); continue; }
                if (!it.Row && TryNudge(it, Free, out var moved))
                {
                    var delta = moved.Center - it.Fp.Center;
                    it.Obj.Position = new float3(it.Obj.Position.x + delta.x, it.Obj.Position.y, it.Obj.Position.z + delta.y);
                    it.Fp = moved;
                    Accept(moved);
                    report.Move(it.Obj.Role);
                    continue;
                }
                it.Drop = true;
            }

            // Remove dropped objects (indices descending per layer) and report.
            var drops = new List<Item>();
            foreach (var it in items) if (it.Drop) drops.Add(it);
            drops.Sort((a, b) => a.Layer != b.Layer ? a.Layer.CompareTo(b.Layer) : b.Index.CompareTo(a.Index));
            foreach (var it in drops)
            {
                report.Drop(it.Obj.Role, it.Obj.Position);
                plan.Layers[it.Layer].Objects.RemoveAt(it.Index);
            }
            var roles = new List<ObjectRole>(report.Dropped.Keys);
            roles.Sort((a, b) => Tier(a) != Tier(b) ? Tier(a).CompareTo(Tier(b)) : a.CompareTo(b));
            foreach (var r in roles)
                plan.Issues.Add(new PlanIssue
                {
                    Severity = SeverityFor(r),
                    Key = IssueKey(r),
                    Args = new object[] { report.Dropped[r] },
                    At = report.FirstDrop[r],
                });
            return report;
        }

        /// <summary>Half width of a planned net as the game will build it.</summary>
        public static float NetHalfWidth(PlannedNet n, IReadOnlyDictionary<string, AssetInfo> assets)
        {
            if (n != null && !string.IsNullOrEmpty(n.Asset.Id) && assets != null && assets.TryGetValue(n.Asset.Id, out var info))
            {
                if (info.NetWidth > 0f) return info.NetWidth * 0.5f;
                if (n.Role == NetRole.Fence && info.BoundsMax.x > info.BoundsMin.x && info.BoundsMax.z > info.BoundsMin.z)
                    return math.min(info.BoundsMax.x - info.BoundsMin.x, info.BoundsMax.z - info.BoundsMin.z) * 0.5f;   // prop pieces
            }
            return n == null ? 0f : Composer.DefaultHalfWidth(n.Role);
        }

        private static void AddSegments(List<NetSeg> segs, PlannedNet n, float half, int layer, int net)
            => AddSegments(segs, n.A, n.B, n.C, n.D, half, layer, net);

        private static void AddSegments(List<NetSeg> segs, float3 a, float3 b, float3 c, float3 d, float half, int layer, int net)
        {
            var n = (A: a, B: b, C: c, D: d);
            float approx = math.distance(n.A.xz, n.B.xz) + math.distance(n.B.xz, n.C.xz) + math.distance(n.C.xz, n.D.xz);
            if (!math.isfinite(approx)) return;
            int count = math.clamp((int)math.ceil(approx / 4f), 1, 64);
            float2 prev = n.A.xz;
            for (int i = 1; i <= count; i++)
            {
                float t = i / (float)count, u = 1f - t;
                float3 p = u * u * u * n.A + 3f * u * u * t * n.B + 3f * u * t * t * n.C + t * t * t * n.D;
                segs.Add(new NetSeg { A = prev, B = p.xz, Half = half, Layer = layer, Net = net });
                prev = p.xz;
            }
        }

        private static readonly float[] NudgeSteps = { 0.25f, 0.5f, 0.75f, 1f, 1.5f, 2f };

        private static bool TryNudge(Item it, FreeTest free, out Footprint moved)
        {
            moved = it.Fp;
            float limit = math.min(MaxNudge, math.max(0.5f, it.Fp.Radius));
            uint seed = Seeds.Mix(it.Obj.Variation, (uint)(it.Layer * 7919 + it.Index));
            float start = (seed % 360u) * (math.PI / 180f);
            foreach (var r in NudgeSteps)
            {
                if (r > limit + 1e-4f) break;
                int dirs = r < 1f ? 8 : 12;
                for (int k = 0; k < dirs; k++)
                {
                    float a = start + k * (2f * math.PI / dirs);
                    var f = it.Fp;
                    f.Center += new float2(math.cos(a), math.sin(a)) * r;
                    if (free(f, it)) { moved = f; return true; }
                }
            }
            return false;
        }

        private delegate bool FreeTest(in Footprint f, Item it);

        // ───────────────────────── rows ─────────────────────────

        /// <summary>Hedge/fence pieces of one prefab laid end to end: overlapping runs are re-spaced (some pieces dropped).</summary>
        private static int RespaceRows(List<Item> items, Report report)
        {
            var groups = new Dictionary<string, List<Item>>(StringComparer.Ordinal);
            foreach (var it in items)
            {
                if (!IsRowPiece(it)) continue;
                var f = it.Fp;
                bool longX = f.Half.x >= f.Half.y;
                float2 axis = longX ? f.AxisX : f.AxisZ;
                if (axis.x < -1e-4f || (math.abs(axis.x) <= 1e-4f && axis.y < 0f)) axis = -axis;   // undirected
                int ang = (int)math.round(math.degrees(math.atan2(axis.y, axis.x)) * 2f);          // 0.5° buckets
                string key = it.Layer + "|" + it.Obj.Asset.Id + "|" + ang;
                if (!groups.TryGetValue(key, out var g)) groups[key] = g = new List<Item>();
                g.Add(it);
            }
            int changed = 0;
            foreach (var g in groups.Values)
            {
                if (g.Count < 2) continue;
                var f0 = g[0].Fp;
                bool longX = f0.Half.x >= f0.Half.y;
                float2 u = longX ? f0.AxisX : f0.AxisZ, v = new float2(-u.y, u.x);
                float len = 2f * math.max(f0.Half.x, f0.Half.y), thick = 2f * math.min(f0.Half.x, f0.Half.y);
                // Lines: same perpendicular offset (within half the thickness).
                g.Sort((a, b) => math.dot(a.Fp.Center, v).CompareTo(math.dot(b.Fp.Center, v)));
                int i = 0;
                while (i < g.Count)
                {
                    float t0 = math.dot(g[i].Fp.Center, v);
                    int j = i + 1;
                    while (j < g.Count && math.dot(g[j].Fp.Center, v) - t0 <= thick * 0.5f) j++;
                    changed += RespaceLine(g.GetRange(i, j - i), u, len, report);
                    i = j;
                }
            }
            return changed;
        }

        private static bool IsRowPiece(Item it)
        {
            if (it.Obj.Role == ObjectRole.FencePiece) return true;
            string cat = RoleCategories.ForObject(it.Obj);
            return cat == AssetCategories.Hedge || cat == AssetCategories.Fence;
        }

        private static int RespaceLine(List<Item> line, float2 u, float len, Report report)
        {
            foreach (var it in line) it.Row = true;
            if (line.Count < 2) return 0;
            line.Sort((a, b) => math.dot(a.Fp.Center, u).CompareTo(math.dot(b.Fp.Center, u)));
            int changed = 0, start = 0;
            // Runs: consecutive pieces closer than 1.5 lengths belong to one run.
            for (int k = 1; k <= line.Count; k++)
            {
                bool split = k == line.Count || math.dot(line[k].Fp.Center - line[k - 1].Fp.Center, u) > len * 1.5f;
                if (!split) continue;
                changed += RespaceRun(line.GetRange(start, k - start), u, len);
                start = k;
            }
            return changed;
        }

        private static int RespaceRun(List<Item> run, float2 u, float len)
        {
            int n = run.Count;
            if (n < 2) return 0;
            bool overlap = false;
            for (int k = 1; k < n; k++)
                if (math.dot(run[k].Fp.Center - run[k - 1].Fp.Center, u) < len + Gap) { overlap = true; break; }
            if (!overlap) return 0;
            float s0 = math.dot(run[0].Fp.Center, u), s1 = math.dot(run[n - 1].Fp.Center, u);
            float span = s1 - s0 + len;
            int m = math.min(n, math.max(1, (int)math.floor((span + Gap) / (len + Gap) + 1e-4f)));
            for (int k = 0; k < n; k++)
            {
                var it = run[k];
                if (k >= m) { it.Drop = true; continue; }
                float target = m == 1 ? (s0 + s1) * 0.5f : s0 + k * (s1 - s0) / (m - 1);
                float shift = target - math.dot(it.Fp.Center, u);
                if (math.abs(shift) < 1e-5f) continue;
                var d = u * shift;
                it.Fp.Center += d;
                it.Obj.Position = new float3(it.Obj.Position.x + d.x, it.Obj.Position.y, it.Obj.Position.z + d.y);
            }
            return n;
        }

        // ───────────────────────── grid ─────────────────────────

        private static long Cell(int x, int y) => ((long)x << 32) ^ (uint)y;

        private static void Insert(Dictionary<long, List<int>> grid, float2 min, float2 max, int value)
        {
            int x0 = (int)math.floor(min.x / CellSize), x1 = (int)math.floor(max.x / CellSize);
            int y0 = (int)math.floor(min.y / CellSize), y1 = (int)math.floor(max.y / CellSize);
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                {
                    long c = Cell(x, y);
                    if (!grid.TryGetValue(c, out var l)) grid[c] = l = new List<int>();
                    l.Add(value);
                }
        }

        private static IEnumerable<int> Near(Dictionary<long, List<int>> grid, Footprint f)
        {
            float r = f.Radius + Gap;
            int x0 = (int)math.floor((f.Center.x - r) / CellSize), x1 = (int)math.floor((f.Center.x + r) / CellSize);
            int y0 = (int)math.floor((f.Center.y - r) / CellSize), y1 = (int)math.floor((f.Center.y + r) / CellSize);
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                    if (grid.TryGetValue(Cell(x, y), out var l))
                        foreach (var v in l) yield return v;
        }
    }
}
