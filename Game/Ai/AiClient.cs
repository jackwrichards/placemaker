// AI builder (docs/AI-BUILDER.md): one chat call to an OpenAI-compatible endpoint (OpenRouter by default), off the main
// thread. The key goes only into the Authorization header; it is never logged or written to a file.

using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Placemaker.Game.Ai
{
    public sealed class AiReply
    {
        public int Status;               // HTTP status, 0 = no answer (network, timeout)
        public string Text = "";
        public string Error;
        public string Model;
        public int PromptTokens, ReplyTokens;
        public double? Cost;             // USD, when the endpoint reports it (OpenRouter's usage accounting)
        public long Ms;
        public string RawResponse = "";
        public string Reasoning = "";
        public int ReasoningTokens, CachedTokens;
        public string FinishReason;
    }

    public static class AiClient
    {
        private static readonly HttpClient Http = MakeClient();

        private static HttpClient MakeClient()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch (Exception) { }
            return new HttpClient { Timeout = TimeSpan.FromSeconds(240) };
        }

        /// <summary>The request body (no key in it), so it can be saved next to the reply.</summary>
        public static JObject Body(string model, string system, string userText, string pngBase64, int maxTokens)
        {
            var content = new JArray { new JObject { ["type"] = "text", ["text"] = userText } };
            if (!string.IsNullOrEmpty(pngBase64))
                content.Add(new JObject { ["type"] = "image_url", ["image_url"] = new JObject { ["url"] = "data:image/png;base64," + pngBase64 } });
            return new JObject
            {
                ["model"] = model,
                ["max_tokens"] = maxTokens,
                ["messages"] = new JArray
                {
                    new JObject { ["role"] = "system", ["content"] = system },
                    new JObject { ["role"] = "user", ["content"] = content },
                },
                ["usage"] = new JObject { ["include"] = true },
            };
        }

        /// <summary>A message content as text: a string, or the text parts of an array of parts.</summary>
        public static string ContentText(JToken c)
        {
            if (c == null || c.Type == JTokenType.Null) return "";
            if (c.Type == JTokenType.String) return (string)c;
            if (c is JArray a) return string.Join("\n", a.Select(p => p.Type == JTokenType.String ? (string)p : (string)p["text"] ?? "").Where(t => t.Length > 0));
            return c.ToString();
        }

        public static async Task<AiReply> Send(string endpoint, string key, JObject body, CancellationToken cancel)
        {
            var r = new AiReply { Model = (string)body["model"] };
            var sw = Stopwatch.StartNew();
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Post, endpoint))
                {
                    req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
                    req.Headers.TryAddWithoutValidation("HTTP-Referer", "https://mods.paradoxplaza.com/");
                    req.Headers.TryAddWithoutValidation("X-Title", "Placemaker");
                    req.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
                    using (var resp = await Http.SendAsync(req, cancel).ConfigureAwait(false))
                    {
                        r.Status = (int)resp.StatusCode;
                        r.RawResponse = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    }
                }
                JObject j = null;
                try { j = JObject.Parse(r.RawResponse); } catch (Exception) { }
                if (j?["error"] != null) r.Error = (string)j["error"]?["message"] ?? j["error"].ToString();
                else if (r.Status < 200 || r.Status >= 300) r.Error = "HTTP " + r.Status;
                if (j != null)
                {
                    r.Text = ContentText(j["choices"]?[0]?["message"]?["content"]);
                    r.Reasoning = ContentText(j["choices"]?[0]?["message"]?["reasoning"]);
                    r.FinishReason = (string)j["choices"]?[0]?["finish_reason"];
                    r.Model = (string)j["model"] ?? r.Model;
                    r.PromptTokens = (int?)j["usage"]?["prompt_tokens"] ?? 0;
                    r.ReplyTokens = (int?)j["usage"]?["completion_tokens"] ?? 0;
                    r.Cost = (double?)j["usage"]?["cost"];
                    r.ReasoningTokens = (int?)j["usage"]?["completion_tokens_details"]?["reasoning_tokens"] ?? 0;
                    r.CachedTokens = (int?)j["usage"]?["prompt_tokens_details"]?["cached_tokens"] ?? 0;
                }
            }
            catch (Exception e)
            {
                r.Error = e.GetType().Name + ": " + (e.InnerException?.Message ?? e.Message);
            }
            r.Ms = sw.ElapsedMilliseconds;
            return r;
        }
    }
}
