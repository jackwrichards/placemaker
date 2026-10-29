// Placemaker — dynamic decals: kinds are recipes, the library is data (Recipes/decals/kinds.json; docs/DYNAMIC-DECALS.md). GPL-3.0.
//
// A kind is a list of generator operations over named fields (noise, warp, cells, cracks, scatter of stamps, edge
// erosion, colour layers). Any builder (a type, the AI builder, a later player tool) defines a new look by writing a kind
// in JSON; no engine code. The interpreter is DecalGenerator.
//
//   {
//     "id": "mud",                     unique id (strokes name it)
//     "texel": 0.06,                   metres per texel (detail; 0.03 .. 0.25)
//     "layers": "terrain,roads",       what it lies on (terrain = ground and surfaces, roads = paths and streets)
//     "order": 2,                      draw order among our decals
//     "lod": 40,                       stays drawn up to lod x its size in metres from the camera (at least 250 m)
//     "edge": { "feather": 1.5, "warp": 1.2, "warpScale": 5, "round": 1, "taper": 0, "widthNoise": 0 },
//     "ops": [ { "op": "fbm", "out": "n", "scale": 3, "octaves": 4 }, … ]
//   }
//
// Built-in fields: shape (footprint 0..1 with its soft warped edge), edge (metres inside the footprint, negative outside),
// along / across (metres along and across a path, or the box's own x / z), u (0..1 along a path or a box), one.
// A number parameter may follow the stroke: {"wet": [dry, soaked]}, {"age": [fresh, old]}, {"strength": [weak, strong]}.

using System;
using System.Collections.Generic;
using System.Globalization;
using Placemaker.Core.Tool;
using Unity.Mathematics;

namespace Placemaker.Core.Decals.Dynamic
{
    /// <summary>A number that may follow the stroke's wetness, age or strength.</summary>
    public struct DecalParam
    {
        public float A, B;
        /// <summary>0 constant, 1 wetness, 2 age, 3 strength.</summary>
        public byte By;

        public static DecalParam Const(float v) => new DecalParam { A = v, B = v };

        public float Eval(DecalStroke s)
        {
            switch (By)
            {
                case 1: return math.lerp(A, B, math.saturate(s.Wetness));
                case 2: return math.lerp(A, B, math.saturate(s.Age));
                case 3: return math.lerp(A, B, math.saturate(s.Strength));
                default: return A;
            }
        }
    }

    /// <summary>An input of an operation: a field (by index) or a constant.</summary>
    public struct DecalInput
    {
        public int Field;   // -1 = constant
        public float Const;
        public static DecalInput Of(float c) => new DecalInput { Field = -1, Const = c };
    }

    public enum DecalOpKind
    {
        Fbm, Worley, Remap, Band, Mul, Add, Sub, Max, Min, Erode, Cracks, Scatter, Layer,
        Wave, Abs, Pow, Mix, Invert,
    }

    public enum StampKind
    {
        Leaf, Pebble, Print, Blob, Twig, Tread, Litter,
        Oak, Maple, Birch, Rock, Chip, Butt, Paper, Cap, Splat, Paw, Boot, Petal,
    }

    public sealed class DecalOp
    {
        public DecalOpKind Kind;
        public int Out = -1;
        public DecalInput A, B, C;
        public DecalParam Scale, Lo, Hi, K, Width, Warp, WarpScale, Sharp, Gain;
        public int Octaves = 4;
        public uint Salt;
        public bool Ridged, Edge, CellId;
        public float2 Stretch = new float2(1f, 1f);
        public float Jitter = 0.9f;

        // Layer
        public float3 Color = new float3(0.4f, 0.33f, 0.25f), Color2 = new float3(0.4f, 0.33f, 0.25f);
        public DecalInput Tone, Alpha, Height, SmoothBy;
        public DecalParam AlphaK, Vary, VaryScale, Smooth, Smooth2, HeightK, Ao;

        // Cracks
        public DecalParam Seeds, Step, Turn, Branch;
        public float2 Len = new float2(4f, 14f), CrackWidth = new float2(0.015f, 0.05f);
        /// <summary>Chain cracks: one main crack pinned to the middle of the box's two ends (x = ±half, z = 0), so pieces
        /// laid end to end make one long crack; <see cref="Wander"/> is how far it strays (m).</summary>
        public bool Chain;
        public DecalParam Wander;

