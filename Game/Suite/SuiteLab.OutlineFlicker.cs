// Game runner lab: do the game's blue outlines flicker while the camera moves (the user, 21 Oct: "all blue outlines
// flicker when you pan and hover over something")? A house near the spot is lit as a hovered one is (Highlighted, with its
// pieces), the camera pans a little every frame, and every frame is grabbed from the screen and its outline-coloured
// pixels counted: a steady outline is a steady count, a flickering one drops out frame by frame.
//   {"do":"lab","op":"outlineFlicker","frames":90,"pan":0.5,"label":"real-preview"}
// "at": [dx, dz] from the block's centre ("abs": true: a point of the map) (default: the build's outline centre, else the block's); "keep": true leaves
// the house lit. The record has every frame's count, the dropouts (under half the median) and the first frames as PNGs.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteRunner
    {
        private IEnumerator LabOutlineFlicker(ScenarioState st, JObject step, JObject rec)
        {
            var em = _world.EntityManager;
            int frames = (int?)step["frames"] ?? 90;
            float pan = (float?)step["pan"] ?? 0.5f;
            string label = (string)step["label"] ?? "outline";
            var b = st.S.Block;
            float2 at = st.Ring != null && st.Ring.Length >= 3 ? st.Ring.Aggregate(float2.zero, (s, p) => s + p) / st.Ring.Length : b.Center;
            if (step["at"] is JArray a2 && a2.Count == 2) at = ((bool?)step["abs"] == true ? float2.zero : b.Center) + new float2((float)a2[0], (float)a2[1]);

            // the house nearest the spot, lit with everything it owns
            Entity house = Entity.Null;
            float best = float.MaxValue;
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<global::Game.Buildings.Building>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    float d = math.distance(em.GetComponentData<global::Game.Objects.Transform>(e).m_Position.xz, at);
                    if (d < best) { best = d; house = e; }
                }
            if (house == Entity.Null) { rec["error"] = "lab: no building near the spot"; yield break; }
            var lit = new List<Entity> { house };
            if (em.HasBuffer<global::Game.Objects.SubObject>(house))
                foreach (var so in em.GetBuffer<global::Game.Objects.SubObject>(house, true)) if (em.Exists(so.m_SubObject)) lit.Add(so.m_SubObject);
            foreach (var e in lit) { em.AddComponent<Highlighted>(e); em.AddComponent<BatchesUpdated>(e); }
            var pos = em.GetComponentData<global::Game.Objects.Transform>(house).m_Position;
            rec["house"] = $"{house.Index}:{house.Version}";
            rec["lit"] = lit.Count;

            // the hovered colour as it reaches the screen (sRGB, 0-255)
            Color32 outline = new Color32(110, 190, 255, 255);
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<RenderingSettingsData>()))
                if (!q.IsEmptyIgnoreFilter) { var c = q.GetSingleton<RenderingSettingsData>().m_HoveredColor; outline = (Color32)new Color(c.r, c.g, c.b, 1f); }
            rec["outlineColour"] = new JArray(outline.r, outline.g, outline.b);

            SuiteCapture.Aim(pos, 25f, 45f, 70f);
            yield return Frames(30);
            var counts = new List<int>();
            var dir = 1f;
            for (int f = 0; f < frames; f++)
            {
                // the camera pans a little every frame (back and forth, so the house stays in view)
                if (f % 30 == 0 && f > 0) dir = -dir;
                SuiteCapture.Aim(pos + new float3(dir * pan * (f % 30), 0f, 0f), 25f, 45f, 70f);
                yield return new WaitForEndOfFrame();
                var tex = SuiteCapture.GrabTexture2D(out var err);
                if (tex == null) { rec["error"] = "lab: no frame: " + err; break; }
                try
                {
                    var px = tex.GetPixels32();
                    int w = tex.width, h = tex.height, n = 0;
                    // the middle of the screen (the panel and the sky left out)
                    for (int y = h / 5; y < h * 4 / 5; y += 2)
                        for (int x = w * 3 / 10; x < w * 8 / 10; x += 2)
                        {
                            var c = px[y * w + x];
                            int dr = c.r - outline.r, dg = c.g - outline.g, db = c.b - outline.b;
                            if (dr * dr + dg * dg + db * db < 45 * 45) n++;
                        }
                    counts.Add(n);
                    if (f < 4 || f == frames / 2)
                    {
                        var file = Path.Combine(_shots, $"{st.FileIndex + 1:D2}-{Safe(st.S.Id)}.flicker-{Safe(label)}-{f:D3}.png");
                        File.WriteAllBytes(file, tex.EncodeToPNG());
                    }
                }
                finally { UnityEngine.Object.Destroy(tex); }
            }
            if ((bool?)step["keep"] != true)
                foreach (var e in lit) if (em.Exists(e) && em.HasComponent<Highlighted>(e)) { em.RemoveComponent<Highlighted>(e); em.AddComponent<BatchesUpdated>(e); }
            if (counts.Count == 0) yield break;
            var sorted = counts.OrderBy(x => x).ToList();
            int median = sorted[sorted.Count / 2];
            int dropouts = counts.Count(x => x < median / 2);
            int changes = 0;
            for (int i = 1; i < counts.Count; i++) if (Math.Abs(counts[i] - counts[i - 1]) > Math.Max(20, median / 3)) changes++;
            rec["frames"] = counts.Count;
            rec["median"] = median;
            rec["min"] = sorted[0];
            rec["max"] = sorted[sorted.Count - 1];
            rec["dropouts"] = dropouts;
            rec["jumps"] = changes;
            rec["counts"] = new JArray(counts);
            Mod.Log.Info($"Placemaker lab: outline flicker ({label}): house {house.Index} lit with {lit.Count - 1} pieces, {counts.Count} frames panning {pan} m a frame; outline pixels median {median}, min {sorted[0]}, max {sorted[sorted.Count - 1]}, "
                + $"{dropouts} frames under half the median, {changes} jumps frame to frame; counts {string.Join(",", counts)}");
        }
    }
}
