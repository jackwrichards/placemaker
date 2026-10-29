// Placemaker — dynamic decals: strokes as bytes on the group record (docs/DYNAMIC-DECALS.md). GPL-3.0.
//
// Version first (RESEARCH-NOTES rule 5): the game's serializer writes our buffer of bytes as it is and gives it back on
// load; this codec reads exactly what its version wrote and rejects anything it does not understand (the decals are
// then simply not drawn; nothing else depends on them). Without Placemaker the game skips the whole component type and
// the city keeps no trace (docs/research/dynamic-decals.md, "Saves and mod removal").
//
// Strings are [u16 length][utf8].
// v1: [u8 version][u16 strokes] per stroke: [str kind][u32 seed][u8 shape][f32 strength][u8 wet][u8 age][u8 layers][i8 order][f32 y]
//     box: [f32 cx][f32 cz][f32 yaw][f32 sx][f32 sz]; path: [u16 n] n x [f32 x][f32 z][f16-ish u16 width cm]; outline: [u16 n] n x [f32 x][f32 z]
//     then [u16 kinds] per kind: [str json] (kinds a plan defined itself, not in the library)

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Mathematics;

namespace Placemaker.Core.Decals.Dynamic
{
    public sealed class DecalSet
    {
        public readonly List<DecalStroke> Strokes = new List<DecalStroke>();
        /// <summary>Kinds the plan carries itself (JSON), for strokes whose kind is not in the library.</summary>
        public readonly List<string> Kinds = new List<string>();
    }

    public static class DecalCodec
    {
        public const byte CurrentVersion = 1;
        public const int MaxStrokes = 4000, MaxPoints = 4096, MaxKindBytes = 32768;

        public static byte[] Encode(DecalSet set)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write(CurrentVersion);
                int n = Math.Min(MaxStrokes, set?.Strokes.Count ?? 0);
                w.Write((ushort)n);
                for (int i = 0; i < n; i++)
                {
                    var s = set.Strokes[i];
                    Str(w, s.Kind);
                    w.Write(s.Seed);
                    w.Write((byte)s.Shape);
                    w.Write(s.Strength);
                    w.Write((byte)math.clamp((int)math.round(s.Wetness * 255f), 0, 255));
                    w.Write((byte)math.clamp((int)math.round(s.Age * 255f), 0, 255));
                    w.Write(s.Layers);
                    w.Write(s.Order);
                    w.Write(s.Y);
                    switch (s.Shape)
                    {
                        case StrokeShape.Box:
                            w.Write(s.Center.x); w.Write(s.Center.y); w.Write(s.Yaw); w.Write(s.Size.x); w.Write(s.Size.y);
                            break;
                        case StrokeShape.Path:
                        {
                            int m = Math.Min(MaxPoints, s.Points.Length);
                            w.Write((ushort)m);
                            for (int k = 0; k < m; k++)
                            {
                                w.Write(s.Points[k].x); w.Write(s.Points[k].y);
                                float wd = k < s.Widths.Length ? s.Widths[k] : (s.Widths.Length > 0 ? s.Widths[s.Widths.Length - 1] : 1f);
                                w.Write((ushort)math.clamp((int)math.round(wd * 100f), 0, 65535));
                            }
                            break;
                        }
                        default:
                        {
                            int m = Math.Min(MaxPoints, s.Points.Length);
                            w.Write((ushort)m);
                            for (int k = 0; k < m; k++) { w.Write(s.Points[k].x); w.Write(s.Points[k].y); }
                            break;
                        }
                    }
                }
                int nk = Math.Min(64, set?.Kinds.Count ?? 0);
                w.Write((ushort)nk);
                for (int i = 0; i < nk; i++) Str(w, set.Kinds[i], MaxKindBytes);
                w.Flush();
                return ms.ToArray();
            }
        }

        /// <summary>Null when the bytes are not a set this version can read.</summary>
        public static DecalSet Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 3) return null;
            try
            {
                using (var ms = new MemoryStream(bytes))
                using (var r = new BinaryReader(ms, Encoding.UTF8))
                {
                    byte version = r.ReadByte();
                    if (version < 1 || version > CurrentVersion) return null;
                    var set = new DecalSet();
                    int n = r.ReadUInt16();
                    if (n > MaxStrokes) return null;
                    for (int i = 0; i < n; i++)
                    {
                        var s = new DecalStroke { Kind = ReadStr(r) };
                        s.Seed = r.ReadUInt32();
                        byte shape = r.ReadByte();
                        if (shape > (byte)StrokeShape.Outline) return null;
                        s.Shape = (StrokeShape)shape;
                        s.Strength = r.ReadSingle();
                        s.Wetness = r.ReadByte() / 255f;
                        s.Age = r.ReadByte() / 255f;
                        s.Layers = r.ReadByte();
                        s.Order = r.ReadSByte();
                        s.Y = r.ReadSingle();
                        switch (s.Shape)
                        {
                            case StrokeShape.Box:
                                s.Center = new float2(r.ReadSingle(), r.ReadSingle()); s.Yaw = r.ReadSingle(); s.Size = new float2(r.ReadSingle(), r.ReadSingle());
                                break;
                            case StrokeShape.Path:
                            {
                                int m = r.ReadUInt16();
                                if (m > MaxPoints) return null;
                                s.Points = new float2[m]; s.Widths = new float[m];
                                for (int k = 0; k < m; k++) { s.Points[k] = new float2(r.ReadSingle(), r.ReadSingle()); s.Widths[k] = r.ReadUInt16() / 100f; }
                                break;
                            }
                            default:
                            {
                                int m = r.ReadUInt16();
                                if (m > MaxPoints) return null;
                                s.Points = new float2[m];
                                for (int k = 0; k < m; k++) s.Points[k] = new float2(r.ReadSingle(), r.ReadSingle());
                                break;
                            }
                        }
                        if (!Finite(s)) return null;
                        set.Strokes.Add(s);
                    }
                    int nk = r.ReadUInt16();
                    if (nk > 64) return null;
                    for (int i = 0; i < nk; i++) set.Kinds.Add(ReadStr(r));
                    return ms.Position == ms.Length ? set : null;
                }
            }
            catch (Exception) { return null; }
        }

        private static bool Finite(DecalStroke s)
        {
            if (!math.isfinite(s.Strength) || !math.isfinite(s.Y) || !math.all(math.isfinite(s.Center)) || !math.isfinite(s.Yaw) || !math.all(math.isfinite(s.Size))) return false;
            foreach (var p in s.Points) if (!math.all(math.isfinite(p))) return false;
            return true;
        }

        private static void Str(BinaryWriter w, string s, int max = 255)
        {
            var b = Encoding.UTF8.GetBytes(s ?? "");
            int n = Math.Min(b.Length, Math.Min(max, 65535));
            w.Write((ushort)n);
            w.Write(b, 0, n);
        }

        private static string ReadStr(BinaryReader r)
        {
            int n = r.ReadUInt16();
            if (n > MaxKindBytes) throw new InvalidDataException("string too long");
            var b = r.ReadBytes(n);
            if (b.Length != n) throw new EndOfStreamException("cut string");
            return Encoding.UTF8.GetString(b);
        }
    }
}
