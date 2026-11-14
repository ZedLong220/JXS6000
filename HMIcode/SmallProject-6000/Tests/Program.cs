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
Check(Math.Abs(model.LowerDegrees[1] + 180) < .00001 && Math.Abs(model.UpperDegrees[1] - 250) < .00001, "J2 mirrored model range is -180 through 250 degrees");
Check(Math.Abs(model.LowerDegrees[2] + 330) < .00001 && Math.Abs(model.UpperDegrees[2] - 180) < .00001, "J3 mirrored model range is -330 through 180 degrees");
var expandedLowerLegacy = JointCoordinateMap.ToLegacy(new double[] {0,-180,-330,0,0,0});
var expandedUpperLegacy = JointCoordinateMap.ToLegacy(new double[] {0,250,180,0,0,0});
Check(expandedLowerLegacy[1] == -180 && expandedUpperLegacy[1] == 250, "J2 mirrored model range maps to legacy -180 through 250 degrees");
Check(expandedLowerLegacy[2] == -150 && expandedUpperLegacy[2] == 360, "J3 motor coordinates preserve the existing 180 degree home offset");
Check(model.WithinLimits(new double[] {0,0,0,0,-90,0}) && model.WithinLimits(new double[] {0,0,0,0,360,0}), "J5 keeps -90 degrees and extends to +360 degrees");
Check(!model.WithinLimits(new double[] {0,0,0,0,-91,0}) && !model.WithinLimits(new double[] {0,0,0,0,361,0}), "J5 limits");
var compatibility = new Dof6kinematic(path);
var legacyPose = new Pose6D_t();
Check(compatibility.SolveFK(new Joint6D_t(legacy), legacyPose) && Math.Abs(legacyPose.Z-zero.Z) < 1e-9, "Legacy callers use new URDF FK");
var fixedTargets = new[] {
    new double[] {0,40,-40,0,0,0}, new double[] {0,10,-50,0,0,0},
    new double[] {0,60,-10,0,0,0}, new double[] {0,50,-100,0,0,0}, new double[6] };
var scale = new[] {MotorCoordinateMap.Direction(1)*150.0/360,MotorCoordinateMap.Direction(2)*150.0/360};
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
var reductions=new[]{30.0,150,150,100,100,1};
var originalDirections=new[]{1,-1,1,-1,1,1};
var sixScale=reductions.Select((ratio,i)=>MotorCoordinateMap.Direction(i)*ratio/360).ToArray();
var fiveScale=sixScale.Take(5).ToArray();
var fiveTargets=new[]{new double[]{.5,-.5,.5,.5,.5,0},new double[]{-.5,-.5,-.5,-.5,-.5,0},
    new double[]{.75,-.75,-.5,-.25,.25,0},new double[6]};
var five=ContinuousPathPlanner.Build(model,new double[6],fiveTargets,new[]{1.0,1.0,1.0,1.0},fiveScale,false,jointIndices:new[]{0,1,2,3,4});
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
try{ContinuousPathPlanner.Build(model,new double[6],new[]{new double[]{0,0,0,0,0,1}},new[]{1.0},fiveScale,false,jointIndices:new[]{0,1,2,3,4});}
catch(ArgumentException){rejected=true;}
Check(rejected,"An excluded sixth-axis command cannot be silently discarded");
var host=MultiAxisProtocol.Encode(five,new[]{0,1,2,3,4},false);
Check(host[0].Code==0x50 && host[0].Data.SequenceEqual(new byte[]{4,31}),"Protocol 4 can explicitly select nodes 1..5");
Check(host.All(c=>c.Data.Length<=8),"Every five-axis command fits classic CAN");
Check(host.Where(c=>c.Code==0x53).All(c=>c.Data[0]>=1 && c.Data[0]<=5),"Five-axis subset omits node 6");
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
var sixTargets=new[]{new double[]{.5,-.5,.5,.5,.5,30},new double[]{-.5,-.5,-.5,-.5,-.5,-45},
    new double[]{.75,-.75,-.5,-.25,.25,60},new double[6]};
