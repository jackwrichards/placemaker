using System.Linq; using Placemaker.Core; using Xunit; using Xunit.Abstractions;
namespace Placemaker.Tests { public class TmpProbe { private readonly ITestOutputHelper o; public TmpProbe(ITestOutputHelper x){o=x;}
 [Fact] public void P(){ ControlEffectTests.LoadDecals(); var plan=BiomeMixTests.Park("greenway",216,196,"flat",new[]{("grass",100.0)});
  foreach(var l in plan.Layers){ o.WriteLine(l.GeneratorId+" paint: "+string.Join(" ",l.Paint.Select(p=>p.Layer+"@"+p.Strength.ToString("0.00")+" area "+(p.Ring==null?-1:Placemaker.Core.Geometry.Poly.Area(Placemaker.Core.Geometry.Poly.To2D(p.Ring))).ToString("0")+" field "+(p.GetType().GetFields().Select(f2=>f2.Name+"="+f2.GetValue(p)).Aggregate((a,b)=>a+","+b)))));
   o.WriteLine(" decals strokes: "+string.Join(" ",l.Decals.GroupBy(d=>d.Kind).Select(g=>g.Key+"x"+g.Count()))); }
  if(plan.Weathering!=null) o.WriteLine("weathering: "+string.Join(" ",plan.Weathering.ByCause.Select(kv=>kv.Key+"="+kv.Value))); } } }
