// Placemaker — what the Block paint brush stops at, player-configurable ("Brush rules").
//
// Each obstacle kind can be switched off, the block lock too, and the curb snap distance and edge
// smoothing are sliders. With every stop and the block lock off the brush is plain Paint (no fence,
// Paint's outline pipeline), so the old behaviour is always one click away.
// s27: the rules are shared by every paint tool: plain Paint cuts the active obstacles out of its outline
// (Cut), Draw / Roads / Block always keep off other Placemaker builds and, with DrawClip, off everything
// the polygon stops name (water is brush-only). New stop: other Placemaker builds (their owner lot + zone
// blocks). New master toggle "Paint over anything" (Anything) switches every stop and the block lock off.
// Area overhaul (2026-10-17): ONE set of stops for Points, Paint bucket and Paint area ("Stops at, keep off, whatever,
// you get it"): roads, paths, bike paths, rail and tram, water, buildings, lots, my builds and zoning (off / built on /
// zoned / the whole grid), plus the fill's Gap and Reach (Core/Geometry/AreaFill). The bucket's own walls (BucketWalls)
// and Draw's DrawClip are no longer read; they stay so old settings files and UIs still parse.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Geometry
{
    public enum BrushObstacleKind { Road = 0, Building = 1, Lot = 2, ZonedCell = 3, EmptyZoneCell = 4, PlacemakerBuild = 5, UnbuiltZonedCell = 6, Path = 7, Bike = 8, Track = 9, Water = 10 }

    /// <summary>A polygon the brush may stop at, tagged with what it is (rules decide whether it counts).</summary>
    public struct BrushObstacle
    {
        public BrushObstacleKind Kind;
        public float2[] Ring;
        public BrushObstacle(BrushObstacleKind kind, float2[] ring) { Kind = kind; Ring = ring; }
    }

    public sealed class BrushRules : IEquatable<BrushRules>
    {
        public const float DefaultSnap = 6f;
        public const float MaxSnap = 20f;
        public const int MaxSmoothing = 6;

        // s49: Zoned = zoned cells with a building on them (or under construction); vacant zoned land stays paintable
        // unless AllZoned is also on (parks over empty road frontage is what players want).
        public bool Roads = true, Buildings = true, Lots = true, Zoned = true, EmptyZones = false, Water = true;
        /// <summary>(s49) Also keep off zoned cells nobody built on yet. (Area overhaul) On by default: "it goes onto zoned areas when it shouldn't".</summary>
        public bool AllZoned = true;
        /// <summary>(Area overhaul) Footpaths, bike paths, rail and tram tracks on the ground.</summary>
        public bool Paths = true, Bikes = true, Tracks = true;
        /// <summary>(Area overhaul) Openings narrower than this (m) count as closed; narrower places are left out (0 = off).</summary>
        public float Gap = FillOptions.DefaultGap;
        /// <summary>(Area overhaul) How far (m) the Paint bucket spreads from the click.</summary>
        public float Reach = FillOptions.DefaultReach;
        /// <summary>(s27) Other Placemaker builds: their owner lot outline and zone-block lots (never the group being edited).</summary>
        public bool Builds = true;
        /// <summary>(s27) "Paint over anything": every stop and the block lock off, whatever the other toggles say.</summary>
        public bool Anything;
        /// <summary>(s27) Draw / Roads / Block: also clip the outline to free land (the active polygon stops, not just other builds).</summary>
        public bool DrawClip;
        /// <summary>Lock a stroke to the closed block it starts in (off: only the obstacles limit it).</summary>
        public bool StayInBlock = true;
        /// <summary>Gap (m) closed by pulling an edge onto the curb / an obstacle; 0 = off.</summary>
        public float Snap = DefaultSnap;
        /// <summary>Moving-average passes for the painted edge (0–6; Paint uses 6).</summary>
        public int Smoothing = BlockBrush.SmoothPasses;
        /// <summary>(s43) What the Paint bucket (Block mode) treats as a wall (curve walls trace the face, area walls cut it).</summary>
        public FillWall BucketWalls = FillWalls.Default;

        public static BrushRules Defaults() => new BrushRules();

        public BrushRules Clone() => (BrushRules)MemberwiseClone();

        /// <summary>Clamps the sliders into range (settings files can hold anything).</summary>
        public BrushRules Normalized()
        {
            var r = Clone();
            r.Snap = float.IsNaN(r.Snap) ? DefaultSnap : math.clamp(r.Snap, 0f, MaxSnap);
            r.Smoothing = math.clamp(r.Smoothing, 0, MaxSmoothing);
            r.BucketWalls &= FillWalls.All;
            r.Gap = float.IsNaN(r.Gap) ? FillOptions.DefaultGap : math.clamp(r.Gap, 0f, FillOptions.MaxGap);
            r.Reach = float.IsNaN(r.Reach) || r.Reach <= 0f ? FillOptions.DefaultReach : math.clamp(r.Reach, FillOptions.MinReach, FillOptions.MaxReach);
            return r;
        }

        public bool Stops(BrushObstacleKind k)
        {
            if (Anything) return false;
            switch (k)
            {
                case BrushObstacleKind.Road: return Roads;
                case BrushObstacleKind.Building: return Buildings;
                case BrushObstacleKind.Lot: return Lots;
                case BrushObstacleKind.ZonedCell: return Zoned;
                case BrushObstacleKind.EmptyZoneCell: return EmptyZones;
                case BrushObstacleKind.UnbuiltZonedCell: return AllZoned;
                case BrushObstacleKind.PlacemakerBuild: return Builds;
                case BrushObstacleKind.Path: return Paths;
                case BrushObstacleKind.Bike: return Bikes;
                case BrushObstacleKind.Track: return Tracks;
                case BrushObstacleKind.Water: return Water;
                default: return true;
            }
        }

        /// <summary>Nothing to stop at and no block lock: the brush is plain Paint.</summary>
        public bool AllOff => Anything || (!Roads && !Paths && !Bikes && !Tracks && !Buildings && !Builds && !Lots && !Zoned && !AllZoned && !EmptyZones && !Water && !StayInBlock);

        /// <summary>(Area overhaul) The outline tools (Points, Paint bucket) follow the stops whatever Paint area's Smart edges switch says.</summary>
        public BrushRules ForOutline()
        {
            if (!Anything) return this;
            var r = Clone();
            r.Anything = false;
            return r;
        }

        /// <summary>(Area overhaul) The fill's settings: the gap and the reach.</summary>
        public FillOptions Fill() => new FillOptions { Gap = Gap, Reach = Reach };

        /// <summary>(Area overhaul) Zoning as one choice: 0 off, 1 built on, 2 zoned (built or not), 3 the whole grid.</summary>
        public int ZoningLevel => !Zoned && !AllZoned && !EmptyZones ? 0 : EmptyZones ? 3 : AllZoned ? 2 : 1;

        public void SetZoningLevel(int level)
        {
            Zoned = level >= 1;
            AllZoned = level >= 2;
            EmptyZones = level >= 3;
        }
        /// <summary>Water counts (brush tools).</summary>
        public bool WaterOn => !Anything && Water;
        /// <summary>The block lock counts (Block paint).</summary>
        public bool LockOn => !Anything && StayInBlock;

        /// <summary>The obstacle rings the rules count (optionally only some kinds).</summary>
        public List<float2[]> Active(IEnumerable<BrushObstacle> obstacles, Func<BrushObstacleKind, bool> only = null)
        {
            var list = new List<float2[]>();
            if (obstacles == null) return list;
            foreach (var o in obstacles)
                if (o.Ring != null && o.Ring.Length >= 3 && Stops(o.Kind) && (only == null || only(o.Kind))) list.Add(o.Ring);
            return list;
        }

        /// <summary>
        /// Plain Paint / Draw: cuts the counted obstacles (grown by <paramref name="margin"/>) out of an outline; an
        /// enclosed one is opened with a 1 m corridor (CS2 areas have no holes). Returns the ring unchanged when nothing
        /// counts or touches it, null when nothing usable is left (touched > 0 then means "only obstacles here").
        /// </summary>
        public float2[] Cut(float2[] ring, IEnumerable<BrushObstacle> obstacles, out int touched, float margin = 0.5f,
            float minArea = 100f, Func<BrushObstacleKind, bool> only = null)
        {
            touched = 0;
            if (ring == null || ring.Length < 3) return ring;
            var active = Active(obstacles, only);
            if (active.Count == 0) return ring;
            return ObstacleCut.Subtract(ring, ObstacleCut.Grow(active, margin), out touched, minArea);
        }

        public bool Equals(BrushRules o) => o != null && Roads == o.Roads && Buildings == o.Buildings && Lots == o.Lots && Zoned == o.Zoned && AllZoned == o.AllZoned
            && EmptyZones == o.EmptyZones && Water == o.Water && StayInBlock == o.StayInBlock && Builds == o.Builds && Anything == o.Anything && DrawClip == o.DrawClip && Snap == o.Snap && Smoothing == o.Smoothing && BucketWalls == o.BucketWalls
            && Paths == o.Paths && Bikes == o.Bikes && Tracks == o.Tracks && Gap == o.Gap && Reach == o.Reach;

        /// <summary>Same walls (what stops, not the brush's own sliders): a fill or a cut made with the other rules still holds.</summary>
        public bool SameWalls(BrushRules o) => o != null && Roads == o.Roads && Paths == o.Paths && Bikes == o.Bikes && Tracks == o.Tracks && Buildings == o.Buildings
            && Lots == o.Lots && Zoned == o.Zoned && AllZoned == o.AllZoned && EmptyZones == o.EmptyZones && Water == o.Water && Builds == o.Builds && Anything == o.Anything;
        public override bool Equals(object obj) => Equals(obj as BrushRules);
        public override int GetHashCode() => (Roads ? 1 : 0) | (Buildings ? 2 : 0) | (Lots ? 4 : 0) | (Zoned ? 8 : 0) | (AllZoned ? 8192 : 0) | (EmptyZones ? 16 : 0)
            | (Water ? 32 : 0) | (StayInBlock ? 64 : 0) | (Builds ? 1024 : 0) | (Anything ? 2048 : 0) | (DrawClip ? 4096 : 0) | (Smoothing << 7) ^ Snap.GetHashCode() ^ ((int)BucketWalls << 16)
            ^ (Paths ? 1 << 24 : 0) ^ (Bikes ? 1 << 25 : 0) ^ (Tracks ? 1 << 26 : 0) ^ Gap.GetHashCode() * 31 ^ Reach.GetHashCode() * 17;

        /// <summary>
        /// The brush for a stroke starting at <paramref name="start"/>: fenced to <paramref name="block"/>
        /// (the closed block under the start, or null) when StayInBlock is on and a block exists, else the
        /// open work box around the start (kept on the start's side of the roads when Roads is on);
        /// unconstrained (= Paint) when nothing is left to stop at. Obstacles the rules switch off are
        /// ignored, water only counts when Water is on.
        /// </summary>
        public BlockBrush Build(float2 start, IReadOnlyList<float2> block, IEnumerable<BrushObstacle> obstacles,
            Func<float2, bool> underWater = null, float cellSize = BrushMask.DefaultCellSize)
        {
            var r = Normalized();
            var rings = new List<IReadOnlyList<float2>>();
            if (obstacles != null)
                foreach (var o in obstacles)
                    if (o.Ring != null && r.Stops(o.Kind)) rings.Add(o.Ring);
            var water = r.WaterOn ? underWater : null;
            BlockBrush brush;
            if (r.LockOn && block != null && block.Count >= 3) brush = new BlockBrush(block, rings, cellSize, water);
            else if (rings.Count == 0 && water == null) brush = BlockBrush.Free(start, cellSize);
            else brush = BlockBrush.Open(start, rings, cellSize, water, BlockBrush.OpenHalfSize, keepSide: r.Stops(BrushObstacleKind.Road));
            brush.SnapDistance = r.Snap;
            brush.Smoothing = r.Smoothing;
            return brush;
        }
    }
}