var six=ContinuousPathPlanner.Build(model,new double[6],sixTargets,new[]{1.0,1.0,1.0,1.0},sixScale,false);
Check(six.Start.Length==6 && six.Segments.All(s=>s.End.Length==6 && s.EndVelocity.Length==6),"Six motor axes use the shared segment clock");
Check(six.Samples.Any(q=>q[5]>20) && six.Samples.Any(q=>q[5]<-20),"Real J6 travels in both directions");
Check(six.MaximumDeviationMm<=.9,"Six-axis geometric tolerance");
for(int i=1;i<six.Segments.Count;++i)for(int j=0;j<6;++j) {
    var a=six.Segments[i-1];var b=six.Segments[i];
    Check(Math.Abs(a.Controls[5][j]-b.Controls[0][j])<1e-8,"Six-axis position continuity");
    // Drives execute durations rounded to whole milliseconds, not raw floats.
    double ta=Math.Round((double)(a.Duration*1000f),MidpointRounding.AwayFromZero)/1000;
    double tb=Math.Round((double)(b.Duration*1000f),MidpointRounding.AwayFromZero)/1000;
    Check(Math.Abs(5*(a.Controls[5][j]-a.Controls[4][j])/ta-5*(b.Controls[1][j]-b.Controls[0][j])/tb)<1e-8,"Six-axis velocity continuity");
}
foreach(double angle in new[]{-180.0,180.0}) {
    var q=new double[]{0,0,0,0,0,angle};
    Check(ContinuousPathPlanner.Build(model,new double[6],new[]{q},new[]{2.0},sixScale,false).Segments.Count==1,"J6 endpoint is reachable");
}
rejected=false;
try { ContinuousPathPlanner.Build(model,new double[6],new[]{new double[]{0,0,0,0,0,180.1}},new[]{2.0},sixScale,false); }
catch(ArgumentException) {rejected=true;}
Check(rejected,"J6 outside -180..180 is rejected");
var sixHost=MultiAxisProtocol.Encode(six,new[]{0,1,2,3,4,5},false);
Check(six.ValidationPaths().Count()==1,"Full curve covers its identical first-pass prefix without a second validation");
var distinctFirstPass=new ContinuousPathPlanner.Plan {
    Samples=new(){new double[6]}, FirstSamples=new(){new double[]{0,0,.5,0,0,0}} };
