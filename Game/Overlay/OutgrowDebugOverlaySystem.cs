// Placemaker — Outgrow's gardens drawn on the city (Options: "Show Outgrow garden outlines"), for checking what Outgrow
// decided without guessing from the hedges (the user, 24 Oct: "is there a debug mode ... that just draws the edges of a
// garden?"). Each outgrown building's garden as its last plan wrote it (PolishGarden), in a colour of its own, a dot at every
// corner, its edges shorter than a short fence piece (2 m) red; the building's own lot thin white. Near the camera, the
// gardens read again every half second; always on top of the tool in use (Placemaker's or none).

using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Rendering;
using Game.Tools;
using Placemaker.Game.Neighbours;
using Placemaker.Game.Tool;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Scripting;
using Color = UnityEngine.Color;
using Transform = Game.Objects.Transform;

namespace Placemaker.Game.Overlay
{
    public sealed partial class OutgrowDebugOverlaySystem : GameSystemBase
    {
        private static readonly Color LotColour = new Color(1f, 1f, 1f, 0.75f);
        private static readonly Color ShortColour = new Color(1f, 0.1f, 0.1f, 1f);

        private OverlayRenderSystem _overlay;
        private EntityQuery _gardens;
        private readonly List<(float3[] Garden, float3[] Lot, Color Colour)> _shown = new List<(float3[], float3[], Color)>();
        private float _readAt = -1f;
        private int _errors;
        private int _logged = -1;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _overlay = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            _gardens = GetEntityQuery(ComponentType.ReadOnly<PolishedLot>(), ComponentType.ReadOnly<PolishGarden>(), ComponentType.ReadOnly<Transform>(),
                ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (ToolRegistration.Settings == null || !ToolRegistration.Settings.ShowGardenOutlines) { _shown.Clear(); return; }
            try
            {
                if (UnityEngine.Time.realtimeSinceStartup - _readAt > 0.5f || _readAt < 0f) { Read(); _readAt = UnityEngine.Time.realtimeSinceStartup; }
                if (_shown.Count == 0) return;
                var buffer = _overlay.GetBuffer(out JobHandle deps);
                deps.Complete();
                var cam = Camera.main;
                // (thicker from further away, so they read at any zoom)
                float w = cam != null ? math.clamp(cam.transform.position.y * 0.0012f, 0.12f, 1.2f) : 0.25f;
                foreach (var (garden, lot, colour) in _shown)
                {
                    for (int k = 0; k < lot.Length; k++) Line(buffer, LotColour, lot[k], lot[(k + 1) % lot.Length], w * 0.4f);
                    for (int k = 0; k < garden.Length; k++)
                    {
                        var a = garden[k];
                        var b = garden[(k + 1) % garden.Length];
                        bool tooShort = math.distance(a.xz, b.xz) < 2f;
                        Line(buffer, tooShort ? ShortColour : colour, a, b, tooShort ? w * 2f : w);
                        buffer.DrawCircle(colour, a, w * 2.2f);
                    }
                }
            }
            catch (Exception e)
            {
                if (_errors++ < 10) Mod.Log.Error(e, "Placemaker: garden outlines failed");
            }
        }

        private static void Line(OverlayRenderSystem.Buffer buffer, Color c, float3 a, float3 b, float w) =>
            buffer.DrawLine(c, c, 0f, OverlayRenderSystem.StyleFlags.Projected, new Line3.Segment(a, b), w, new float2(1f, 1f));

        /// <summary>The gardens near the ground point the camera looks at.</summary>
        private void Read()
        {
            _shown.Clear();
            var cam = Camera.main;
            if (cam == null) return;
            float3 cp = cam.transform.position;
            float3 fw = cam.transform.forward;
            float2 at = cp.xz + (fw.y < -0.05f ? fw.xz * math.min(cp.y / -fw.y, 3000f) : float2.zero);
            float radius = math.clamp(cp.y * 3f, 300f, 2500f);
            int all = _gardens.CalculateEntityCount();
            using (var entities = _gardens.ToEntityArray(Allocator.Temp))
                foreach (var e in entities)
                {
                    var t = EntityManager.GetComponentData<Transform>(e);
                    if (math.distance(t.m_Position.xz, at) > radius) continue;
                    if (!EntityManager.GetComponentData<PolishedLot>(e).Yes) continue;
                    var ring = EntityManager.GetBuffer<PolishGarden>(e, true);
                    if (ring.Length < 3) continue;
                    var garden = new float3[ring.Length];
                    for (int k = 0; k < ring.Length; k++) garden[k] = new float3(ring[k].Position.x, t.m_Position.y, ring[k].Position.y);
                    float3[] lot = Array.Empty<float3>();
                    var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                    if (EntityManager.HasComponent<BuildingData>(prefab))
                    {
                        var half = (float2)EntityManager.GetComponentData<BuildingData>(prefab).m_LotSize * 4f;
                        var right = math.mul(t.m_Rotation, new float3(1f, 0f, 0f));
                        var fwd = math.mul(t.m_Rotation, new float3(0f, 0f, 1f));
                        float3 P(float x, float z) => t.m_Position + right * x + fwd * z;
                        lot = new[] { P(-half.x, -half.y), P(half.x, -half.y), P(half.x, half.y), P(-half.x, half.y) };
                    }
                    // (a colour of its own: neighbours differ)
                    uint hsh = (uint)e.Index * 2654435761u;
                    // (never red: red is an edge too short)
                    var colour = Color.HSVToRGB(0.1f + (hsh >> 8) % 1000 / 1000f * 0.75f, 0.85f, 1f);
                    colour.a = 0.95f;
                    _shown.Add((garden, lot, colour));
                }
            if (_shown.Count != _logged) { _logged = _shown.Count; Mod.Log.Info($"Placemaker garden outlines: {_shown.Count} gardens drawn near {at} ({all} outgrown buildings with a garden)"); }
        }
    }
}
