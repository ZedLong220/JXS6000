using SmallProject.Devices.Arm.Kinematic;
using SmallProject.Devices.Arm.Kinematic.Models;
using SmallProject.Serials.Slcan;

var path = Path.Combine(AppContext.BaseDirectory, "jxs6000.urdf");
var model = new UrdfKinematics(path);
int passed = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    passed++;
}
var decoder = new SlcanReceiveDecoder();
var receiveEvents = decoder.Append("\r\rt0388010003000000000200\r\a");
Check(receiveEvents.Count == 4, "ACK, CAN state frame, and NAK are framed independently");
Check(receiveEvents[0].IsAcknowledgement && receiveEvents[0].Accepted, "Standalone CR is ACK");
Check(receiveEvents[1].IsAcknowledgement && receiveEvents[1].Accepted, "Back-to-back standalone CR is ACK");
Check(!receiveEvents[2].IsAcknowledgement && receiveEvents[2].Frame == "t0388010003000000000200", "SLCAN CR terminates a CAN frame, not an ACK");
Check(receiveEvents[3].IsAcknowledgement && !receiveEvents[3].Accepted, "BEL is NAK");
Check(decoder.Append("t0388").Count == 0, "Partial CAN frame remains buffered");
var completedEvents = decoder.Append("010003000000000200\r");
Check(completedEvents.Count == 1 && completedEvents[0].Frame == "t0388010003000000000200", "CAN frame can span serial reads");
var zero = model.Forward(new double[6]);
Check(Math.Abs(zero.X - 57.5) < .00001 && Math.Abs(zero.Y - 86.73) < .00001 && Math.Abs(zero.Z - 372.6) < .00001, "JXS6000 zero tool0 position");
Check(Math.Abs(zero.A) < .00001 && Math.Abs(zero.B) < .00001 && Math.Abs(zero.C) < .00001, "JXS6000 zero tool0 orientation");
var legacy = new double[] { 0, 0, 180, 0, -90, 0 };
Check(JointCoordinateMap.ToModel(legacy).All(v => v == 0), "Legacy home maps to model zero");
Check(JointCoordinateMap.ToLegacy(JointCoordinateMap.ToModel(legacy)).SequenceEqual(legacy), "Coordinate round trip");
var sampleAngles = new double[] {10,-20,30,-40,50,-60};
Check(JointCoordinateMap.ToLegacy(sampleAngles).Zip(Joint6D_t.defult.a).Select(p=>p.First-p.Second).SequenceEqual(sampleAngles), "Motor relative targets retain original home mapping");
var targets = new[] {
    new double[] {0,0,0,0,0,0}, new double[] {15,-20,-30,15,25,35},
    new double[] {-30,25,-50,-20,-35,45}, new double[] {25,-35,45,30,60,-45},
    new double[] {0,0,0,0,90,0}, new double[] {0,0,0,0,-90,0},
    new double[] {120,-60,80,-70,50,100}, new double[] {-100,60,-80,110,-40,-120},
    new double[] {0,150,-150,0,0,0}, new double[] {0,-150,90,0,0,0},
    new double[] {0,0,0,0,0,180}, new double[] {180,0,0,0,0,0}
};
foreach (var q in targets)
{
    var target = model.Forward(q);
    Check(model.TryInverse(target, new double[6], out var answer), "Reachable IK: " + string.Join(",", q));
    var actual = model.Forward(answer);
    Check(Math.Sqrt(Math.Pow(actual.X-target.X,2)+Math.Pow(actual.Y-target.Y,2)+Math.Pow(actual.Z-target.Z,2)) < .03, "IK position residual");
    Check(actual.R.Zip(target.R).All(pair => Math.Abs(pair.First-pair.Second) < .0002), "IK rotation residual");
    Check(model.WithinLimits(answer), "IK respects URDF limits");
}
Check(!model.TryInverse(new Pose6D_t(10000,10000,10000,0,0,0), new double[6], out _), "Unreachable target fails");
Check(!model.TryInverse(new Pose6D_t(double.NaN,0,0,0,0,0), new double[6], out _), "Non-finite target fails");
Check(Math.Abs(model.LowerDegrees[1] + 250) < .00001 && Math.Abs(model.UpperDegrees[1] - 180) < .00001, "J2 extends its negative limit to -250 degrees");
Check(Math.Abs(model.LowerDegrees[2] + 180) < .00001 && Math.Abs(model.UpperDegrees[2] - 330) < .00001, "J3 keeps its negative range and extends to +330 degrees");
var expandedLowerLegacy = JointCoordinateMap.ToLegacy(new double[] {0,-250,-180,0,0,0});
var expandedUpperLegacy = JointCoordinateMap.ToLegacy(new double[] {0,180,330,0,0,0});
Check(expandedLowerLegacy[1] == -250 && expandedUpperLegacy[1] == 180, "J2 expanded model range maps to legacy -250 through 180 degrees");
Check(expandedLowerLegacy[2] == 0 && expandedUpperLegacy[2] == 510, "J3 motor coordinates preserve the existing 180 degree home offset");
Check(model.WithinLimits(new double[] {0,0,0,0,-90,0}) && model.WithinLimits(new double[] {0,0,0,0,360,0}), "J5 keeps -90 degrees and extends to +360 degrees");
Check(!model.WithinLimits(new double[] {0,0,0,0,-91,0}) && !model.WithinLimits(new double[] {0,0,0,0,361,0}), "J5 limits");
var compatibility = new Dof6kinematic(path);
var legacyPose = new Pose6D_t();
Check(compatibility.SolveFK(new Joint6D_t(legacy), legacyPose) && Math.Abs(legacyPose.Z-zero.Z) < 1e-9, "Legacy callers use new URDF FK");
var fixedTargets = new[] {
    new double[] {0,40,-40,0,0,0}, new double[] {0,10,-50,0,0,0},
    new double[] {0,60,-10,0,0,0}, new double[] {0,50,-100,0,0,0}, new double[6] };
