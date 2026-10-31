// Game runner — what a removed build leaves behind (GPL-3.0).
//   {"do":"lab","op":"leftovers","label":"before","baseline":true}
//       counts everything in the scenario's area (its outline's bounds plus 12 m): objects, buildings, trees, net edges and
//       nodes, lots and surfaces, by prefab; hidden or overridden pieces; and, city-wide, our own prefabs (owner lots, zone
//       blocks, companions, surface copies), tagged pieces, group records, decals drawn by code. Kept as the baseline.
//   {"do":"lab","op":"leftovers","label":"after the bulldozer"}
//       the same count, and what differs from the baseline, prefab by prefab ("leftovers: none" when nothing does).

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Common;
using Game.Tools;
using Newtonsoft.Json.Linq;
using Placemaker.Game.Decals;
using Placemaker.Game.Groups;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteRunner
    {
        private readonly Dictionary<string, SortedDictionary<string, int>> _leftoverBaselines = new Dictionary<string, SortedDictionary<string, int>>(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<Entity>> _leftoverBaseEntities = new Dictionary<string, HashSet<Entity>>(StringComparer.Ordinal);
        private HashSet<Entity> _censusEntities;

        private void LabLeftovers(ScenarioState st, JObject step, JObject rec)
        {
            string label = (string)step["label"] ?? "";
            string key = st.S?.Id ?? "";
            _censusEntities = new HashSet<Entity>();
            var counts = LeftoverCensus(st);
            var all = new JObject();
            foreach (var kv in counts) all[kv.Key] = kv.Value;
            rec["census"] = all;
            if ((bool?)step["baseline"] == true || !_leftoverBaselines.ContainsKey(key))
            {
                _leftoverBaselines[key] = counts;
                _leftoverBaseEntities[key] = _censusEntities;
                Mod.Log.Info($"Placemaker lab: leftovers ({label}): baseline taken, {counts.Count} kind(s): "
                    + string.Join(", ", counts.Select(kv => $"{kv.Value} {kv.Key}")) + ".");
                return;
            }
            var basis = _leftoverBaselines[key];
            var diff = new List<string>();
            var jd = new JObject();
            foreach (var k in basis.Keys.Union(counts.Keys).OrderBy(k => k, StringComparer.Ordinal))
            {
                int a = basis.TryGetValue(k, out var x) ? x : 0, b = counts.TryGetValue(k, out var y) ? y : 0;
                if (a == b) continue;
                diff.Add($"{k} {(b > a ? "+" : "")}{b - a}");
                jd[k] = b - a;
            }
            rec["diff"] = jd;
            rec["leftovers"] = diff.Count;
            Mod.Log.Info($"Placemaker lab: leftovers ({label}), group {st.Group}: " + (diff.Count == 0 ? "none" : string.Join("; ", diff)) + ".");
            if (diff.Count > 0 && _leftoverBaseEntities.TryGetValue(key, out var before)) ExplainLeftovers(before, label);
        }

        /// <summary>
        /// {"do":"lab","op":"removeGroups","which":"current"|"all","after":0,"wait":true,"label":"..."}: asks the registry to remove
        /// the scenario's build or every build in the city, now or <c>after</c> frames later (in the background, so the next
        /// steps run meanwhile: a build that starts while it lands), and with <c>wait</c> waits until they are gone.
        /// {"do":"lab","op":"prepareUninstall"}: the settings button "Get this city ready to remove Placemaker".
        /// </summary>
        private IEnumerator LabRemoveGroups(ScenarioState st, JObject step, JObject rec)
        {
            string label = (string)step["label"] ?? "";
            var groups = ((string)step["which"] ?? "current") == "all" ? new List<int>(Services.Groups?.AllGroups ?? new List<int>()) : new List<int> { st.Group };
            groups.RemoveAll(g => g == 0);
            int after = (int?)step["after"] ?? 0;
            rec["groups"] = new JArray(groups);
            void Request()
            {
                int ok = 0;
                foreach (var g in groups) if (Services.Groups != null && Services.Groups.RequestRemove(g)) ok++;
                Mod.Log.Info($"Placemaker lab: remove groups ({label}): {ok} of {groups.Count} removal(s) requested at frame {Time.frameCount}"
                    + (Services.Build is global::Placemaker.Game.Build.BuildService b && b.Busy ? ", while a build runs" : "") + ".");
            }
            if (after <= 0) Request();
            else StartCoroutine(Guard(Later(after, Request), e => Mod.Log.Error(e, "lab removeGroups")));
            if ((bool?)step["wait"] != true) yield break;
            float t0 = Time.realtimeSinceStartup;
            yield return Until(() => Services.Groups == null || groups.TrueForAll(g => Services.Groups.Get(g) == null), 120f, "removals");
            rec["seconds"] = Math.Round(Time.realtimeSinceStartup - t0, 2);
            Mod.Log.Info($"Placemaker lab: remove groups ({label}): {groups.Count} gone in {rec["seconds"]} s.");
            if (groups.Contains(st.Group)) { st.Deleted = true; st.Built = false; }
        }

        /// <summary>
        /// {"do":"lab","op":"bulldozeQuestion","answer":"ungroup"|"remove"|"cancel","picture":"question"}: the bulldozer's question
        /// for the scenario's build, asked the way the bulldoze patch asks it (the bulldozer active and waiting), read from the
        /// screen (title, message, buttons), pictured, and answered by clicking the button a person would click.
        /// </summary>
        private IEnumerator LabBulldozeQuestion(ScenarioState st, JObject step, JObject rec)
        {
            string answer = (string)step["answer"] ?? "cancel";
            yield return CloseTool();
            var tools = _world.GetExistingSystemManaged<ToolSystem>();
            var dozer = _world.GetOrCreateSystemManaged<BulldozeToolSystem>();
            tools.activeTool = dozer;
            yield return Frames(10);
            var state = global::Placemaker.Game.Groups.Patches.BulldozeConfirmationPatch.StateField;
            state.SetValue(dozer, Enum.Parse(state.FieldType, "Waiting"));
            bool asked = global::Placemaker.Game.Groups.Patches.BulldozeConfirmationPatch.AskRemoveOrUngroup(dozer, GroupRegistrySystem.Instance, new List<int> { st.Group });
            rec["asked"] = asked;
            yield return Frames(20);
            JObject seen = null;
            yield return UiRun("var b=[].slice.call(document.querySelectorAll('button'));var t=document.body.innerText;"
                + "return {buttons:b.map(function(x){return (x.innerText||'').trim();}).filter(function(x){return x.length>0&&x.length<40;}).slice(-8),"
                + "hasTitle:t.indexOf(" + SuiteUiSystem.Q(L("Placemaker.Bulldoze.Title")) + ")>=0,hasMessage:t.indexOf(" + SuiteUiSystem.Q(L("Placemaker.Bulldoze.Message")) + ")>=0};", r => seen = r);
            rec["screen"] = seen?["value"];
            if (step["picture"] != null) yield return StepShot(st, step, rec, (string)step["picture"]);
            string label = answer == "ungroup" ? L("Placemaker.Bulldoze.Ungroup") : answer == "remove" ? L("Placemaker.Bulldoze.RemoveAll") : L("Placemaker.Bulldoze.Cancel");
            JObject click = null;
            yield return UiRun("var b=[].slice.call(document.querySelectorAll('button')).filter(function(x){return (x.innerText||'').trim()===" + SuiteUiSystem.Q(label) + ";});"
                + "if(!b.length)return 'no button';b[b.length-1].click();return 'clicked';", r => click = r);
            rec["click"] = click?["value"];
            yield return Frames(30);
            rec["toolState"] = state.GetValue(dozer).ToString();
            bool ungrouped = (Services.Groups as GroupRegistrySystem)?.IsUngrouped(st.Group) ?? false;
            rec["ungrouped"] = ungrouped;
            Mod.Log.Info($"Placemaker lab: bulldoze question for group {st.Group}: asked {asked}, screen {seen?["value"]?.ToString(Newtonsoft.Json.Formatting.None)}, "
                + $"answer '{label}' {rec["click"]}, bulldozer {rec["toolState"]}, ungrouped {ungrouped}.");
            tools.activeTool = _world.GetOrCreateSystemManaged<DefaultToolSystem>();
            yield return Frames(10);
            yield return OpenTool();
        }

        private static string L(string key)
        {
            try
            {
                var dict = global::Game.SceneFlow.GameManager.instance?.localizationManager?.activeDictionary;
                if (dict != null && dict.TryGetValue(key, out var v)) return v;
            }
            catch (Exception) { }
            return key;
        }

        private static IEnumerator Later(int frames, Action act)
        {
            yield return Frames(frames);
            act();
        }

        /// <summary>Entities in the area now that were not there at the baseline: who owns them, and in what state.</summary>
        private void ExplainLeftovers(HashSet<Entity> before, string label)
        {
            var em = _world.EntityManager;
            var lines = new List<string>();
            int n = 0;
            foreach (var e in _censusEntities)
            {
                if (before.Contains(e)) continue;
                if (++n > 12) continue;
                string owner = "no owner";
                if (em.HasComponent<Owner>(e))
                {
                    var o = em.GetComponentData<Owner>(e).m_Owner;
                    owner = o == Entity.Null ? "owner null" : !em.Exists(o) ? $"owner {o.Index} gone" : $"owner {o.Index} {_lookup.KindOf(o)} {_lookup.PrefabName(o)}"
                        + (em.HasComponent<Deleted>(o) ? " (deleted)" : "") + (em.HasComponent<PlacemakerMember>(o) ? " (tagged)" : "");
                    if (em.Exists(o) && em.HasBuffer<global::Game.Objects.SubObject>(o))
                    {
                        var buf = em.GetBuffer<global::Game.Objects.SubObject>(o, true);
                        bool listed = false; for (int i = 0; i < buf.Length; i++) if (buf[i].m_SubObject == e) listed = true;
                        owner += listed ? ", listed in its sub-objects" : ", NOT in its sub-objects";
                    }
                }
                var flags = new List<string>();
                if (em.HasComponent<global::Game.Objects.Secondary>(e)) flags.Add("secondary");
                if (em.HasComponent<global::Game.Objects.Attached>(e)) { var p = em.GetComponentData<global::Game.Objects.Attached>(e).m_Parent; flags.Add("attached to " + (p == Entity.Null ? "null" : em.Exists(p) ? _lookup.PrefabName(p) : "gone")); }
                if (em.HasComponent<global::Game.Tools.Hidden>(e)) flags.Add("hidden");
                if (em.HasComponent<Overridden>(e)) flags.Add("overridden");
                if (em.HasComponent<global::Game.Objects.Static>(e)) flags.Add("static");
                lines.Add($"{e.Index} {_lookup.KindOf(e)} {_lookup.PrefabName(e)}: {owner}" + (flags.Count > 0 ? " [" + string.Join(", ", flags) + "]" : ""));
            }
            Mod.Log.Info($"Placemaker lab: leftovers ({label}): {n} new entit(ies) in the area; " + string.Join("; ", lines));
        }

        private SortedDictionary<string, int> LeftoverCensus(ScenarioState st)
        {
            var em = _world.EntityManager;
            var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            void Add(string k) => counts[k] = (counts.TryGetValue(k, out var n) ? n : 0) + 1;
            float2 min = new float2(float.MaxValue), max = new float2(float.MinValue);
            if (st.Ring != null) foreach (var p in st.Ring) { min = math.min(min, p); max = math.max(max, p); }
            min -= 12f; max += 12f;
            bool In(float3 p) => st.Ring != null && p.x >= min.x && p.x <= max.x && p.z >= min.y && p.z <= max.y;

            var desc = new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<global::Game.Prefabs.PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            };
            using (var q = em.CreateEntityQuery(desc))
            using (var ents = q.ToEntityArray(Allocator.Temp))
                foreach (var e in ents)
                {
                    string name = _lookup.PrefabName(e) ?? "?";
                    bool own = name.StartsWith("Placemaker", StringComparison.Ordinal);
                    float3 at; string kind;
                    if (em.HasComponent<global::Game.Net.Edge>(e) && em.HasComponent<global::Game.Net.Curve>(e))
                    { kind = "edge"; at = global::Colossal.Mathematics.MathUtils.Position(em.GetComponentData<global::Game.Net.Curve>(e).m_Bezier, 0.5f); }
                    else if (em.HasComponent<global::Game.Net.Node>(e)) { kind = "node"; at = em.GetComponentData<global::Game.Net.Node>(e).m_Position; }
                    else if (em.HasComponent<global::Game.Areas.Area>(e) && em.HasBuffer<global::Game.Areas.Node>(e))
                    {
                        var nodes = em.GetBuffer<global::Game.Areas.Node>(e, true);
                        if (nodes.Length == 0) continue;
                        float3 c = default; for (int i = 0; i < nodes.Length; i++) c += nodes[i].m_Position; at = c / nodes.Length;
                        kind = em.HasComponent<global::Game.Areas.Lot>(e) ? "lot" : em.HasComponent<global::Game.Areas.Surface>(e) ? "surface" : "area";
                    }
                    else if (em.HasComponent<global::Game.Objects.Transform>(e))
                    {
                        at = em.GetComponentData<global::Game.Objects.Transform>(e).m_Position;
                        kind = em.HasComponent<global::Game.Buildings.Building>(e) ? "building" : em.HasComponent<global::Game.Objects.Tree>(e) ? "tree"
                            : em.HasComponent<global::Game.Vehicles.Vehicle>(e) || em.HasComponent<global::Game.Creatures.Creature>(e) ? null : "object";
                        if (kind == null) continue;   // traffic and people come and go
                    }
                    else if (own) { Add("city-wide: " + name + " (no position)"); continue; }
                    else continue;
                    if (own && (kind == "lot" || kind == "building" || kind == "surface")) Add("city-wide: own " + kind + " " + name);
                    if (!In(at)) continue;
                    Add(kind + " " + name);
                    _censusEntities?.Add(e);
                    if (em.HasComponent<global::Game.Tools.Hidden>(e)) Add("hidden " + kind);
                    if (em.HasComponent<Overridden>(e)) Add("overridden " + kind);
                }
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<PlacemakerMember>(), ComponentType.Exclude<Deleted>()))
            { int n = q.CalculateEntityCount(); if (n > 0) counts["city-wide: tagged pieces"] = n; }
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<PlacemakerGroup>(), ComponentType.Exclude<Deleted>()))
            { int n = q.CalculateEntityCount(); if (n > 0) counts["city-wide: group records"] = n; }
            if (SharedDecals.InstanceCount > 0) counts["city-wide: code decals"] = SharedDecals.InstanceCount;
            return counts;
        }
    }
}