Check(distinctFirstPass.ValidationPaths().Count()==2,"A different first-pass curve is still validated separately");
Check(sixHost[0].Data.SequenceEqual(new byte[]{4,63}),"Protocol 4 selects all six axes");
Check(sixHost.Count(c=>c.Code==0x53)==six.Segments.Count*12,"All six targets and velocities are encoded");
Check(sixHost.Where(c=>c.Code==0x53).Any(c=>c.Data[0]==6),"Node6 receives its own target");
Check(sixHost.All(c=>c.Data.Length<=8),"Six-axis frames fit classic CAN");
File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"six-axis-host.txt"),sixHost.Select(c=>$"{c.Code:X2} {Convert.ToHexString(c.Data)}"));
File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"six-axis-samples.json"),System.Text.Json.JsonSerializer.Serialize(six.Samples));
for(int axis=0;axis<6;++axis) {
    var singleTarget=new double[6]; singleTarget[axis]=.5;
    var single=ContinuousPathPlanner.Build(model,new double[6],new[]{singleTarget},new[]{1.0},
        new[]{sixScale[axis]},false,jointIndices:new[]{axis});
    var singleHost=MultiAxisProtocol.Encode(single,new[]{axis},false);
    Check(MotorCoordinateMap.Direction(axis)==-originalDirections[axis],$"J{axis+1} reverses its original motor direction");
    var targetFrame=singleHost.Single(c=>c.Code==0x53 && c.Data[1]==0);
    double sentRevolutions=BitConverter.ToSingle(targetFrame.Data,4);
    Check(Math.Abs(sentRevolutions-.5*sixScale[axis])<1e-7,$"J{axis+1} wire target applies the new sign once");
    Check(Math.Abs(sentRevolutions*360/(MotorCoordinateMap.Direction(axis)*reductions[axis])-.5)<1e-5,
        $"J{axis+1} encoder inverse recovers the positive model angle");
    Check(single.ValidationPaths().Count()==1,$"J{axis+1} identical single move is checked once");
    Check(singleHost[0].Data.SequenceEqual(new byte[]{4,(byte)(1<<axis)}),$"J{axis+1} single-axis mask");
    Check(singleHost.Where(c=>c.Code==0x51 || c.Code==0x53).All(c=>c.Data[0]==axis+1),
        $"J{axis+1} is the only node receiving start, target and velocity");
    Check(single.Samples.All(q=>Enumerable.Range(0,6).Where(i=>i!=axis).All(i=>q[i]==0)),
        $"J{axis+1} single-axis samples hold other joints fixed");
    Check(singleHost.Last().Code==0x3A && singleHost.Last().Data.SequenceEqual(new byte[]{1}),
        $"J{axis+1} runs once instead of repeating");
    if(axis==2) {
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"j3-only-host.txt"),
            singleHost.Select(c=>$"{c.Code:X2} {Convert.ToHexString(c.Data)}"));
        var returnPlan=ContinuousPathPlanner.Build(model,singleTarget,new[]{new double[6]},new[]{1.0},
            new[]{sixScale[axis]},false,jointIndices:new[]{axis});
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"j3-return-only-host.txt"),
            MultiAxisProtocol.Encode(returnPlan,new[]{axis},false).Select(c=>$"{c.Code:X2} {Convert.ToHexString(c.Data)}"));
    }
}
// Every nonempty combination must encode only its selected nodes, including
// explicit zero targets, while retaining the other joints' current positions.
for(int mask=1;mask<64;mask++) {
    int[] axes=Enumerable.Range(0,6).Where(i=>(mask&(1<<i))!=0).ToArray();
    var start=Enumerable.Repeat(.25,6).ToArray();
    var target=start.ToArray();
    foreach(int axis in axes) target[axis]=axis%2==0 ? 0 : -.5;
    var subset=ContinuousPathPlanner.Build(model,start,new[]{target},new[]{1.0},
        axes.Select(i=>sixScale[i]).ToArray(),false,jointIndices:axes);
    var commands=MultiAxisProtocol.Encode(subset,axes,false);
    Check(commands[0].Data.SequenceEqual(new byte[]{4,(byte)mask}),$"Subset {mask:X2} selects the exact axis mask");
    Check(commands.Where(c=>c.Code==0x51).Select(c=>(int)c.Data[0]).SequenceEqual(axes.Select(i=>i+1)),
        $"Subset {mask:X2} loads only selected start positions");
    var ends=commands.Where(c=>c.Code==0x53 && c.Data[1]==0).ToArray();
    Check(ends.Length==axes.Length && ends.Select(c=>(int)c.Data[0]).SequenceEqual(axes.Select(i=>i+1)),
        $"Subset {mask:X2} sends no excluded motor targets");
    Check(ends.All(c=>Math.Abs(BitConverter.ToSingle(c.Data,4)-target[c.Data[0]-1]*sixScale[c.Data[0]-1])<1e-7),
        $"Subset {mask:X2} preserves zero targets and motor directions");
    Check(subset.Samples.All(q=>Enumerable.Range(0,6).Where(i=>!axes.Contains(i)).All(i=>q[i]==start[i])),
        $"Subset {mask:X2} holds excluded joints fixed throughout collision samples");
}
// Regression: the recorded J1/J2/J3 loop used to append a duplicate zero
// endpoint and generated a stationary block at the end of every lap.
var threeAxes=new[]{0,1,2};
var recordedTargets=new[]{new double[]{10,10,10,0,0,0},new double[]{-10,-10,30,0,0,0},new double[6],new double[6]};
var smoothed=ContinuousPathPlanner.Build(model,new double[6],recordedTargets,new[]{2.0,3,4,.05},
    threeAxes.Select(i=>sixScale[i]).ToArray(),true,toleranceMm:3,jointIndices:threeAxes);
Check(smoothed.Segments.Count==5,"Three recorded points plus duplicate loop endpoint contain no dwell segment");
Check(smoothed.Segments.Take(smoothed.Segments.Count-1).All(s=>s.EndVelocity.Any(v=>Math.Abs(v)>1e-5)),
    "Every non-reversing interior vector corner retains pass-through velocity");
Check(smoothed.MaximumDeviationMm<=2.7 && smoothed.Samples.All(model.WithinLimits),
    "Adjustable smoothing retains its geometric allowance and model limits");