var scale = new[] {-150.0/360,150.0/360};
var continuous = ContinuousPathPlanner.Build(model,new double[6],fixedTargets,
    new double[] {4,3,5,6,8},scale,false,jointIndices:new[]{1,2});
Check(continuous.Segments.Count==9,"Five fixed legs use four short corner blends");
Check(continuous.MaximumDeviationMm<=.9,"Full convex-hull deviation, after wire quantization, stays inside 1 mm");
Check(continuous.Segments.Take(8).All(s=>s.EndVelocity.Any(v=>Math.Abs(v)>.001)),"Every non-reversing vector corner has a nonzero pass-through velocity");
Check(continuous.Segments[^1].EndVelocity.All(v=>v==0),"Final knot stops");
Check(continuous.Samples.All(model.WithinLimits),"Sampled actual curve remains within limits");
for(int i=0;i<continuous.Segments.Count;++i)
{
    var s=continuous.Segments[i];
    var a=s.ReferenceStart; var b=s.ReferenceEnd;
    for(int k=0;k<=200;++k)
    {
        var q=ContinuousPathPlanner.Evaluate(s.Controls,k/200.0);
        var d=b.Zip(a,(x,y)=>x-y).ToArray(); double norm=d.Sum(x=>x*x);
        double u=norm==0?0:Math.Clamp(q.Zip(a,(x,y)=>x-y).Zip(d,(x,y)=>x*y).Sum()/norm,0,1);
        var reference=a.Zip(d,(x,y)=>x+u*y).ToArray();
        var actualPose=model.Forward(q); var referencePose=model.Forward(reference);
        Check(Math.Sqrt(Math.Pow(actualPose.X-referencePose.X,2)+Math.Pow(actualPose.Y-referencePose.Y,2)+Math.Pow(actualPose.Z-referencePose.Z,2))<=1,"Sampled TCP geometric deviation");
    }
    if(i>0) for(int j=1;j<=2;++j)
    {
        var p=continuous.Segments[i-1];
        Check(Math.Abs(p.Controls[5][j]-s.Controls[0][j])<1e-10,"Knot position continuity");
        Check(Math.Abs(5*(p.Controls[5][j]-p.Controls[4][j])/p.Duration-5*(s.Controls[1][j]-s.Controls[0][j])/s.Duration)<.00001,"Knot velocity continuity");
        Check(Math.Abs(p.Controls[5][j]-2*p.Controls[4][j]+p.Controls[3][j])<1e-10 && Math.Abs(s.Controls[0][j]-2*s.Controls[1][j]+s.Controls[2][j])<1e-10,"Knot acceleration is continuous and zero");
    }
}
var reversal = ContinuousPathPlanner.Build(model,new double[6],
    new[]{new double[]{0,20,-20,0,0,0},new double[6]},new double[]{2,2},scale,false,jointIndices:new[]{1,2});
Check(reversal.Segments[0].EndVelocity.All(v=>v==0),"Exact vector reversal decelerates to zero without a network dwell");
var periodic=ContinuousPathPlanner.Build(model,new double[6],fixedTargets,new double[]{4,3,5,6,8},scale,true,jointIndices:new[]{1,2});
Check(periodic.FirstSamples.Count>0 && periodic.Segments[^1].End.SequenceEqual(periodic.Start),"Repeat has closed seam and separately validated first entry");
bool rejected=false;
try { ContinuousPathPlanner.Build(model,new double[6],new[]{new double[]{0,20,-20,10,0,0}},new[]{2.0},scale,false,jointIndices:new[]{1,2}); }
catch(ArgumentException) { rejected=true; }
Check(rejected,"Inactive axes cannot silently move in a planned path");
var wirePath=Path.Combine(AppContext.BaseDirectory,"continuous-wire.txt");
using(var wire=new StreamWriter(wirePath))
{
    string F(float v)=>v.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
    wire.WriteLine($"{continuous.Segments.Count} 0 {F(continuous.Start[0])} {F(continuous.Start[1])}");
    foreach(var s in continuous.Segments) wire.WriteLine($"{F(s.Duration)} {F(s.End[0])} {F(s.End[1])} {F(s.EndVelocity[0])} {F(s.EndVelocity[1])}");
    wire.WriteLine(string.Join(" ",Enumerable.Range(0,2).Select(i=>(model.MaximumToolRadiiMillimetres()[i+1]*Math.PI/180/Math.Abs(scale[i])/51200).ToString("R",System.Globalization.CultureInfo.InvariantCulture))));
}
File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"continuous-samples.json"),
    System.Text.Json.JsonSerializer.Serialize(new {samples=continuous.Samples,first=continuous.FirstSamples,
        safe=ContinuousPathPlanner.Build(model,new double[6],new[] {
            new double[]{0,-.5,.5,0,0,0},new double[]{0,-.5,-.5,0,0,0},
            new double[]{0,-1,-.5,0,0,0},new double[6] },new double[]{1,1,1,1},scale,false,jointIndices:new[]{1,2}).Samples}));
