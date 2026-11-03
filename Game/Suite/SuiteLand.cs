// Game runner: a scenario's "land" (test land made to order, Core/Suite/SuitePlot) read from its JSON. The keys and
// their values are in suite/schema.json ("land"); a heightmap PNG is read here into metres.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Placemaker.Core.Suite;
using Unity.Mathematics;
using UnityEngine;

namespace Placemaker.Game.Suite
{
    internal static class SuiteLand
    {
        /// <summary>A plot from a scenario's "land"; null with <paramref name="error"/> when it cannot be made.</summary>
        public static SuitePlot Parse(JObject land, out string error)
        {
            error = null;
            var p = new SuitePlot();
            try
            {
                p.Shape = ((string)land["shape"] ?? "rect").ToLowerInvariant();
                if (p.Shape == "square" || p.Shape == "rectangle") p.Shape = "rect";
                var size = land["size"];
                if (size is JArray a && a.Count >= 2) p.Size = new float2((float)a[0], (float)a[1]);
                else if (size != null && size.Type != JTokenType.Null) p.Size = new float2((float)size, (float)size);
                if (land["points"] is JArray pts)
                {
                    p.Shape = "points";
                    p.Points = pts.OfType<JArray>().Select(q => new float2((float)q[0], (float)q[1])).ToArray();
                    if (p.Points.Length < 3) { error = "land: points needs at least 3 corners"; return null; }
                    p.SizeFromPoints();
                }
                if (land["neck"] != null) p.Neck = (float)land["neck"];
                p.Ground = ((string)land["ground"] ?? "flat").ToLowerInvariant();
                if (land["grade"] != null) { float g = (float)land["grade"]; p.Grade = g > 1f ? g / 100f : g; }   // 6 or 0.06
                p.Toward = ((string)land["toward"] ?? "north").ToLowerInvariant();
                if (land["amplitude"] != null) p.Amplitude = (float)land["amplitude"];
                if (land["wavelength"] != null) p.Wavelength = (float)land["wavelength"];
                if (land["heights"] is JArray rows)
                {
                    p.Ground = "heights";
                    p.Heights = rows.OfType<JArray>().Select(r => r.Select(v => (float)v).ToArray()).ToArray();
                }
                if (land["heightmap"] != null)
                {
                    p.Ground = "heights";
                    p.Heights = ReadPng((string)land["heightmap"], (float?)land["range"] ?? 10f, out error);
                    if (error != null) return null;
                }
                // (rf-beach) a lake beside the land: "water": "north", or { side, width, depth, freeboard, bank (grade, % or m/m), shore }
                var water = land["water"];
                if (water != null && water.Type == JTokenType.String) p.Water = ((string)water).ToLowerInvariant();
                else if (water is JObject w)
                {
                    p.Water = ((string)w["side"] ?? "north").ToLowerInvariant();
                    if (w["width"] != null) p.WaterWidth = math.max(20f, (float)w["width"]);
                    if (w["depth"] != null) p.WaterDepth = math.max(0.5f, (float)w["depth"]);
                    if (w["freeboard"] != null) p.Freeboard = math.max(0.3f, (float)w["freeboard"]);
                    if (w["bank"] != null) { float b = (float)w["bank"]; p.BankGrade = b > 3f ? b / 100f : b; }
                    if (w["shore"] != null) p.ShoreIn = (float)w["shore"];
                }
                if (p.Water == "none" || p.Water == "false") p.Water = "";
                if (p.HasWater && p.Water != "north" && p.Water != "south" && p.Water != "east" && p.Water != "west") { error = "land: water side must be north, south, east or west, not " + p.Water; return null; }
                string roads = ((string)land["roads"] ?? "around").ToLowerInvariant();
                p.RoadsAround = roads != "none";
                p.RoadsFollow = roads == "follow";
                if (land["road"] != null) p.Road = (string)land["road"];
                if (land["gap"] != null) p.Gap = math.max(4f, (float)land["gap"]);
                // (area tools) extra roads and paths: [{"prefab": "Small Road", "points": [[dx, dz], ...]}], relative to the land's centre
                if (land["nets"] is JArray nets)
                    foreach (var n in nets.OfType<JObject>())
                    {
                        var q = (n["points"] as JArray)?.OfType<JArray>().Select(v => new float2((float)v[0], (float)v[1])).ToArray();
                        if (q == null || q.Length < 2) { error = "land: every net needs \"points\" with at least 2 points"; return null; }
                        p.Nets.Add(((string)n["prefab"] ?? SuiteLayout.SmallRoad, q));
                    }
                if (p.Size.x < 4f || p.Size.y < 4f || p.Size.x > 4000f || p.Size.y > 4000f) { error = $"land: size {p.Size.x} x {p.Size.y} m is outside 4..4000 m"; return null; }
                return p;
            }
            catch (Exception e) { error = "land: " + e.Message; return null; }
        }

        /// <summary>A greyscale PNG as metres (black 0, white = range), rows from the image's top (north), at most 256 x 256.</summary>
        private static float[][] ReadPng(string path, float range, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) { error = "land: heightmap not found: " + path; return null; }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!tex.LoadImage(File.ReadAllBytes(path))) { error = "land: heightmap is not a PNG or JPG: " + path; return null; }
                int w = tex.width, h = tex.height, cols = math.min(256, w), rows = math.min(256, h);
                var px = tex.GetPixels32();
                var r = new float[rows][];
                for (int j = 0; j < rows; j++)
                {
                    r[j] = new float[cols];
                    int y = h - 1 - (int)((j + 0.5f) * h / rows);   // texture rows start at the bottom
                    for (int i = 0; i < cols; i++)
                    {
                        var c = px[y * w + (int)((i + 0.5f) * w / cols)];
                        r[j][i] = (c.r + c.g + c.b) / (3f * 255f) * range;
                    }
                }
                return r;
            }
            finally { UnityEngine.Object.Destroy(tex); }
        }
    }
}