        // Scatter
        public StampKind Stamp;
        /// <summary>Several stamp shapes mixed (each stamp picks one); empty = <see cref="Stamp"/> only.</summary>
        public StampKind[] Stamps = Array.Empty<StampKind>();
        /// <summary>A fixed direction for every stamp (degrees from +x, NaN = random) and its spread (degrees).</summary>
        public float Angle = float.NaN, AngleJitter = 15f;
        public DecalParam Density, Clusters, ClusterShare;
        public float2 ClusterRadius = new float2(0.6f, 2.5f), Size = new float2(0.06f, 0.13f), Aspect = new float2(0.45f, 0.7f), StampAlpha = new float2(0.75f, 1f);
        public float2 Ramp = new float2(1f, 1f);
        public float NoiseScale, NoiseLo = -1f, NoiseHi = 1f, Side, EdgeBias, ValueVary = 0.12f, StampHeight = 0.4f, StampSmooth = 0.3f;
        public bool AlongPath;
        /// <summary>A walking trail instead of a scatter: one stamp every <see cref="Trail"/> metres along the box's x (or the
        /// path), alternating left and right by <see cref="Gait"/>, facing the way of travel (footprints, paw prints).</summary>
        public float Trail, Gait = 0.1f;
        public int MaxStamps = 6000;
        public float3[] Palette = Array.Empty<float3>();
    }

    public sealed class DecalKind
    {
        public const int BuiltinShape = 0, BuiltinEdge = 1, BuiltinAlong = 2, BuiltinAcross = 3, BuiltinU = 4, BuiltinOne = 5, BuiltinRadial = 6, BuiltinCount = 7;
        /// <summary>radial: 0 at the box's centre, 1 at its edge (paths: 0 on the line, 1 at its edge).</summary>
        public static readonly string[] Builtins = { "shape", "edge", "along", "across", "u", "one", "radial" };

        public string Id = "";
        public string About = "";
        /// <summary>The library family it belongs to (leaves, ground, water, cracks, litter, prints, strips, tone …), for browsing.</summary>
        public string Family = "";
        /// <summary>A short plain name for people ("Oak leaves").</summary>
        public string Name = "";
        /// <summary>"macro" (reads from the player's view, 30-100 m up: the kinds builders should reach for first) or "detail"
        /// (small things that only show up close: use sparingly, as texture inside macro looks).</summary>
        public string Tier = "macro";
        /// <summary>How many variants the shared library makes and how they differ (DecalVariants).</summary>
        public VariantSpec Variants = new VariantSpec();
        public float Texel = 0.08f;
        public byte Layers = DecalLayerBits.Terrain;
        public int Order;
        public float Lod = 40f;
        public float Feather = 1f, Warp, WarpScale = 6f, Round = 1f, Taper, WidthNoise;
        /// <summary>Relief in metres for height 1 (normal map strength).</summary>
        public float Bump = 0.012f;
        public float Opacity = 1f;
        /// <summary>Colour saturation of the result (1 = as the recipe says, 0 = grey); a family file may set a default.</summary>
        public float Saturation = 1f;
        /// <summary>Most texels a side of one tile (keeps a tile at or under 512 x 512).</summary>
        public int MaxTexels = 512;
        public readonly List<DecalOp> Ops = new List<DecalOp>();
        /// <summary>Named fields (built-ins first).</summary>
        public readonly List<string> Fields = new List<string>(Builtins);
        /// <summary>The kind's JSON (a plan's own kinds are saved with the strokes).</summary>
        public string Json = "";

        public int FieldIndex(string name, bool create)
        {
            int i = Fields.IndexOf(name);
            if (i >= 0 || !create) return i;
            Fields.Add(name);
            return Fields.Count - 1;
        }
    }

    public sealed class DecalLibrary
    {
        public const string FolderName = "decals";
        public const string FileName = "kinds.json";

        /// <summary>The library the game loaded (Recipes/decals/kinds.json); tests set their own.</summary>
        public static DecalLibrary Current = new DecalLibrary();

        private readonly Dictionary<string, DecalKind> _kinds = new Dictionary<string, DecalKind>(StringComparer.Ordinal);
        public readonly List<string> Warnings = new List<string>();
        public IEnumerable<DecalKind> Kinds => _kinds.Values;
        public int Count => _kinds.Count;