var fiveScale=new[]{30.0/360,-150.0/360,150.0/360,-100.0/360,100.0/360};
var fiveTargets=new[]{new double[]{.5,-.5,.5,.5,.5,0},new double[]{-.5,-.5,-.5,-.5,-.5,0},
    new double[]{.75,-.75,-.5,-.25,.25,0},new double[6]};
var five=ContinuousPathPlanner.Build(model,new double[6],fiveTargets,new[]{1.0,1.0,1.0,1.0},fiveScale,false);
Check(five.Start.Length==5 && five.Segments.All(s=>s.End.Length==5 && s.EndVelocity.Length==5),"Five axes, one shared segment clock");
Check(five.MaximumDeviationMm<=.9,"Five-axis curve retains conservative 1 mm geometric tolerance");
Check(five.Samples.All(q=>q[5]==0),"Virtual J6 remains fixed throughout the actual curve");
for(int i=1;i<five.Segments.Count;++i)for(int j=0;j<5;++j)
{
    var a=five.Segments[i-1]; var b=five.Segments[i];
    Check(Math.Abs(a.Controls[5][j]-b.Controls[0][j])<1e-8,"All five knot positions continuous");
    Check(Math.Abs(5*(a.Controls[5][j]-a.Controls[4][j])/a.Duration-5*(b.Controls[1][j]-b.Controls[0][j])/b.Duration)<1e-6,"All five knot velocities continuous");
}
rejected=false;
try{ContinuousPathPlanner.Build(model,new double[6],new[]{new double[]{0,0,0,0,0,1}},new[]{1.0},fiveScale,false);}
catch(ArgumentException){rejected=true;}
Check(rejected,"Virtual sixth-axis commands cannot be silently discarded");
var host=MultiAxisProtocol.Encode(five,new[]{0,1,2,3,4},false);
Check(host[0].Code==0x50 && host[0].Data.SequenceEqual(new byte[]{3,31}),"Protocol 3 explicitly includes all nodes 1..5");
Check(host.All(c=>c.Data.Length<=8),"Every five-axis command fits classic CAN");
Check(host.Where(c=>c.Code==0x53).All(c=>c.Data[0]>=1 && c.Data[0]<=5),"No commands sent for virtual node 6");
Check(host.Count(c=>c.Code==0x53)==five.Segments.Count*10,"Every block includes every axis target and exit velocity");
Check(host.Count(c=>c.Code==0x54)==five.Segments.Count,"Each block has a distinct commit");
var fixedProtocol=MultiAxisProtocol.Encode(continuous,new[]{1,2},false);
Check(fixedProtocol[0].Data[1]==6 && fixedProtocol.Where(c=>c.Code==0x53).All(c=>c.Data[0]==2 || c.Data[0]==3),"Original fixed test explicitly selects J2/J3 only");
File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"five-axis-host.txt"),host.Select(c=>$"{c.Code:X2} {Convert.ToHexString(c.Data)}"));
File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"five-axis-samples.json"),System.Text.Json.JsonSerializer.Serialize(five.Samples));
using(var wire=new StreamWriter(Path.Combine(AppContext.BaseDirectory,"five-axis-wire.txt"))) {
    string F(float v)=>v.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
    wire.WriteLine($"5 {five.Segments.Count} 0 "+string.Join(" ",five.Start.Select(F)));
    foreach(var s in five.Segments) wire.WriteLine(F(s.Duration)+" "+string.Join(" ",s.End.Select(F))+" "+string.Join(" ",s.EndVelocity.Select(F)));
}
var fixedLower=model.LowerDegrees.ToArray(); var fixedUpper=model.UpperDegrees.ToArray(); fixedLower[5]=fixedUpper[5]=0;
var fivePose=model.Forward(new[]{10.0,-20,30,-10,15,0});
Check(model.TryInverse(fivePose,new double[6],out var fiveAnswer,fixedLower,fixedUpper) && Math.Abs(fiveAnswer[5])<1e-8,"Five-axis IK never depends on the virtual sixth axis");
Console.WriteLine($"PASS: {passed} kinematics/planning/protocol checks; five-axis geometric bound {five.MaximumDeviationMm:F4} mm; fixture: five-axis-host.txt");
