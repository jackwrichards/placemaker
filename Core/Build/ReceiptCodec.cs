// Placemaker — binary codec for GroupRecord (GPL-3.0).
// Version-first layout in the spirit of ParkingLotTool's ParkingLotBuildReceipt (KruemelGames,
// GPL-3.0): the CS2 serializer has no per-component length, so our bytes go into one
// length-prefixed buffer and carry their own version. Readers stop after the fields they know,
// so a save written by a newer Placemaker still loads in an older one (extra bytes ignored).

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Mathematics;

namespace Placemaker.Core.Build
{
    public static class ReceiptCodec
    {
        /// <summary>"PMGR" little-endian.</summary>
        public const uint Magic = 0x52474D50;
        /// <summary>Bump when appending fields. v1 = initial layout; v2 (s17) = park stats block after the area block.</summary>
        public const int CurrentVersion = 3;

        // Corrupt data must never make us allocate gigabytes or recurse forever.
        private const int MaxCount = 1_000_000;
        private const int MaxStringBytes = 1 << 20;
        private const int MaxStepDepth = 32;

        // Settings value tags (save format).
        private const byte TagNull = 0, TagDouble = 1, TagBool = 2, TagString = 3, TagStringArray = 4;

        public static byte[] Encode(GroupRecord r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, new UTF8Encoding(false)))
            {
                w.Write(Magic);
                w.Write(CurrentVersion);
                w.Write(r.GroupId);
                Str(w, r.RecipeId);
                Str(w, r.RecipeNameKey);
                Str(w, r.CustomName);
                w.Write((int)r.Flags);
                w.Write(r.BuiltAtTicks);
                w.Write(r.Cost);
                w.Write(r.Upkeep);
                w.Write(r.Stats?.Count ?? 0);
                if (r.Stats != null)
                    foreach (var s in r.Stats) { Str(w, s.Key); w.Write(s.Value); Str(w, s.Unit); }
                w.Write(r.Facts?.Count ?? 0);
                if (r.Facts != null)
                    foreach (var kv in r.Facts) { Str(w, kv.Key); w.Write(kv.Value); }
                w.Write(r.PrefabNames?.Count ?? 0);
                if (r.PrefabNames != null) foreach (var n in r.PrefabNames) Str(w, n);

                // Recipe and area as separately framed blocks: a block that fails to decode later
                // (e.g. a layer changed meaning) loses only the receipt, never the group itself.
                Block(w, bw => { bw.Write(r.Recipe != null); if (r.Recipe != null) WriteRecipe(bw, r.Recipe); });
                Block(w, bw => { bw.Write(r.Area != null); if (r.Area != null) WriteArea(bw, r.Area); });
                // v2: park stats (settings + applied values). The recipe's own copy of the settings is not in the recipe
                // block (older readers would choke on it); Decode puts it back on the recipe for Edit.
                var parkStats = r.ParkStats ?? (r.Recipe?.ParkStats != null && !r.Recipe.ParkStats.IsDefault
                    ? new ParkStatsRecord { Settings = r.Recipe.ParkStats.Clone() } : null);
                Block(w, bw => { bw.Write(parkStats != null); if (parkStats != null) WriteParkStats(bw, parkStats); });
                // v3 (P2-1): the hand edits of every step, by step path, in a block of their own (older readers stop before it)
                Block(w, bw => WriteTouches(bw, r.Recipe?.Root));
                w.Flush();
                return ms.ToArray();
            }
        }

        /// <summary>Decodes a record. Returns null for unreadable data (never throws).</summary>
        public static GroupRecord Decode(byte[] data)
        {
            if (data == null || data.Length < 8) return null;
            try
            {
                using (var ms = new MemoryStream(data, false))
                using (var rd = new BinaryReader(ms, new UTF8Encoding(false)))
                {
                    if (rd.ReadUInt32() != Magic) return null;
                    int version = rd.ReadInt32();
                    if (version < 1) return null;
                    var r = new GroupRecord
                    {
                        GroupId = rd.ReadInt32(),
                        RecipeId = Str(rd),
                        RecipeNameKey = Str(rd),
                        CustomName = Str(rd),
                        Flags = (GroupFlags)rd.ReadInt32(),
                        BuiltAtTicks = rd.ReadInt64(),
                        Cost = rd.ReadDouble(),
                        Upkeep = rd.ReadDouble(),
                    };
                    int n = Count(rd);
                    for (int i = 0; i < n; i++) r.Stats.Add(new PlanStat(Str(rd), rd.ReadDouble(), Str(rd)));
                    n = Count(rd);
                    for (int i = 0; i < n; i++) { var k = Str(rd); r.Facts[k] = rd.ReadDouble(); }
                    n = Count(rd);
                    for (int i = 0; i < n; i++) r.PrefabNames.Add(Str(rd));

                    r.Recipe = ReadBlock(rd, br => br.ReadBoolean() ? ReadRecipe(br) : null);
                    r.Area = ReadBlock(rd, br => br.ReadBoolean() ? ReadArea(br) : null);
                    if (version >= 2 && ms.Position < ms.Length)
                    {
                        r.ParkStats = ReadBlock(rd, br => br.ReadBoolean() ? ReadParkStats(br) : null);
                        if (r.ParkStats?.Settings != null && r.Recipe != null) r.Recipe.ParkStats = r.ParkStats.Settings.Clone();
                    }
                    if (version >= 3 && ms.Position < ms.Length)
                        ReadBlock(rd, br => { ReadTouches(br, r.Recipe?.Root); return (object)null; });
                    // version >= 4 fields would be read here, guarded by `version`.
                    return r;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ───────────── hand edits (v3, Core/Touches.cs) ─────────────

        private static void StepsWithTouches(RecipeStep s, string path, int depth, List<KeyValuePair<string, RecipeStep>> into)
        {
            if (s == null || depth > MaxStepDepth) return;
            if (s.Touches != null && s.Touches.Count > 0) into.Add(new KeyValuePair<string, RecipeStep>(path, s));
            for (int i = 0; i < (s.Children?.Count ?? 0); i++) StepsWithTouches(s.Children[i], path + "/" + i, depth + 1, into);
            if (s.RegionOverrides != null)
            {
                var keys = new List<string>(s.RegionOverrides.Keys);
                keys.Sort(StringComparer.Ordinal);
                foreach (var k in keys) StepsWithTouches(s.RegionOverrides[k], path + "/@" + k, depth + 1, into);
            }
        }

        private static RecipeStep StepAt(RecipeStep root, string path)
        {
            if (root == null || string.IsNullOrEmpty(path)) return null;
            var s = root;
            var parts = path.Split('/');
            for (int i = 1; i < parts.Length && s != null; i++)
            {
                var p = parts[i];
                if (p.StartsWith("@", StringComparison.Ordinal)) s = s.RegionOverrides != null && s.RegionOverrides.TryGetValue(p.Substring(1), out var o) ? o : null;
                else if (int.TryParse(p, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int idx)) s = idx >= 0 && idx < s.Children.Count ? s.Children[idx] : null;
                else s = null;
            }
            return s;
        }

        private static void WriteTouches(BinaryWriter w, RecipeStep root)
        {
            var steps = new List<KeyValuePair<string, RecipeStep>>();
            StepsWithTouches(root, "r", 0, steps);
            w.Write(steps.Count);
            foreach (var kv in steps)
            {
                Str(w, kv.Key);
                w.Write(kv.Value.Touches.Count);
                foreach (var t in kv.Value.Touches) { w.Write(t.Version); Str(w, t.Kind); F3(w, t.Position); w.Write(t.Yaw); Str(w, t.Arg ?? ""); }
            }
        }

        private static void ReadTouches(BinaryReader rd, RecipeStep root)
        {
            int n = Count(rd);
            for (int i = 0; i < n; i++)
            {
                var step = StepAt(root, Str(rd));
                int m = Count(rd);
                for (int k = 0; k < m; k++)
                {
                    int v = rd.ReadInt32();
                    var t = new HandTouch(Str(rd), F3(rd), rd.ReadSingle(), Str(rd)) { Version = v };
                    if (step != null && !string.IsNullOrEmpty(t.Kind)) step.Touches.Add(t);
                }
            }
        }

        // ───────────── park stats (v2) ─────────────

        private static void WriteParkStats(BinaryWriter w, ParkStatsRecord p)
        {
            var st = p.Settings ?? new ParkStatsSettings();
            w.Write(st.KeepVanilla);
            w.Write(st.Overrides.Count);
            foreach (var kv in st.Overrides) { Str(w, kv.Key); w.Write(kv.Value); }
            w.Write(p.Applied?.Count ?? 0);
            if (p.Applied != null) foreach (var kv in p.Applied) { Str(w, kv.Key); w.Write(kv.Value); }
            w.Write(p.Kind);
            w.Write(p.LeisureType);
        }

        private static ParkStatsRecord ReadParkStats(BinaryReader rd)
        {
            var p = new ParkStatsRecord { Settings = new ParkStatsSettings { KeepVanilla = rd.ReadBoolean() } };
            int n = Count(rd);
            for (int i = 0; i < n; i++) { var k = Str(rd) ?? ""; p.Settings.Overrides[k] = rd.ReadDouble(); }
            n = Count(rd);
            for (int i = 0; i < n; i++) { var k = Str(rd) ?? ""; p.Applied[k] = rd.ReadDouble(); }
            p.Kind = rd.ReadInt32();
            p.LeisureType = rd.ReadInt32();
            return p;
        }

        // ───────────── recipe ─────────────

        private static void WriteRecipe(BinaryWriter w, Recipe r)
        {
            Str(w, r.Id); Str(w, r.NameKey); Str(w, r.Name); Str(w, r.Icon);
            w.Write(r.FormatVersion);
            WriteStep(w, r.Root ?? new RecipeStep(), 0);
        }

        private static Recipe ReadRecipe(BinaryReader rd)
        {
            var r = new Recipe { Id = Str(rd), NameKey = Str(rd), Name = Str(rd), Icon = Str(rd) };
            r.FormatVersion = rd.ReadInt32();
            r.Root = ReadStep(rd, 0);
            Layers.Common.LayerAliases.Normalize(r.Root);   // retired layer ids → canonical (g5)
            return r;
        }

        private static void WriteStep(BinaryWriter w, RecipeStep s, int depth)
        {
            if (depth > MaxStepDepth) throw new InvalidDataException("recipe too deep");
            Str(w, s.Layer);
            WriteSettings(w, s.Settings ?? new SettingsBag());
            var locked = new List<string>(s.Locked ?? new HashSet<string>());
            locked.Sort(StringComparer.Ordinal);   // deterministic bytes for identical recipes
            w.Write(locked.Count);
            foreach (var l in locked) Str(w, l);
            w.Write(s.Seed);
            Str(w, s.Target);
            w.Write(s.Enabled);
            w.Write(s.Markers?.Count ?? 0);
            if (s.Markers != null)
                foreach (var m in s.Markers) { Str(w, m.Kind); w.Write(m.RingSegment); w.Write(m.RingT); F3(w, m.Position); }
            w.Write(s.Children?.Count ?? 0);
            if (s.Children != null) foreach (var c in s.Children) WriteStep(w, c, depth + 1);
            var keys = new List<string>(s.RegionOverrides?.Keys ?? (IEnumerable<string>)Array.Empty<string>());
            keys.Sort(StringComparer.Ordinal);
            w.Write(keys.Count);
            foreach (var k in keys) { Str(w, k); WriteStep(w, s.RegionOverrides[k], depth + 1); }
        }

        private static RecipeStep ReadStep(BinaryReader rd, int depth)
        {
            if (depth > MaxStepDepth) throw new InvalidDataException("recipe too deep");
            var s = new RecipeStep { Layer = Str(rd), Settings = ReadSettings(rd) };
            int n = Count(rd);
            for (int i = 0; i < n; i++) s.Locked.Add(Str(rd));
            s.Seed = rd.ReadUInt32();
            s.Target = Str(rd);
            s.Enabled = rd.ReadBoolean();
            n = Count(rd);
            for (int i = 0; i < n; i++)
                s.Markers.Add(new EdgeMarker { Kind = Str(rd), RingSegment = rd.ReadInt32(), RingT = rd.ReadSingle(), Position = F3(rd) });
            n = Count(rd);
            for (int i = 0; i < n; i++) s.Children.Add(ReadStep(rd, depth + 1));
            n = Count(rd);
            for (int i = 0; i < n; i++) { var k = Str(rd); s.RegionOverrides[k] = ReadStep(rd, depth + 1); }
            return s;
        }

        private static void WriteSettings(BinaryWriter w, SettingsBag bag)
        {
            var keys = new List<string>(bag.Values.Keys);
            keys.Sort(StringComparer.Ordinal);
            w.Write(keys.Count);
            foreach (var k in keys)
            {
                Str(w, k);
                var v = bag.Values[k];
                switch (v)
                {
                    case null: w.Write(TagNull); break;
                    case bool b: w.Write(TagBool); w.Write(b); break;
                    case string s: w.Write(TagString); Str(w, s); break;
                    case string[] a:
                        w.Write(TagStringArray); w.Write(a.Length);
                        foreach (var e in a) Str(w, e);
                        break;
                    case IConvertible c:
                        // Every numeric type collapses to double, matching SettingsBag.Num.
                        w.Write(TagDouble); w.Write(c.ToDouble(System.Globalization.CultureInfo.InvariantCulture));
                        break;
                    default: w.Write(TagNull); break;
                }
            }
        }

        private static SettingsBag ReadSettings(BinaryReader rd)
        {
            var bag = new SettingsBag();
            int n = Count(rd);
            for (int i = 0; i < n; i++)
            {
                var k = Str(rd);
                byte tag = rd.ReadByte();
                switch (tag)
                {
                    case TagNull: bag.Values[k] = null; break;
                    case TagDouble: bag.Values[k] = rd.ReadDouble(); break;
                    case TagBool: bag.Values[k] = rd.ReadBoolean(); break;
                    case TagString: bag.Values[k] = Str(rd); break;
                    case TagStringArray:
                        int m = Count(rd);
                        var a = new string[m];
                        for (int j = 0; j < m; j++) a[j] = Str(rd);
                        bag.Values[k] = a;
                        break;
                    default: throw new InvalidDataException("unknown settings tag " + tag);
                }
            }
            return bag;
        }

        // ───────────── area ─────────────

        private static void WriteArea(BinaryWriter w, AreaShape a)
        {
            var ring = a.Ring ?? Array.Empty<float3>();
            w.Write(ring.Length);
            foreach (var p in ring) F3(w, p);
            w.Write((int)a.Source);
            w.Write(a.Attachments?.Count ?? 0);
            if (a.Attachments != null)
                foreach (var t in a.Attachments)
                {
                    w.Write((int)t.Kind); w.Write(t.ExternalId); w.Write(t.ExternalT);
                    F3(w, t.Position); F3(w, t.Direction); w.Write(t.RoadWidth);
                    w.Write(t.RingSegment); w.Write(t.RingT);
                }
        }

        private static AreaShape ReadArea(BinaryReader rd)
        {
            var a = new AreaShape();
            int n = Count(rd);
            a.Ring = new float3[n];
            for (int i = 0; i < n; i++) a.Ring[i] = F3(rd);
            a.Source = (AreaSource)rd.ReadInt32();
            n = Count(rd);
            for (int i = 0; i < n; i++)
                a.Attachments.Add(new BoundaryAttachment
                {
                    Kind = (AttachmentKind)rd.ReadInt32(), ExternalId = rd.ReadInt64(), ExternalT = rd.ReadSingle(),
                    Position = F3(rd), Direction = F3(rd), RoadWidth = rd.ReadSingle(),
                    RingSegment = rd.ReadInt32(), RingT = rd.ReadSingle(),
                });
            return a;
        }

        // ───────────── primitives ─────────────

        private static void Block(BinaryWriter w, Action<BinaryWriter> body)
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms, new UTF8Encoding(false)))
            {
                body(bw);
                bw.Flush();
                w.Write((int)ms.Length);
                w.Write(ms.GetBuffer(), 0, (int)ms.Length);
            }
        }

        private static T ReadBlock<T>(BinaryReader rd, Func<BinaryReader, T> body) where T : class
        {
            int len = rd.ReadInt32();
            if (len < 0 || len > rd.BaseStream.Length - rd.BaseStream.Position) throw new InvalidDataException("bad block");
            var bytes = rd.ReadBytes(len);
            try
            {
                using (var ms = new MemoryStream(bytes, false))
                using (var br = new BinaryReader(ms, new UTF8Encoding(false)))
                    return body(br);
            }
            catch (Exception)
            {
                return null;   // a broken receipt block degrades to "no receipt"
            }
        }

        private static void Str(BinaryWriter w, string s)
        {
            if (s == null) { w.Write(-1); return; }
            var b = Encoding.UTF8.GetBytes(s);
            w.Write(b.Length);
            w.Write(b);
        }

        private static string Str(BinaryReader rd)
        {
            int len = rd.ReadInt32();
            if (len < 0) return "";
            if (len > MaxStringBytes) throw new InvalidDataException("string too long");
            var b = rd.ReadBytes(len);
            if (b.Length != len) throw new EndOfStreamException();
            return Encoding.UTF8.GetString(b);
        }

        private static int Count(BinaryReader rd)
        {
            int n = rd.ReadInt32();
            if (n < 0 || n > MaxCount) throw new InvalidDataException("bad count");
            return n;
        }

        private static void F3(BinaryWriter w, float3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
        private static float3 F3(BinaryReader rd) => new float3(rd.ReadSingle(), rd.ReadSingle(), rd.ReadSingle());
    }
}