        public DecalKind Get(string id) => id != null && _kinds.TryGetValue(id, out var k) ? k : null;
        public bool Has(string id) => id != null && _kinds.ContainsKey(id);
        public void Add(DecalKind k) { if (k != null && k.Id.Length > 0) _kinds[k.Id] = k; }

        /// <summary>Reads a library file ({"kinds": [...]}); bad kinds are skipped with a warning.</summary>
        public static DecalLibrary Parse(string json)
        {
            var lib = new DecalLibrary();
            if (!JsonValue.TryParse(json ?? "", out var root, out var err)) { lib.Warnings.Add("decal library: " + err); return lib; }
            var list = root.IsArray ? root : root["kinds"];
            string family = root.IsObject ? root["family"].AsString("") : "";
            float saturation = root.IsObject ? (float)root["saturation"].AsNumber(1) : 1f;
            // a family's colour levels for its variants ("saturations": [1, 0.8]): kinds without their own take them
            var levels = new List<float>();
            if (root.IsObject) foreach (var x in root["saturations"].Items) levels.Add(math.clamp((float)x.AsNumber(1), 0f, 1f));
            foreach (var k in list.Items)
            {
                var kind = ParseKind(k, lib.Warnings);
                if (kind == null) continue;
                if (kind.Family.Length == 0) kind.Family = family;
                if (float.IsNaN(kind.Saturation)) kind.Saturation = saturation;
                if (kind.Variants.Saturations.Length == 0 && levels.Count > 0) kind.Variants.Saturations = levels.ToArray();
                if (lib._kinds.ContainsKey(kind.Id)) lib.Warnings.Add("decal library: duplicate kind " + kind.Id);
                lib._kinds[kind.Id] = kind;
            }
            return lib;
        }

        /// <summary>Sub-folder of Recipes/decals with the shared library's families (one file each: leaves.json, ground.json …).</summary>
        public const string LibraryFolder = "library";

        /// <summary>The whole library from Recipes/decals: kinds.json (the stroke kinds) and every library/*.json family, in
        /// name order. Missing files are skipped; read errors become warnings.</summary>
        public static DecalLibrary LoadFolder(string decalsDir)
        {
            var lib = new DecalLibrary();
            var files = new List<string>();
            string main = System.IO.Path.Combine(decalsDir, FileName);
            if (System.IO.File.Exists(main)) files.Add(main);
            string sub = System.IO.Path.Combine(decalsDir, LibraryFolder);
            if (System.IO.Directory.Exists(sub))
            {
                var more = System.IO.Directory.GetFiles(sub, "*.json");
                Array.Sort(more, StringComparer.OrdinalIgnoreCase);
                files.AddRange(more);
            }
            foreach (var f in files)
            {
                try { lib.Merge(Parse(System.IO.File.ReadAllText(f)), System.IO.Path.GetFileName(f)); }
                catch (Exception e) { lib.Warnings.Add(System.IO.Path.GetFileName(f) + ": " + e.Message); }
            }
            return lib;
        }

        /// <summary>Adds every kind of another library file (later files win; a duplicate id is a warning).</summary>
        public void Merge(DecalLibrary other, string from)
        {
            if (other == null) return;
            foreach (var w in other.Warnings) Warnings.Add(from + ": " + w);
            foreach (var k in other.Kinds)
            {
                if (_kinds.ContainsKey(k.Id)) Warnings.Add(from + ": kind " + k.Id + " replaces an earlier one");
                _kinds[k.Id] = k;
            }
        }

        /// <summary>One kind from its JSON text (a plan's own kind); null and a warning when it does not read.</summary>
        public static DecalKind ParseKind(string json, List<string> warnings)
        {
            if (!JsonValue.TryParse(json ?? "", out var v, out var err)) { warnings?.Add("decal kind: " + err); return null; }
            return ParseKind(v, warnings);
        }