for(int i=0;i<smoothed.Segments.Count;i++) {
    var s=smoothed.Segments[i];
    Check(i==0 || !s.End.SequenceEqual(smoothed.Segments[i-1].End),"No repeated stationary endpoint block");
    foreach(int axis in threeAxes) {
        var c=s.Controls.Select(q=>q[axis]*sixScale[axis]).ToArray();
        var v=Enumerable.Range(0,5).Select(k=>5*(c[k+1]-c[k])/s.Duration).ToArray();
        var a=Enumerable.Range(0,4).Select(k=>4*(v[k+1]-v[k])/s.Duration).ToArray();
        var j=Enumerable.Range(0,3).Select(k=>3*(a[k+1]-a[k])/s.Duration).ToArray();
        for(int sample=0;sample<=100;sample++) {
            double u=sample/100.0;
            double jerk=j[0]*(1-u)*(1-u)+2*j[1]*u*(1-u)+j[2]*u*u;
            Check(Math.Abs(jerk)<=50.001,"Actual quantized motor trajectory obeys the jerk limit");
        }
    }
}
var duplicateInterior=ContinuousPathPlanner.Build(model,new double[6],
    new[]{new double[6],recordedTargets[0],recordedTargets[0],recordedTargets[1],new double[6]},
    new[]{.05,2,.05,3,4},threeAxes.Select(i=>sixScale[i]).ToArray(),true,toleranceMm:3,jointIndices:threeAxes);
Check(duplicateInterior.Segments.Select(s=>s.Duration).SequenceEqual(smoothed.Segments.Select(s=>s.Duration)),
    "Leading and interior duplicate recordings do not change motion timing");
rejected=false;
try { ContinuousPathPlanner.Build(model,new double[6],new[]{new double[6]},new[]{1.0},sixScale,false); }
catch(ArgumentException) { rejected=true; }
Check(rejected,"All-identical points do not upload an endless stationary loop");
File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"smooth-three-axis-host.txt"),
    MultiAxisProtocol.Encode(smoothed,threeAxes,true).Select(c=>$"{c.Code:X2} {Convert.ToHexString(c.Data)}"));
using(var wire=new StreamWriter(Path.Combine(AppContext.BaseDirectory,"smooth-three-axis-wire.txt"))) {
    string F(float v)=>v.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
    wire.WriteLine($"3 {smoothed.Segments.Count} 1 "+string.Join(" ",smoothed.Start.Select(F)));
    foreach(var s in smoothed.Segments) wire.WriteLine(F(s.Duration)+" "+string.Join(" ",s.End.Select(F))+" "+string.Join(" ",s.EndVelocity.Select(F)));
}
var fastSettings=new SmallProject.Configs.JConfiguration();
Check(fastSettings.TryValidateMotion(out _) && fastSettings.Speed==60 &&
    fastSettings.MotorMaxAcceleration==SmallProject.Configs.JConfiguration.DriverCompatibleMaxAcceleration &&
    new[]{fastSettings.ReductionJ1,fastSettings.ReductionJ2,fastSettings.ReductionJ3,fastSettings.ReductionJ4,fastSettings.ReductionJ5,fastSettings.ReductionJ6}
        .SequenceEqual(new[]{50,100,100,30,30,1}),"Installed ratios and faster motion defaults");
var installedReductions=new double[]{50,100,100,30,30,1};
ContinuousPathPlanner.Plan Profile(SmallProject.Configs.JConfiguration settings) {
    var durations=new List<double>(); var previous=new double[6];
    foreach(var target in recordedTargets) {
        durations.Add(MotionTiming.MinimumSeconds(previous,target,installedReductions,threeAxes,settings));
        previous=target;
    }
    return ContinuousPathPlanner.Build(model,new double[6],recordedTargets,durations,
        threeAxes.Select(i=>MotorCoordinateMap.Direction(i)*installedReductions[i]/360).ToArray(),true,
        toleranceMm:3,jointIndices:threeAxes,maxVelocity:settings.MotorMaxVelocity,
        maxAcceleration:settings.MotorMaxAcceleration,maxJerk:settings.MotorMaxJerk);
}
var previousSettings=new SmallProject.Configs.JConfiguration {
    Speed=20,MotorMaxVelocity=4.2,MotorMaxAcceleration=6.25,MotorMaxJerk=50 };
