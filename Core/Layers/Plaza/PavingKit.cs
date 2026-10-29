// Art pass a2 (playtest 3: "a flat grey slab"): squares are laid out in real paving, not one surface.
//
// Why a composition of non-overlapping pieces: every paved surface is drawn through the −94 overlay
// clone (BuildService.OverlayPavedSurfaces), so two paved surfaces that overlap z-fight, and a grass
// surface under a paved one is simply hidden. A square's design (border band, rondel, axes, bands,
// lawns, planting beds) is therefore cut into a tiling: each element claims its shape minus what the
// elements before it claimed, the field gets what is left. CS2 areas have no holes, so annuli and
// frames are opened with Poly.CutHoles.
//
// Palettes name explicit assets from the player's real catalog (modded paving packs first — G87
// Urban Surfaces, EAI Surface Pack — then the vanilla surface that reads closest), so a square never
// depends on the automatic pool. Vanilla look (thumbnails): Tiles 01 light grey setts, Tiles 02 warm
// beige setts, Tiles 03 dark grey slab grid, Concrete 02 pale, Pavement 01 asphalt-dark.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Plaza
{
    /// <summary>
    /// The art-pass palettes other paved types still use (market square, transit plaza, fairground,
    /// amphitheatre). Plazas use the player-facing <see cref="GroundPalettes"/> (s18); ids live in
    /// <see cref="GroundSurfaces"/>.
    /// </summary>
    public static class PavingPalettes
    {
        public const string SlabGrey = GroundSurfaces.SlabGrey;
        public const string SlabBeige = GroundSurfaces.SlabBeige;
        public const string SlabWhite = GroundSurfaces.SlabWhite;
        public const string SlabBlack = GroundSurfaces.SlabBlack;
        public const string ClayD = GroundSurfaces.ClayD;
        public const string ClayF = GroundSurfaces.ClayF;
        public const string ClayH = GroundSurfaces.ClayH;
        public const string Setts = GroundSurfaces.Setts;
        public const string WarmCobble = GroundSurfaces.WarmCobble;
        public const string ArcCobble = GroundSurfaces.ArcCobble;
        public const string CrazyPaving = GroundSurfaces.CrazyPaving;
        public const string DarkFlags = GroundSurfaces.DarkFlags;
        public const string LargeSlabs = GroundSurfaces.LargeSlabs;
        public const string LargeSlabsDark = GroundSurfaces.LargeSlabsDark;
        public const string Blocks = GroundSurfaces.Blocks;
        public const string GreyStone = GroundSurfaces.GreyStone;
        public const string Herringbone = GroundSurfaces.Herringbone;
        public const string Hexagonal = GroundSurfaces.Hexagonal;
        public const string Polished = GroundSurfaces.Polished;
        public const string RedBrick = GroundSurfaces.RedBrick;
        public const string WalkingStone = GroundSurfaces.WalkingStone;

        public const string VTilesLight = GroundSurfaces.VTilesLight, VTilesWarm = GroundSurfaces.VTilesWarm, VTilesDark = GroundSurfaces.VTilesDark;
        public const string VConcrete = GroundSurfaces.VConcrete, VConcretePale = GroundSurfaces.VConcretePale, VAsphalt = GroundSurfaces.VAsphalt;
        public const string VGrass = GroundSurfaces.VGrass;

        private static AssetRef R(string id, params string[] fallbacks) => new AssetRef(id, fallbacks);

        public static readonly PavingPalette Civic = new PavingPalette
        {
            Id = "civic",
            Field = R(SlabGrey, GreyStone, VTilesLight), Band = R(DarkFlags, WalkingStone, VTilesDark),
            Accent = R(SlabWhite, Polished, VConcretePale), Inlay = R(ArcCobble, Hexagonal, VTilesDark),
        };
        public static readonly PavingPalette Warm = new PavingPalette
        {
            Id = "warm",
            Field = R(SlabBeige, VTilesWarm), Band = R(ClayD, RedBrick, VTilesDark),
            Accent = R(SlabWhite, Polished, VConcretePale), Inlay = R(ClayF, RedBrick, VTilesDark),
        };
        public static readonly PavingPalette Garden = new PavingPalette
        {
            Id = "garden",
            Field = R(ClayH, Herringbone, VTilesWarm), Band = R(WarmCobble, Setts, VTilesLight),
            Accent = R(CrazyPaving, SlabBeige, VConcretePale), Inlay = R(ClayD, RedBrick, VTilesDark),
        };
        public static readonly PavingPalette Modern = new PavingPalette
        {
            Id = "modern",
            Field = R(LargeSlabs, Polished, VConcretePale), Band = R(SlabBlack, Hexagonal, VTilesDark),
            Accent = R(Polished, SlabWhite, VConcrete), Inlay = R(LargeSlabsDark, WalkingStone, VTilesDark),
        };
        public static readonly PavingPalette Cobble = new PavingPalette
        {
            Id = "cobble",
            Field = R(Setts, GreyStone, VTilesLight), Band = R(DarkFlags, WalkingStone, VTilesDark),
            Accent = R(SlabGrey, Polished, VConcretePale), Inlay = R(ArcCobble, Hexagonal, VTilesDark),
        };
        public static readonly PavingPalette Transit = new PavingPalette
        {
            Id = "transit",
            Field = R(Blocks, GreyStone, VTilesLight), Band = R(SlabBlack, Hexagonal, VTilesDark),
            Accent = R(LargeSlabs, Polished, VConcretePale), Inlay = R(LargeSlabsDark, WalkingStone, VTilesDark),
        };

        public static readonly PavingPalette[] All = { Civic, Warm, Garden, Modern, Cobble, Transit };
        public static readonly string[] Ids = { "civic", "warm", "garden", "modern", "cobble", "transit" };

        public static PavingPalette Get(string id, PavingPalette fallback = null)
        {
            foreach (var p in All) if (p.Id == id) return p;
            return fallback ?? Civic;
        }

        /// <summary>The palette with its field replaced by the player's own surface choice (when set).</summary>
        public static PavingPalette WithField(PavingPalette p, string chosen)
        {
            if (string.IsNullOrEmpty(chosen)) return p;
            var c = p.Clone();
            c.Field = new AssetRef(chosen, p.Field.Id);
            return c;
        }
    }

    /// <summary>
    /// A square's ground as a tiling: elements in priority order (first claims first), the field fills
    /// the rest. Emits only pieces CS2 keeps; slivers under ~1.5 m² are left to the field's neighbours.
    /// </summary>
    public sealed class PavingPlan
    {
        private struct Element
        {
            public List<float2[]> Rings;
            public SurfaceRole Role;
            public AssetRef Asset;
            public string Category;
            /// <summary>(rf-shops3) Claimed but not laid: bare ground in the paving (tree pits, planting islands painted instead).</summary>
            public bool Hole;
        }

        private readonly List<Element> _elements = new List<Element>();

        public int Count => _elements.Count;

        public void Add(float2[] ring, SurfaceRole role, AssetRef asset, string category = null)
        {
            if (ring == null || ring.Length < 3) return;
            Add(new List<float2[]> { ring }, role, asset, category);
        }

        public void Add(List<float2[]> rings, SurfaceRole role, AssetRef asset, string category = null)
        {
            if (rings == null || rings.Count == 0) return;
            var ccw = new List<float2[]>(rings.Count);
            foreach (var r in rings) if (r != null && r.Length >= 3 && Poly.Area(r) > 0.5f) ccw.Add(Poly.EnsureCcw((float2[])r.Clone()));
            if (ccw.Count == 0) return;
            _elements.Add(new Element { Rings = ccw, Role = role, Asset = asset, Category = category });
        }

        /// <summary>
        /// (rf-shops3) Bare ground: the shapes are kept out of everything added after them and out of the field, and nothing is laid
        /// there (the terrain shows: tree pits and planting islands the layer paints with soil or lawn).
        /// </summary>
        public void Hole(List<float2[]> rings)
        {
            if (rings == null || rings.Count == 0) return;
            var ccw = new List<float2[]>(rings.Count);
            foreach (var r in rings) if (r != null && r.Length >= 3 && Poly.Area(r) > 0.5f) ccw.Add(Poly.EnsureCcw((float2[])r.Clone()));
            if (ccw.Count == 0) return;
            _elements.Add(new Element { Rings = ccw, Hole = true });
        }

        /// <summary>Direction the field's openings are split along (the square's own axis): seams run with the paving.</summary>
        public float2 Axis = new float2(1f, 0f);

        /// <summary>Emits the tiling: every element clipped to the outline minus earlier claims, then the field. Returns surfaces emitted.</summary>
        public int Emit(LayerContext ctx, float2[] outline, SurfaceRole fieldRole, AssetRef field, string fieldCategory = null, bool emitField = true)
        {
            int n = 0;
            // Claims are kept as separate regions (never unioned): a difference against overlapping clip
            // rings is the same under the non-zero rule, and only claims whose bounds meet the element
            // take part — this keeps a square with 60 tree pits well inside the planning budget.
            var claimed = new List<(Region r, float2 min, float2 max)>();
            var outlineList = new[] { (IReadOnlyList<float2>)outline };
            foreach (var e in _elements)
            {
                ctx.Check();
                List<Region> inside;
                try { inside = Poly.Boolean(e.Rings, outlineList, BoolOp.Intersection); }
                catch (Exception) { continue; }
                if (inside.Count == 0) continue;
                Bounds(inside, out var mn, out var mx);
                var clip = new List<Region>();
                foreach (var c in claimed)
                    if (c.min.x <= mx.x && c.max.x >= mn.x && c.min.y <= mx.y && c.max.y >= mn.y) clip.Add(c.r);
                var part = clip.Count == 0 ? inside : Poly.Boolean(inside, clip, BoolOp.Difference);
                if (e.Hole)
                {
                    foreach (var h in part)
                    {
                        if (h?.Outer == null || h.Outer.Length < 3) continue;
                        Poly.Bounds(h.Outer, out var hmn, out var hmx);
                        claimed.Add((h, hmn, hmx));
                    }
                    continue;
                }
                // s18: only what was really laid is claimed. A piece too small or too thin to lay (a sliver
                // between two bands) stays unclaimed, so the field paves it instead of leaving bare ground.
                var laid = new List<float2[]>();
                n += EmitRegions(ctx, part, e.Role, e.Asset, e.Category, MinPiece, MinPieceWidth, laid);
                foreach (var ring in laid)
                {
                    Poly.Bounds(ring, out var rmn, out var rmx);
                    claimed.Add((new Region(ring), rmn, rmx));
                }
            }
            if (!emitField) return n;
            var all = new List<Region>(claimed.Count);
            foreach (var c in claimed) all.Add(c.r);
            var whole = new Region(Poly.EnsureCcw((float2[])outline.Clone()));
            var rest = all.Count == 0 ? new List<Region> { whole } : Poly.Boolean(new[] { whole }, all, BoolOp.Difference);
            n += EmitRegions(ctx, rest, fieldRole, field, fieldCategory, 2.0f, 0.5f, null);
            return n;
        }

        /// <summary>Smallest design piece (m²) / mean width (m) that is laid; smaller ones go to the field (s18: no slivers).</summary>
        public const float MinPiece = 2.5f, MinPieceWidth = 0.55f;

        private static void Bounds(List<Region> regions, out float2 mn, out float2 mx)
        {
            mn = new float2(float.MaxValue); mx = new float2(float.MinValue);
            foreach (var r in regions)
            {
                Poly.Bounds(r.Outer, out var a, out var b);
                mn = math.min(mn, a); mx = math.max(mx, b);
            }
        }

        private int EmitRegions(LayerContext ctx, List<Region> regions, SurfaceRole role, AssetRef asset, string category, float minArea, float minWidth, List<float2[]> laid)
        {
            int n = 0;
            foreach (var r in regions)
            {
                if (r == null || r.Outer == null || r.Area < minArea) continue;
                foreach (var ring in OpenHoles(r))
                {
                    if (ring == null || ring.Length < 3) continue;
                    float area = Poly.Area(ring);
                    if (area < minArea || Poly.MeanWidth(ring) < minWidth) continue;
                    // CS2 acceptance may split a ring (thin necks of an arc cut by a walk): judge the pieces it keeps.
                    foreach (var acc in SurfaceAcceptance.MakeAcceptable2D(ring))
                    {
                        if (acc == null || acc.Length < 3) continue;
                        // (2026-10-28) The build removes every node the game's shape rules refuse (AreaRings); a removed
                        // node of a big piece is a long wedge of bare ground, or (a reflex node) a wedge over the
                        // neighbour. Repair here, never beyond the piece, and claim only what is really laid.
                        foreach (var piece in GameRings(acc))
                        {
                            if (Poly.Area(piece) < minArea * 0.6f || Poly.MeanWidth(piece) < minWidth * 0.6f) continue;
                            if (ctx.Surface(piece, role, asset, category)) { n++; laid?.Add(piece); }
                        }
                    }
                }
            }
            return n;
        }

        /// <summary>Area (m²) the shape-rule repairs of <see cref="GameRings"/> took off the pieces (for the plan facts).</summary>
        public float RepairLoss { get; private set; }
        public static Action<string> DebugLoss;

        /// <summary>
        /// (2026-10-28) The ring as the game keeps it unchanged: valid as it is, or repaired by the build's own rule
        /// (AreaRings.PrepareSurface) and clipped back inside the ring wherever a repair grew it, until every piece is
        /// valid. Pieces that cannot be made valid are left out (the area stays unclaimed for later elements and the field).
        /// </summary>
        private List<float2[]> GameRings(float2[] ring)
        {
            float before = Poly.Area(ring);
            var res = GameRings(ring, 0);
            float after = 0f;
            foreach (var r in res) after += Poly.Area(r);
            RepairLoss += math.max(0f, before - after);
            if (DebugLoss != null && before - after > 1f) DebugLoss($"n={ring.Length} before={before:0.0} after={after:0.0} pieces={res.Count} valid0={Build.AreaRings.IsValid(ring, Build.AreaRings.SurfaceMinNodeDistance + Build.AreaRings.Margin)} first={Build.AreaRings.FirstViolation(ring, Build.AreaRings.SurfaceMinNodeDistance + Build.AreaRings.Margin)} ring={string.Join(" ", System.Linq.Enumerable.Select(ring, p => $"({p.x:0.##},{p.y:0.##})"))}");
            return res;
        }

        /// <summary>The ring shrunk by <paramref name="e"/> and grown back (miter joins), clipped to the ring: simple hole-free pieces.</summary>
        private static List<float2[]> Opening(float2[] ring, float e)
        {
            var res = new List<float2[]>();
            try
            {
                foreach (var inner in Poly.Offset(ring, e, OffsetJoin.Miter))
                    foreach (var grown in Poly.Offset(inner, -e, OffsetJoin.Miter))
                        foreach (var back in Poly.Boolean(new[] { grown }, new[] { new Region(Poly.EnsureCcw((float2[])ring.Clone())) }, BoolOp.Intersection))
                        {
                            if (back?.Outer == null || back.Area < 0.5f) continue;
                            if (back.HasHoles) res.AddRange(Poly.CutHoles(back));
                            else res.Add(Poly.EnsureCcw(back.Outer));
                        }
            }
            catch (Exception) { res.Clear(); }
            return res;
        }

        /// <summary>A valid version of <paramref name="piece"/> (nodes removed) that stays inside <paramref name="limit"/>, or null.</summary>
        private static float2[] Fix(float2[] piece, float2[] limit, float d)
        {
            float dd = d + Build.AreaRings.Margin;
            for (int attempt = 0; attempt < 2 && piece != null; attempt++)
            {
                if (!Build.AreaRings.IsValid(piece, dd)) piece = Build.AreaRings.MakeValid(piece, d);
                if (piece == null || !Build.AreaRings.IsValid(piece, dd)) return null;
                List<Region> extra;
                try { extra = Poly.Boolean(new[] { (IReadOnlyList<float2>)piece }, new[] { (IReadOnlyList<float2>)limit }, BoolOp.Difference); }
                catch (Exception) { return null; }
                float grown = 0f;
                foreach (var e in extra) grown += e.Area;
                if (grown < 0.05f) return piece;
                // A removed reflex node grew it over the neighbour: clip back and try once more.
                List<Region> back;
                try { back = Poly.Boolean(new[] { (IReadOnlyList<float2>)piece }, new[] { (IReadOnlyList<float2>)limit }, BoolOp.Intersection); }
                catch (Exception) { return null; }
                Region best = null;
                foreach (var b in back) if (b?.Outer != null && !b.HasHoles && (best == null || b.Area > best.Area)) best = b;
                piece = best != null ? Poly.EnsureCcw(best.Outer) : null;
            }
            return null;
        }

        private static List<float2[]> GameRings(float2[] ring, int depth)
        {
            var res = new List<float2[]>();
            float d = Build.AreaRings.SurfaceMinNodeDistance;
            if (Build.AreaRings.IsValid(ring, d + Build.AreaRings.Margin)) { res.Add(ring); return res; }
            // First an opening (shrink, then grow back by the same amount, clipped to the ring): it takes off the needle
            // tips where a walk meets a rondel at a slant and the necks narrower than the game allows, nothing else.
            foreach (float e in new[] { 0.25f, 0.45f })
            {
                var opened = Opening(ring, e);
                if (opened.Count == 0) continue;
                var fixedPieces = new List<float2[]>();
                foreach (var o in opened)
                {
                    if (Poly.Area(o) < MinPiece * 0.5f) continue;
                    var f = Fix(o, ring, d);
                    if (f == null) { fixedPieces = null; break; }
                    fixedPieces.Add(f);
                }
                if (fixedPieces != null && fixedPieces.Count > 0) { res = fixedPieces; break; }
            }
            if (res.Count == 0)
            {
                var tries = Build.AreaRings.PrepareSurface(ring, d);
                var v = Build.AreaRings.MakeValid(ring, d);
                if (tries.Count == 0 && v != null) tries.Add(v);
                foreach (var t in tries)
                {
                    var f = Fix(t, ring, d);
                    if (f != null) res.Add(f);
                }
            }
            // Nothing valid came of it: hand the ring on as it is (the build repairs it, as before).
            if (res.Count == 0) { res.Add(ring); return res; }
            // What the repairs cut off (the wedge behind a removed node) is laid again as its own pieces.
            if (depth < 2 && res.Count > 0)
            {
                List<Region> rest;
                try
                {
                    var clips = new List<IReadOnlyList<float2>>();
                    foreach (var r in res) clips.Add(r);
                    rest = Poly.Boolean(new[] { (IReadOnlyList<float2>)ring }, clips, BoolOp.Difference);
                }
                catch (Exception) { rest = new List<Region>(); }
                foreach (var part in rest)
                {
                    if (part?.Outer == null || part.Area < MinPiece || Poly.MeanWidth(part.Outer) < MinPieceWidth) continue;
                    var rings = part.HasHoles ? Poly.CutHoles(part) : new List<float2[]> { part.Outer };
                    foreach (var pr in rings)
                    {
                        if (pr == null || pr.Length < 3) continue;
                        foreach (var g in GameRings(Poly.EnsureCcw(pr), depth + 1))
                            if (Build.AreaRings.IsValid(g, d + Build.AreaRings.Margin)) res.Add(g);   // a rest piece only when it is clean
                    }
                }
            }
            return res;
        }

        /// <summary>
        /// Hole-free rings for a region: one straight cut along <see cref="Axis"/> through each row of
        /// openings (a row of tree pits costs one cut, not one per pit), the generic seams for the rest.
        /// </summary>
        private List<float2[]> OpenHoles(Region r)
        {
            var result = new List<float2[]>();
            if (!r.HasHoles) { result.Add(Poly.EnsureCcw(r.Outer)); return result; }
            var axis = math.normalizesafe(Axis, new float2(1f, 0f));
            var side = new float2(-axis.y, axis.x);
            var rows = new List<float>();
            foreach (var h in r.Holes) rows.Add(math.dot(Poly.Centroid(h), side));
            rows.Sort();
            var cuts = new List<float>();
            foreach (var v in rows) if (cuts.Count == 0 || v - cuts[cuts.Count - 1] > 0.6f) cuts.Add(v);
            Poly.Bounds(r.Outer, out var mn, out var mx);
            float reach = math.length(mx - mn) + 10f;
            var c0 = (mn + mx) * 0.5f;
            var pieces = new List<Region> { r };
            foreach (var v in cuts)
            {
                var basePt = c0 + side * (v - math.dot(c0, side));
                var line = new[] { basePt - axis * reach, basePt + axis * reach };
                var next = new List<Region>();
                foreach (var p in pieces)
                {
                    if (!p.HasHoles) { next.Add(p); continue; }
                    List<Region> split;
                    try { split = Poly.Split(p, line); }
                    catch (Exception) { split = null; }
                    if (split == null || split.Count == 0) next.Add(p); else next.AddRange(split);
                }
                pieces = next;
            }
            // (2026-10-28) A hole the row cuts missed (or a split that failed): cut it across, through its own centre, before
            // falling back to keyhole seams — a keyhole's zero-width bridge breaks the game's shape rules (AreaRings: edges
            // closer than 0.375 m), and the build's repair then takes a wedge out of the paving.
            for (int pass = 0; pass < 2; pass++)
            {
                var dir = pass == 0 ? side : axis;
                var next = new List<Region>();
                foreach (var p in pieces)
                {
                    if (!p.HasHoles) { next.Add(p); continue; }
                    var c = Poly.Centroid(p.Holes[0]);
                    var line = new[] { c - dir * reach, c + dir * reach };
                    List<Region> split;
                    try { split = Poly.Split(p, line); }
                    catch (Exception) { split = null; }
                    if (split == null || split.Count < 2) next.Add(p); else next.AddRange(split);
                }
                pieces = next;
            }
            foreach (var p in pieces)
            {
                if (p.HasHoles) result.AddRange(Poly.CutHoles(p));
                else result.Add(Poly.EnsureCcw(p.Outer));
            }
            return result;
        }
    }

    /// <summary>Shapes that squares are laid out with.</summary>
    public static class PavingShapes
    {
        /// <summary>The frame band: the outline minus its inset (opened into hole-free pieces by the plan).</summary>
        public static List<float2[]> Frame(float2[] outline, float width)
        {
            var result = new List<float2[]>();
            try
            {
                var inner = Poly.Offset(outline, width);
                var band = inner.Count == 0 ? new List<Region> { new Region(outline) } : Poly.Boolean(new[] { (IReadOnlyList<float2>)outline }, ToList(inner), BoolOp.Difference);
                foreach (var r in band) result.AddRange(Poly.CutHoles(r));
            }
            catch (Exception) { }
            return result;
        }

        /// <summary>The outline's inset ring(s) (the field inside a frame).</summary>
        public static List<float2[]> Inset(float2[] outline, float width)
        {
            try { return Poly.Offset(outline, width); }
            catch (Exception) { return new List<float2[]>(); }
        }

        /// <summary>A ring of width w around a disc of radius r (two half-annuli, no hole).</summary>
        public static List<float2[]> Annulus(float2 c, float r, float w, int sides = 48)
        {
            var result = new List<float2[]>();
            if (r <= 0.2f || w <= 0.2f) return result;
            int half = sides / 2;
            for (int k = 0; k < 2; k++)
            {
                var pts = new List<float2>(half * 2 + 2);
                float a0 = k * math.PI;
                for (int i = 0; i <= half; i++)
                {
                    float a = a0 + i * math.PI / half;
                    pts.Add(c + new float2(math.cos(a), math.sin(a)) * (r + w));
                }
                for (int i = half; i >= 0; i--)
                {
                    float a = a0 + i * math.PI / half;
                    pts.Add(c + new float2(math.cos(a), math.sin(a)) * r);
                }
                result.Add(pts.ToArray());
            }
            return result;
        }

        /// <summary>A straight band of width w from a to b (rectangle).</summary>
        public static float2[] Band(float2 a, float2 b, float w)
        {
            var d = b - a;
            float len = math.length(d);
            if (len < 0.5f) return null;
            return LayerRegion.Rect((a + b) * 0.5f, d / len, len * 0.5f, w * 0.5f);
        }

        /// <summary>Bands of width w every `pitch` metres in both directions of a frame, clipped later by the plan.</summary>
        public static List<float2[]> Grid(LayerRegion region, float2 centre, float2 axis, float pitch, float w)
        {
            var result = new List<float2[]>();
            var side = new float2(-axis.y, axis.x);
            float reach = math.length(region.Max - region.Min);
            int n = (int)math.ceil(reach / pitch);
            for (int i = -n; i <= n; i++)
            {
                float o = (i + 0.5f) * pitch;
                result.Add(LayerRegion.Rect(centre + side * o, axis, reach, w * 0.5f));
                result.Add(LayerRegion.Rect(centre + axis * o, side, reach, w * 0.5f));
            }
            return result;
        }

        /// <summary>A regular polygon (octagon by default) — a rondel that reads as laid stone, not a CAD circle.</summary>
        public static float2[] Polygon(float2 c, float r, int sides, float phase = 0f) => LayerRegion.Circle(c, r, sides, phase);

        private static List<IReadOnlyList<float2>> ToList(List<float2[]> rings)
        {
            var l = new List<IReadOnlyList<float2>>(rings.Count);
            foreach (var r in rings) l.Add(r);
            return l;
        }
    }
}
