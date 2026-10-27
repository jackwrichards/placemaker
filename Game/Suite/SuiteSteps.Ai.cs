// Game runner: the "ai" step (docs/AI-BUILDER.md, phase one). Sends the site brief of the current selection with the
// step's prompt, waits for the reply, copies the brief's map and text into the run, and records the call's numbers.
// Judges nothing: the reply is read by whoever reads the run.

using System;
using System.Collections;
using System.IO;
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
                    ["costUsd"] = r.Cost, ["replyChars"] = r.Text?.Length ?? 0,
                };
                if (r.Error != null) rec["error"] = "ai: " + r.Error;
            }
            else if (_timedOut) rec["error"] = "ai: no reply within " + timeout + " s";

            // the brief, the map and the reply into the run, next to the pictures
            try
            {
                string map = Path.Combine(folder, "map.png");
                if (File.Exists(map))
                {
                    string file = ShotFile(st, "ai-map");
                    File.Copy(map, Path.Combine(_shots, file));
                    rec["shot"] = "shots/" + file;
                    st.Shots["ai-map"] = "shots/" + file;
                    _contact.Add(new ShotRow { Scenario = st.FileIndex, Id = st.S.Id, Step = st.StepIndex, Do = "ai", Name = "ai-map", File = "shots/" + file, Mode = "ai map", Settled = true });
                }
                foreach (var f in new[] { "brief.txt", "reply.txt", "call.json" })
                    if (File.Exists(Path.Combine(folder, f))) File.Copy(Path.Combine(folder, f), Path.Combine(_out, $"{st.FileIndex + 1:D2}-{Safe(st.S.Id)}.{st.StepIndex:D2}-ai-{f}"), true);
            }
            catch (Exception e) { Say("ai: copying the files failed: " + e.Message); }
            if (r != null)
                Say($"ai: {r.Model} HTTP {r.Status} in {r.Ms} ms, {r.PromptTokens} + {r.ReplyTokens} tokens, cost {(r.Cost.HasValue ? "$" + r.Cost.Value.ToString("0.0000") : "unknown")}" +
                    (r.Error != null ? ", error: " + r.Error : ", reply " + r.Text.Length + " chars"));
        }
    }
}
