// Game runner: the AI builder's steps (docs/AI-BUILDER.md). "ai": the step-1 brief call (one call, no tools; "probe"
// sends an invalid key to prove the network). "aiAgent": a message to the AI category's agent, as the player types it in
// the panel; waits until it finishes or answers, then copies its maps, transcript and summary into the run.
// Judges nothing: whoever reads the run rates it.

using System;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Placemaker.Game.Ai;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteRunner
    {
        private IEnumerator StepAi(ScenarioState st, JObject step, JObject rec)
        {
            string prompt = (string)step["prompt"] ?? "";
            float timeout = (float?)step["timeout"] ?? 240f;
            int before = AiBuilder.Runs;
            if (!AiBuilder.Start(prompt, out var why, probe: (bool?)step["probe"] ?? false)) { rec["error"] = "ai: not sent: " + why; yield break; }
            yield return Until(() => AiBuilder.Runs > before && AiBuilder.Status != AiBuilder.State.Working, timeout, "the AI's reply");
            var r = AiBuilder.LastReply;
            var folder = AiBuilder.LastFolder;
            rec["folder"] = folder;
            if (r != null)
            {
                rec["ai"] = new JObject
                {
                    ["model"] = r.Model, ["status"] = r.Status, ["ms"] = r.Ms, ["promptTokens"] = r.PromptTokens, ["replyTokens"] = r.ReplyTokens,
                    ["costUsd"] = r.Cost, ["replyChars"] = r.Text?.Length ?? 0, ["finish"] = r.FinishReason,
                };
                if (r.Error != null) rec["error"] = "ai: " + r.Error;
            }
            else if (_timedOut) rec["error"] = "ai: no reply within " + timeout + " s";
            CopyAiFiles(st, rec, folder, new[] { "map.png" }, new[] { "brief.txt", "reply.txt", "call.json", "response.json" });
            if (r != null)
                Say($"ai: {r.Model} HTTP {r.Status} in {r.Ms} ms, {r.PromptTokens} + {r.ReplyTokens} tokens, cost {(r.Cost.HasValue ? "$" + r.Cost.Value.ToString("0.0000") : "unknown")}" +
                    (r.Error != null ? ", error: " + r.Error : ", reply " + r.Text.Length + " chars, finish " + r.FinishReason));
        }

        private IEnumerator StepAiAgent(ScenarioState st, JObject step, JObject rec)
        {
            string prompt = (string)step["prompt"] ?? "";
            float timeout = (float?)step["timeout"] ?? 900f;
            if (!AiAgent.Send(prompt, out var why)) { rec["error"] = "aiAgent: not sent: " + why; yield break; }
            yield return Frames(2);
            // "uiShotAfter": a picture of the screen with the panel while the AI works (the chat, its progress, its steps)
            float mid = (float?)step["uiShotAfter"] ?? 0f;
            if (mid > 0f)
            {
                float t0 = UnityEngine.Time.realtimeSinceStartup;
                yield return Until(() => !AiAgent.Busy || UnityEngine.Time.realtimeSinceStartup - t0 >= mid, mid + 5f, "the AI to work a while");
                if (AiAgent.Busy) yield return StepShot(st, new JObject { ["do"] = "shot", ["shotMode"] = "ui" }, rec, "chat-working");
            }
            yield return Until(() => !AiAgent.Busy, timeout, "the AI to finish or answer");
            if (_timedOut) { AiAgent.Stop(); rec["error"] = "aiAgent: still working after " + timeout + " s (stopped)"; yield return Until(() => !AiAgent.Busy, 30f, "the AI to stop"); }
            yield return Frames(30);   // the last re-plan reaches the ghost
            var view = AiAgent.View();
            rec["aiAgent"] = new JObject
            {
                ["status"] = view["status"], ["pieces"] = view["pieces"], ["costUsd"] = view["cost"], ["tokens"] = view["tokens"],
                ["lines"] = ((JArray)view["lines"]).Count, ["wishes"] = new JArray(AiAgent.Wishes),
            };
            var folder = AiAgent.Folder;
            rec["folder"] = folder;
            string[] maps = Array.Empty<string>();
            try { maps = Directory.GetFiles(folder, "map-*.png").Select(Path.GetFileName).OrderBy(n => int.Parse(n.Substring(4, n.Length - 8))).ToArray(); } catch (Exception) { }
            CopyAiFiles(st, rec, folder, maps.Length > 0 ? new[] { maps.Last() } : new string[0], new[] { "brief.txt", "transcript.jsonl", "summary.json" });
            foreach (var l in (JArray)view["lines"])
                Say("ai chat [" + l["who"] + "] " + ((string)l["text"] ?? (string)l["key"] + " " + string.Join(", ", (l["args"] as JArray)?.Select(a => (string)a) ?? Enumerable.Empty<string>())));
            Say($"aiAgent: {view["status"]}, {view["pieces"]} pieces, {view["tokens"]} tokens, ${view["cost"]}, {maps.Length} maps");
        }

        /// <summary>The AI's pictures into the run's shots (and the contact sheet), its text files beside them.</summary>
        private void CopyAiFiles(ScenarioState st, JObject rec, string folder, string[] pictures, string[] texts)
        {
            if (string.IsNullOrEmpty(folder)) return;
            try
            {
                foreach (var pic in pictures)
                {
                    string src = Path.Combine(folder, pic);
                    if (!File.Exists(src)) continue;
                    string name = "ai-" + Path.GetFileNameWithoutExtension(pic);
                    string file = ShotFile(st, name);
                    File.Copy(src, Path.Combine(_shots, file));
                    rec["shot"] = "shots/" + file;
                    st.Shots[name] = "shots/" + file;
                    _contact.Add(new ShotRow { Scenario = st.FileIndex, Id = st.S.Id, Step = st.StepIndex, Do = (string)rec["do"], Name = name, File = "shots/" + file, Mode = "ai map", Settled = true });
                }
                foreach (var f in texts)
                    if (File.Exists(Path.Combine(folder, f))) File.Copy(Path.Combine(folder, f), Path.Combine(_shots, $"{st.FileIndex + 1:D2}-{Safe(st.S.Id)}.{st.StepIndex:D2}-ai-{f}"), true);   // next to the pictures: the queue brings shots/ back
            }
            catch (Exception e) { Say("ai: copying the files failed: " + e.Message); }
        }
    }
}