        public static DecalKind ParseKind(JsonValue v, List<string> warnings)
        {
            if (v == null || !v.IsObject) return null;
            var k = new DecalKind { Id = v["id"].AsString(""), About = v["about"].AsString(""), Family = v["family"].AsString(""), Name = v["name"].AsString(""), Tier = v["tier"].AsString("macro") };
            if (k.Id.Length == 0) { warnings?.Add("decal kind without an id"); return null; }
            try
            {
                k.Json = v.ToString();
                k.Texel = math.clamp((float)v["texel"].AsNumber(0.08), 0.008f, 0.5f);
                k.Layers = DecalLayerBits.Parse(v["layers"].AsString(""), DecalLayerBits.Terrain);
                k.Order = (int)v["order"].AsNumber(0);
                k.Lod = (float)v["lod"].AsNumber(40);
                k.Bump = (float)v["bump"].AsNumber(0.012);
                k.Opacity = (float)v["opacity"].AsNumber(1);
                { float sat = (float)v["saturation"].AsNumber(double.NaN); k.Saturation = float.IsNaN(sat) ? float.NaN : math.clamp(sat, 0f, 1f); }   // NaN: the family default (Parse), else 1
                k.MaxTexels = math.clamp((int)v["maxTexels"].AsNumber(512), 64, 1024) / 4 * 4;
                k.Variants = VariantSpec.Parse(v["variants"]);
                var e = v["edge"];
                if (e.IsObject)
                {
                    k.Feather = (float)e["feather"].AsNumber(1);
                    k.Warp = (float)e["warp"].AsNumber(0);
                    k.WarpScale = math.max(0.2f, (float)e["warpScale"].AsNumber(6));
                    k.Round = math.saturate((float)e["round"].AsNumber(1));
                    k.Taper = (float)e["taper"].AsNumber(0);
                    k.WidthNoise = (float)e["widthNoise"].AsNumber(0);
                }
                int n = 0;
                foreach (var o in v["ops"].Items)
                {
                    var op = ParseOp(k, o, (uint)(++n * 7919));
                    if (op == null) { warnings?.Add($"decal kind {k.Id}: op {n} ('{o["op"].AsString("")}') not understood, skipped"); continue; }
                    k.Ops.Add(op);
                }
                if (k.Ops.Count == 0) { warnings?.Add("decal kind " + k.Id + " has no operations"); return null; }
            }
            catch (Exception ex) { warnings?.Add("decal kind " + k.Id + ": " + ex.Message); return null; }
            return k;
        }

        // ───────────── ops ─────────────