var slowProfile=Profile(previousSettings);
var fastProfile=Profile(fastSettings);
double oldSeconds=slowProfile.Segments.Sum(s=>(double)s.Duration),fastSeconds=fastProfile.Segments.Sum(s=>(double)s.Duration);
Check(fastSeconds<oldSeconds*.6,"The same recorded loop is materially faster with the installed ratios");
var reportedTargets=new[]{new double[]{50,20,40,0,0,0},new double[]{20,-20,60,0,0,0},new double[6]};
var reportedTimes=new List<double>();
var reportedPrevious=new double[6];
foreach(var target in reportedTargets.Concat(new[]{new double[6]})) {
    reportedTimes.Add(MotionTiming.MinimumSeconds(reportedPrevious,target,installedReductions,threeAxes,fastSettings));
    reportedPrevious=target;
}
var reportedLoop=ContinuousPathPlanner.Build(model,new double[6],reportedTargets.Concat(new[]{new double[6]}).ToArray(),
    reportedTimes,threeAxes.Select(i=>MotorCoordinateMap.Direction(i)*installedReductions[i]/360).ToArray(),true,
    toleranceMm:20,jointIndices:threeAxes,maxVelocity:fastSettings.MotorMaxVelocity,
    maxAcceleration:fastSettings.MotorMaxAcceleration,maxJerk:fastSettings.MotorMaxJerk);
Check(reportedLoop.Segments.Count==5 && reportedLoop.Segments[^1].End.SequenceEqual(reportedLoop.Start),
    "Reported three-point 20 mm loop is a closed five-block path");
var reportedHost=MultiAxisProtocol.Encode(reportedLoop,threeAxes,true);
Check(reportedHost.Count(c=>c.Code==0x54)==5 && reportedHost.Last().Data.SequenceEqual(new byte[]{2}),
    "Reported five-block loop is completely committed before repeat start");
File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"reported-three-point-host.txt"),
    reportedHost.Select(c=>$"{c.Code:X2} {Convert.ToHexString(c.Data)}"));
foreach(var segment in fastProfile.Segments) foreach(int axis in threeAxes) {
    var c=segment.Controls.Select(q=>q[axis]*installedReductions[axis]/360).ToArray();
    var v=Enumerable.Range(0,5).Select(k=>5*(c[k+1]-c[k])/segment.Duration).ToArray();
    var a=Enumerable.Range(0,4).Select(k=>4*(v[k+1]-v[k])/segment.Duration).ToArray();
    var j=Enumerable.Range(0,3).Select(k=>3*(a[k+1]-a[k])/segment.Duration).ToArray();
    double EvaluateControls(double[] b,double u) { var row=b.ToArray();for(int n=row.Length-1;n>0;n--) for(int k=0;k<n;k++) row[k]=row[k]*(1-u)+row[k+1]*u;return row[0]; }
    for(int sample=0;sample<=100;sample++) {
        double u=sample/100.0;
        Check(Math.Abs(EvaluateControls(v,u))<=15.001 && Math.Abs(EvaluateControls(a,u))<=50.001 &&
            Math.Abs(EvaluateControls(j,u))<=500.001,"Faster quantized path retains the configured dynamic bounds");
    }
}
var unrelatedTarget=new double[]{0,0,0,0,0,180};
Check(MotionTiming.MinimumSeconds(new double[6],unrelatedTarget,installedReductions,new[]{0},fastSettings)==.05,
    "An excluded joint does not slow a selected-axis move");
foreach(double invalid in new[]{0.0,-1,double.NaN,double.PositiveInfinity,31}) {
    var bad=new SmallProject.Configs.JConfiguration{MotorMaxVelocity=invalid};
    Check(!bad.TryValidateMotion(out _),"Invalid motor limits are rejected before planning");
}
var excessiveAcceleration=new SmallProject.Configs.JConfiguration{MotorMaxAcceleration=24};
Check(!excessiveAcceleration.TryValidateMotion(out _),"Acceleration above the verified drive limit is rejected");
File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"fast-three-axis-host.txt"),
    MultiAxisProtocol.Encode(fastProfile,threeAxes,true).Select(c=>$"{c.Code:X2} {Convert.ToHexString(c.Data)}"));
using(var wire=new StreamWriter(Path.Combine(AppContext.BaseDirectory,"fast-three-axis-wire.txt"))) {
    string F(float v)=>v.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
    wire.WriteLine($"3 {fastProfile.Segments.Count} 1 "+string.Join(" ",fastProfile.Start.Select(F)));
    foreach(var s in fastProfile.Segments) wire.WriteLine(F(s.Duration)+" "+string.Join(" ",s.End.Select(F))+" "+string.Join(" ",s.EndVelocity.Select(F)));
}
Console.WriteLine($"Installed-ratio three-point loop: {oldSeconds:F3}s -> {fastSeconds:F3}s ({oldSeconds/fastSeconds:F2}x planned speed)");
Console.WriteLine($"PASS: {passed} kinematics/planning/protocol checks; six-axis geometric bound {six.MaximumDeviationMm:F4} mm; fixture: six-axis-host.txt");
