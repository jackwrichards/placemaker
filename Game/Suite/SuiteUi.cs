// In-game test suite: the panel, driven the way a person drives it. A small UI system (registered only when the suite
// gate opened) runs JavaScript in the game's UI view: it reads and clicks the panel's real elements (data-testid) and
// fires the panel's own triggers (engine.trigger('Placemaker.<name>', ...)), so the TriggerBinding path, its argument
// conversions and the React panel's own gates run, in the UI update like a click. Results come back through one
// trigger of its own (PlacemakerSuite.report). It also reads the JSON the panel is bound to (state, design, summary,
// catalog) — what the panel shows.

using System;
using System.Collections.Generic;
using Colossal.UI.Binding;
using Game.SceneFlow;
using Game.UI;
using Newtonsoft.Json.Linq;
using UnityEngine.Scripting;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteUiSystem : UISystemBase
    {
        public const string Group = "PlacemakerSuite";
        public static SuiteUiSystem Instance { get; private set; }

        private int _nextId;
        private readonly Dictionary<int, JObject> _replies = new Dictionary<int, JObject>();
        public int Reports { get; private set; }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            Instance = this;
            AddBinding(new TriggerBinding<string>(Group, "report", OnReport));
        }

        private void OnReport(string json)
        {
            Reports++;
            try
            {
                var o = JObject.Parse(json ?? "{}");
                int id = (int?)o["id"] ?? -1;
                lock (_replies) _replies[id] = o;
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker suite: ui report unreadable: " + e.Message); }
        }

        private static cohtml.Net.View View => GameManager.instance?.userInterface?.view?.View;

        public static bool Available => Instance != null && View != null;

        /// <summary>Runs a function body in the UI view; its return value comes back as reply["value"]. Returns the request id or -1.</summary>
        public int Run(string body)
        {
            var v = View;
            if (v == null) return -1;
            int id = ++_nextId;
            string js = "(function(){var __id=" + id + ";try{var __v=(function(){" + body + "})();"
                + "engine.trigger('" + Group + ".report',JSON.stringify({id:__id,ok:true,value:(__v===undefined?null:__v)}));}"
                + "catch(e){engine.trigger('" + Group + ".report',JSON.stringify({id:__id,ok:false,error:String(e)}));}})();";
            try { v.ExecuteScript(js); }
            catch (Exception e) { Mod.Log.Warn("Placemaker suite: ExecuteScript failed: " + e.Message); return -1; }
            return id;
        }

        public JObject Take(int id)
        {
            lock (_replies)
            {
                if (!_replies.TryGetValue(id, out var o)) return null;
                _replies.Remove(id);
                return o;
            }
        }

        /// <summary>A JS string literal.</summary>
        public static string Q(string s) => Newtonsoft.Json.JsonConvert.ToString(s ?? "");

        /// <summary>engine.trigger('Placemaker.name', args...) — the call a panel button makes.</summary>
        public static string TriggerJs(string name, params object[] args)
        {
            var sb = new System.Text.StringBuilder("engine.trigger(" + Q("Placemaker." + name));
            foreach (var a in args)
            {
                sb.Append(',');
                switch (a)
                {
                    case null: sb.Append("null"); break;
                    case string s: sb.Append(Q(s)); break;
                    case bool b: sb.Append(b ? "true" : "false"); break;
                    case float f: sb.Append(f.ToString("R", System.Globalization.CultureInfo.InvariantCulture)); break;
                    case double d: sb.Append(d.ToString("R", System.Globalization.CultureInfo.InvariantCulture)); break;
                    default: sb.Append(Convert.ToString(a, System.Globalization.CultureInfo.InvariantCulture)); break;
                }
            }
            sb.Append(");return true;");
            return sb.ToString();
        }

        /// <summary>
        /// JS that finds the element with a data-testid, reports what a person would see (present, disabled, text,
        /// rectangle) and optionally clicks it (HTMLElement.click, else a dispatched MouseEvent).
        /// </summary>
        public static string ElementJs(string testId, bool click)
        {
            return "var el=document.querySelector('[data-testid=\"'+" + Q(testId) + "+'\"]');"
                + "if(!el)return {found:false};"
                + "var r=el.getBoundingClientRect();"
                + "var dis=el.getAttribute('aria-disabled')==='true'||el.disabled===true||(el.className&&String(el.className).indexOf('disabled')>=0);"
                + "var o={found:true,disabled:dis,text:String(el.textContent||'').replace(/\\s+/g,' ').trim().slice(0,120),rect:[r.left,r.top,r.width,r.height],action:el.getAttribute('data-action')};"
                + (click ? "try{el.click();o.clicked='click';}catch(e){try{var ev=new MouseEvent('click',{bubbles:true,cancelable:true});el.dispatchEvent(ev);o.clicked='dispatch';}catch(e2){o.clicked='failed: '+e2;}}" : "")
                + "return o;";
        }

        /// <summary>JS: drags an element with the mouse by (dx, dy) screen pixels: a press near its left end, moves on the
        /// window, a release (how a window is moved by its header).</summary>
        public static string DragJs(string testId, float dx, float dy)
        {
            string x = dx.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), y = dy.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            return "var el=document.querySelector('[data-testid=\"'+" + Q(testId) + "+'\"]');"
                + "if(!el)return {found:false};"
                + "var r=el.getBoundingClientRect(),sx=r.left+Math.min(40,r.width/2),sy=r.top+r.height/2;"
                + "function ev(t,x,y){return new MouseEvent(t,{bubbles:true,cancelable:true,clientX:x,clientY:y,button:0});}"
                + "el.dispatchEvent(ev('mousedown',sx,sy));"
                + "for(var i=1;i<=4;i++)window.dispatchEvent(ev('mousemove',sx+" + x + "*i/4,sy+" + y + "*i/4));"
                + "window.dispatchEvent(ev('mouseup',sx+" + x + ",sy+" + y + "));"
                + "return {found:true,from:[sx,sy],by:[" + x + "," + y + "]};";
        }

        /// <summary>JS: a double-click on an element (near its left end).</summary>
        public static string DoubleClickJs(string testId) =>
            "var el=document.querySelector('[data-testid=\"'+" + Q(testId) + "+'\"]');"
            + "if(!el)return {found:false};"
            + "var r=el.getBoundingClientRect();"
            + "el.dispatchEvent(new MouseEvent('dblclick',{bubbles:true,cancelable:true,clientX:r.left+Math.min(40,r.width/2),clientY:r.top+r.height/2,button:0}));"
            + "return {found:true};";

        /// <summary>JS: the panel's rectangle (the header's container) in screen pixels from the top-left, and the view size.</summary>
        public static string PanelRectJs =>
            "var h=document.querySelector('[data-testid=\"panel-header\"]');"
            + "var w=window.innerWidth,hh=window.innerHeight;"
            + "if(!h)return {open:false,view:[w,hh]};"
            + "var p=h.parentElement||h;for(var i=0;i<3&&p.parentElement&&p.getBoundingClientRect().height<200;i++)p=p.parentElement;"
            + "var r=p.getBoundingClientRect();return {open:true,rect:[r.left,r.top,r.width,r.height],view:[w,hh]};";

        // ───────────── what the panel is bound to ─────────────

        private static System.Reflection.FieldInfo FieldOf(string name) =>
            typeof(global::Placemaker.Game.UI.PlacemakerUISystem).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        /// <summary>The JSON of one of the panel's bindings ("state", "design", "summary", "catalog", "managed"); null when unavailable or unparsable.</summary>
        public JToken Bound(string binding, out string error)
        {
            error = null;
            try
            {
                var ui = World.GetExistingSystemManaged<global::Placemaker.Game.UI.PlacemakerUISystem>();
                if (ui == null) { error = "PlacemakerUISystem missing"; return null; }
                var f = FieldOf("_" + binding);
                if (f == null) { error = "no binding field _" + binding; return null; }
                var vb = f.GetValue(ui) as ValueBinding<string>;
                if (vb == null) { error = "binding _" + binding + " is not a ValueBinding<string>"; return null; }
                return JToken.Parse(vb.value ?? "null");
            }
            catch (Exception e) { error = e.GetType().Name + ": " + e.Message; return null; }
        }

        [Preserve]
        protected override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }
    }
}