        private static DecalOp ParseOp(DecalKind k, JsonValue o, uint salt)
        {
            if (!o.IsObject) return null;
            var op = new DecalOp { Salt = salt + (uint)o["seed"].AsNumber(0) * 104729u };
            string name = o["op"].AsString("").ToLowerInvariant();
            string outName = o["out"].AsString("");
            switch (name)
            {
                case "fbm": case "noise":
                    op.Kind = DecalOpKind.Fbm;
                    op.Scale = P(o["scale"], 4); op.Octaves = math.clamp((int)o["octaves"].AsNumber(4), 1, 8);
                    op.Gain = P(o["gain"], 0.5f); op.Warp = P(o["warp"], 0); op.WarpScale = P(o["warpScale"], 8);
                    op.Ridged = o["ridged"].AsBool(false);
                    op.Stretch = V2(o["stretch"], new float2(1f, 1f));
                    break;
                case "worley": case "cells":
                    op.Kind = DecalOpKind.Worley;
                    op.Scale = P(o["scale"], 1); op.Edge = o["mode"].AsString("f1") == "edge"; op.CellId = o["mode"].AsString("f1") == "id"; op.Jitter = (float)o["jitter"].AsNumber(0.9);
                    op.Warp = P(o["warp"], 0); op.WarpScale = P(o["warpScale"], 4);
                    break;
                case "remap": case "curve":
                    op.Kind = DecalOpKind.Remap;
                    op.A = In(k, o["in"]); op.Lo = P(o["lo"], 0); op.Hi = P(o["hi"], 1); op.K = P(o["k"], 1);
                    break;
                case "band":
                    op.Kind = DecalOpKind.Band;
                    op.A = In(k, o["in"]); op.Lo = P(o["at"], 0.5f); op.Width = P(o["width"], 0.05f); op.K = P(o["k"], 1);
                    break;
                case "mul": case "add": case "sub": case "max": case "min":
                    op.Kind = name == "mul" ? DecalOpKind.Mul : name == "add" ? DecalOpKind.Add : name == "sub" ? DecalOpKind.Sub : name == "max" ? DecalOpKind.Max : DecalOpKind.Min;
                    op.A = In(k, o["a"]); op.B = In(k, o["b"]);
                    op.C = o.Has("c") ? In(k, o["c"]) : DecalInput.Of(float.NaN);
                    op.K = P(o["k"], 1);
                    break;
                case "erode":
                    op.Kind = DecalOpKind.Erode;
                    op.A = In(k, o["cover"]); op.B = In(k, o["grain"]); op.Sharp = P(o["sharp"], 5);
                    break;
                case "cracks":
                    op.Kind = DecalOpKind.Cracks;
                    op.Seeds = P(o["seeds"], 0.03f); op.Step = P(o["step"], 0.5f); op.Turn = P(o["turn"], 0.45f); op.Branch = P(o["branch"], 0.35f);
                    op.Len = V2(o["len"], new float2(3f, 12f)); op.CrackWidth = V2(o["width"], new float2(0.015f, 0.05f));
                    op.Warp = P(o["warp"], 0.15f);
                    op.Chain = o["chain"].AsBool(false); op.Wander = P(o["wander"], 0.5f);
                    break;
                case "wave":
                    // 0..1 stripes: 0.5 + 0.5 cos(2π (in / period + phase)), sharpened by "sharp" (1 = soft sine)
                    op.Kind = DecalOpKind.Wave;
                    op.A = In(k, o["in"]); op.Scale = P(o["period"], 1); op.Lo = P(o["phase"], 0); op.Sharp = P(o["sharp"], 1); op.K = P(o["k"], 1);
                    op.Warp = P(o["warp"], 0); op.WarpScale = P(o["warpScale"], 3);
                    break;
                case "abs": case "invert":
                    op.Kind = name == "abs" ? DecalOpKind.Abs : DecalOpKind.Invert;
                    op.A = In(k, o["in"]); op.K = P(o["k"], 1);
                    break;
                case "pow": case "gamma":
                    op.Kind = DecalOpKind.Pow;
                    op.A = In(k, o["in"]); op.Lo = P(o["power"], 2); op.K = P(o["k"], 1);
                    break;
                case "mix": case "lerp":
                    op.Kind = DecalOpKind.Mix;
                    op.A = In(k, o["a"]); op.B = In(k, o["b"]); op.C = In(k, o["t"]); op.K = P(o["k"], 1);
                    break;
                case "scatter":
                    op.Kind = DecalOpKind.Scatter;
                    if (o["stamp"].IsArray)
                    {
                        var st = new List<StampKind>();
                        foreach (var x in o["stamp"].Items) st.Add(ParseStamp(x.AsString("leaf")));
                        op.Stamps = st.ToArray();
                        op.Stamp = st.Count > 0 ? st[0] : StampKind.Leaf;
                    }
                    else op.Stamp = ParseStamp(o["stamp"].AsString("leaf"));
                    if (o.Has("angle")) { op.Angle = (float)o["angle"].AsNumber(0); op.AngleJitter = (float)o["angleJitter"].AsNumber(15); }
                    op.Density = P(o["density"], 2); op.Clusters = P(o["clusters"], 0.3f); op.ClusterShare = P(o["cluster"], 0.7f);
                    op.ClusterRadius = V2(o["clusterRadius"], op.ClusterRadius); op.Size = V2(o["size"], op.Size); op.Aspect = V2(o["aspect"], op.Aspect);
                    op.StampAlpha = V2(o["alpha"], op.StampAlpha); op.Ramp = V2(o["ramp"], op.Ramp);
                    var wn = o["noise"];
                    if (wn.IsObject) { op.NoiseScale = (float)wn["scale"].AsNumber(5); op.NoiseLo = (float)wn["lo"].AsNumber(-0.2); op.NoiseHi = (float)wn["hi"].AsNumber(0.5); }
                    op.Side = (float)o["side"].AsNumber(0); op.EdgeBias = (float)o["edgeBias"].AsNumber(0);
                    op.ValueVary = (float)o["value"].AsNumber(0.12); op.StampHeight = (float)o["height"].AsNumber(0.4); op.StampSmooth = (float)o["smooth"].AsNumber(0.3);
                    op.AlongPath = o["align"].AsString("random") == "along";
                    op.Trail = (float)o["trail"].AsNumber(0); op.Gait = (float)o["gait"].AsNumber(0.1);
                    op.MaxStamps = math.clamp((int)o["max"].AsNumber(6000), 1, 40000);
                    op.Palette = Palette(o["palette"]);
                    if (op.Palette.Length == 0) op.Palette = new[] { new float3(0.45f, 0.36f, 0.26f) };
                    return op;   // scatter writes the layers itself
                case "layer": case "paint":
                    op.Kind = DecalOpKind.Layer;
                    op.Alpha = In(k, o["alpha"]); op.AlphaK = P(o["alphaK"], 1);
                    op.Color = Rgb(o["color"], op.Color); op.Color2 = o.Has("color2") ? Rgb(o["color2"], op.Color) : op.Color;
                    op.Tone = o.Has("tone") ? In(k, o["tone"]) : DecalInput.Of(0f);
                    op.Vary = P(o["vary"], 0.12f); op.VaryScale = P(o["varyScale"], 0.7f);
                    op.Smooth = P(o["smooth"], 0.25f); op.Smooth2 = o.Has("smooth2") ? P(o["smooth2"], 0.25f) : op.Smooth;
                    op.SmoothBy = o.Has("smoothBy") ? In(k, o["smoothBy"]) : DecalInput.Of(0f);
                    op.Height = o.Has("height") ? In(k, o["height"]) : DecalInput.Of(0f); op.HeightK = P(o["heightK"], 1);
                    op.Ao = P(o["ao"], 1);
                    return op;
                default:
                    return null;
            }
            if (outName.Length == 0) outName = "f" + k.Fields.Count;
            op.Out = k.FieldIndex(outName, true);
            if (op.Kind == DecalOpKind.Cracks) op.B = new DecalInput { Field = k.FieldIndex(outName + "Halo", true) };
            return op;
        }

        private static StampKind ParseStamp(string s)
        {
            switch ((s ?? "").ToLowerInvariant())
            {
                case "pebble": case "gravel": return StampKind.Pebble;
                case "print": case "footprint": return StampKind.Print;
                case "blob": case "drop": case "clod": return StampKind.Blob;
                case "twig": return StampKind.Twig;
                case "tread": return StampKind.Tread;
                case "litter": return StampKind.Litter;
                case "oak": return StampKind.Oak;
                case "maple": case "plane": return StampKind.Maple;
                case "birch": return StampKind.Birch;
                case "rock": case "stone": return StampKind.Rock;
                case "chip": case "bark": return StampKind.Chip;
                case "butt": case "cigarette": return StampKind.Butt;
                case "paper": case "wrapper": return StampKind.Paper;
                case "cap": case "bottlecap": return StampKind.Cap;
                case "splat": case "dropping": return StampKind.Splat;
                case "paw": return StampKind.Paw;
                case "boot": return StampKind.Boot;
                case "petal": return StampKind.Petal;
                default: return StampKind.Leaf;
            }
        }

        private static DecalInput In(DecalKind k, JsonValue v)
        {
            if (v.Kind == JsonKind.Number) return DecalInput.Of((float)v.AsNumber());
            string s = v.AsString("");
            if (s.Length == 0) return DecalInput.Of(1f);
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float c)) return DecalInput.Of(c);
            int i = k.FieldIndex(s, false);
            if (i < 0) throw new FormatException("unknown field '" + s + "' (fields are made by an earlier op's \"out\")");
            return new DecalInput { Field = i };
        }

        private static DecalParam P(JsonValue v, float def)
        {
            if (v == null || v.IsNull) return DecalParam.Const(def);
            if (v.Kind == JsonKind.Number) return DecalParam.Const((float)v.AsNumber());
            if (v.IsObject)
            {
                foreach (var (key, by) in new[] { ("wet", (byte)1), ("age", (byte)2), ("strength", (byte)3) })
                {
                    var r = v[key];
                    if (r.IsArray && r.Count >= 2) return new DecalParam { A = (float)r.Items[0].AsNumber(), B = (float)r.Items[1].AsNumber(), By = by };
                }
            }
            return DecalParam.Const(def);
        }

        private static float2 V2(JsonValue v, float2 def) =>
            v.IsArray && v.Count >= 2 ? new float2((float)v.Items[0].AsNumber(), (float)v.Items[1].AsNumber()) : v.Kind == JsonKind.Number ? new float2((float)v.AsNumber()) : def;

        /// <summary>A colour as [r, g, b] in 0..255 (sRGB).</summary>
        private static float3 Rgb(JsonValue v, float3 def) =>
            v.IsArray && v.Count >= 3 ? new float3((float)v.Items[0].AsNumber(), (float)v.Items[1].AsNumber(), (float)v.Items[2].AsNumber()) / 255f : def;

        private static float3[] Palette(JsonValue v)
        {
            var list = new List<float3>();
            foreach (var c in v.Items) if (c.IsArray && c.Count >= 3) list.Add(Rgb(c, default));
            return list.ToArray();
        }
    }
}
