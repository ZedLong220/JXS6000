using SmallProject.Devices.Arm.Kinematic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

if (args.Length < 2) throw new ArgumentException("Usage: FixedArcGenerator <repository root> <validation output directory>");
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
string root=Path.GetFullPath(args[0]), output=Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
string urdf=Path.Combine(root,"HMIcode/SmallProject-6000/SmallProject/Viewer/URDF/jxs6000.urdf");
var model=new UrdfKinematics(urdf);
double[][] knots={new[]{-15.0,75,-300,65,-90,0},new[]{-50.28666400864631,90,-280,30,-90,0},new[]{-85.57332801729262,70,-300,70,-90,0}};
double[] scales=new[]{50.0,100,100,30,30,1}.Select((r,i)=>r*MotorCoordinateMap.Direction(i)/360).ToArray();
double[] Position(double[] q) { var p=model.Forward(q);return new[]{p.X,p.Y,p.Z}; }
double[] Sub(double[] a,double[] b)=>a.Zip(b,(x,y)=>x-y).ToArray();
double[] Add(double[] a,double[] b)=>a.Zip(b,(x,y)=>x+y).ToArray();
double[] Scale(double[] a,double k)=>a.Select(x=>x*k).ToArray();
double Dot(double[] a,double[] b)=>a.Zip(b,(x,y)=>x*y).Sum();
double Norm(double[] a)=>Math.Sqrt(Dot(a,a));
double[] Unit(double[] a)=>Scale(a,1/Norm(a));
var a=Position(knots[0]);var b=Position(knots[1]);var c=Position(knots[2]);
// Recompute the circle through all three tool positions. Changing the endpoint
// joints changes the circle; the old diameter midpoint would miss the via pose.
var chord=Sub(c,a);var u=Unit(chord);var toMiddle=Sub(b,a);
var v=Unit(Sub(toMiddle,Scale(u,Dot(toMiddle,u))));
double bx=Dot(toMiddle,u),by=Dot(toMiddle,v),cx=Norm(chord)/2;
if(Math.Abs(by)<1e-8)throw new Exception("Arc points are collinear");
var center=Add(a,Add(Scale(u,cx),Scale(v,(Dot(toMiddle,toMiddle)/2-bx*cx)/by)));
double radius=Norm(Sub(a,center));
var ex=Unit(Sub(a,center));var ey=Unit(Sub(Sub(b,center),Scale(ex,Dot(Sub(b,center),ex))));
double Phase(double[] p) {var d=Sub(p,center);double t=Math.Atan2(Dot(d,ey),Dot(d,ex));return t<0?t+2*Math.PI:t;}
double arcRadians=Phase(c),middle=Phase(b)/arcRadians;
if(middle<=0 || middle>=1)throw new Exception("Requested middle pose is outside the selected arc");
double invariant=Position(new[]{0.0,75,-300,65,-90,0})[0];
double[] Target(double s)=>Add(center,Scale(Add(Scale(ex,Math.Cos(arcRadians*s)),Scale(ey,Math.Sin(arcRadians*s))),radius));
double[] Solve(double s) {
    var xyz=Target(s);double planar=Math.Sqrt(xyz[0]*xyz[0]+xyz[1]*xyz[1]);
    double q1=(-Math.Acos(Math.Clamp(invariant/planar,-1,1))-Math.Atan2(xyz[1],xyz[0]))*180/Math.PI;
    var desired=model.Forward(new[]{q1,75,-300,65,-90,0});desired.X=xyz[0];desired.Y=xyz[1];desired.Z=xyz[2];
    var lo=model.LowerDegrees.ToArray();var hi=model.UpperDegrees.ToArray();
    lo[0]=hi[0]=q1;lo[4]=hi[4]=-90;lo[5]=hi[5]=0;
    if(!model.TryInverse(desired,knots[1],out var q,lo,hi)) throw new Exception($"IK failed at {s}");
    // Refine the two planar arm coordinates with constant wrist pitch. This
    // removes the UI solver's 0.02 mm stopping tolerance from tangent estimates.
    q[3]=-160-q[1]-q[2];
    for(int iteration=0;iteration<8;iteration++) {
        var residual=Sub(xyz,Position(q));if(Norm(residual)<1e-9) break;
        const double h=1e-4;var j=new double[2][];
        for(int k=0;k<2;k++) {var test=q.ToArray();test[k+1]+=h;test[3]-=h;j[k]=Scale(Sub(Position(test),Position(q)),1/h);}
        double aa=Dot(j[0],j[0]),ab=Dot(j[0],j[1]),bb=Dot(j[1],j[1]),det=aa*bb-ab*ab;
        if(Math.Abs(det)<1e-12)throw new Exception("Singular arm configuration");
        double ra=Dot(j[0],residual),rb=Dot(j[1],residual);
        q[1]+=(ra*bb-rb*ab)/det;q[2]+=(rb*aa-ra*ab)/det;q[3]=-160-q[1]-q[2];
    }
    if(!model.WithinLimits(q) || Norm(Sub(Position(q),xyz))>1e-7)throw new Exception("Refined IK is invalid");
    return q;
}
// A cosine phase law shortens the slow launch/landing portions. Each drive
// block still supplies zero endpoint acceleration and passes the unchanged
// 15 rev/s, 15 rev/s^2 and 500 rev/s^3 limits after wire quantization.
double InverseSmooth(double s)=>Math.Acos(1-2*s)/Math.PI;
double[] Tangent(double s) {
    const double h=1e-4;
    var m2=Solve(s-2*h);var m1=Solve(s-h);var p1=Solve(s+h);var p2=Solve(s+2*h);
    return Enumerable.Range(0,6).Select(i=>(m2[i]-8*m1[i]+8*p1[i]-p2[i])/(12*h)).ToArray();
}
double Bound(double[] b,int depth=4) {
    if(depth==0)return b.Max(Math.Abs);
    var row=b.ToArray();var left=new double[b.Length];var right=new double[b.Length];left[0]=row[0];right[^1]=row[^1];
    for(int k=1;k<b.Length;k++){for(int i=0;i<b.Length-k;i++)row[i]=(row[i]+row[i+1])*.5;left[k]=row[0];right[^(k+1)]=row[b.Length-k-1];}
    return Math.Max(Bound(left,depth-1),Bound(right,depth-1));
}
int Quantize(float rev)=>(int)Math.Round((double)(rev*51200f),MidpointRounding.AwayFromZero);
const int count=20, half=count/2;
var progress=Enumerable.Range(0,count+1).Select(i=>i<=half?middle*i/half:middle+(1-middle)*(i-half)/half).ToArray();
var q=progress.Select(Solve).ToArray();
foreach(var checkpoint in new[]{(index:0,knot:0),(index:half,knot:1),(index:count,knot:2)})
    if(q[checkpoint.index].Zip(knots[checkpoint.knot],(x,y)=>Math.Abs(x-y)).Max()>1e-5)
        throw new Exception($"Circle does not reproduce requested checkpoint {checkpoint.knot}");
q[0]=knots[0];q[half]=knots[1];q[^1]=knots[2];
var tangent=progress.Select(Tangent).ToArray();
float[][] positions=q.Select(p=>p.Select((v,i)=>(float)(v*scales[i])).ToArray()).ToArray();
float[][] velocities=Array.Empty<float[]>();float[] durations=Array.Empty<float>();
double total=3,stretch=1;double[] peakV=new double[6],peakA=new double[6],peakJ=new double[6];
for(int attempt=0;attempt<20;attempt++) {
    var time=progress.Select(s=>Math.Round(InverseSmooth(s)*total*1000)/1000).ToArray();
    durations=Enumerable.Range(0,count).Select(i=>(float)(time[i+1]-time[i])).ToArray();
    velocities=Enumerable.Range(0,count+1).Select(i=>{
        double t=time[i]/total,rate=Math.PI*.5*Math.Sin(Math.PI*t)/total;
        return tangent[i].Select((v,j)=>(float)(v*scales[j]*rate)).ToArray();
    }).ToArray();
    Array.Clear(velocities[0]);Array.Clear(velocities[^1]);
    // Monotone Bezier controls for J1. Quintic zero-acceleration knots can
    // otherwise reverse slightly at rest even when endpoint velocities have
    // the correct sign. Scale the whole tangent to retain the tool direction.
    for(int i=0;i<count;i++) {
        double distance=Math.Abs(positions[i+1][0]-positions[i][0]);
        double used=2*(Math.Abs(velocities[i][0])+Math.Abs(velocities[i+1][0]))*durations[i]/5;
        if(used>distance*.99) {
            float factor=(float)(distance*.99/used);
            for(int j=0;j<6;j++){velocities[i][j]*=factor;velocities[i+1][j]*=factor;}
        }
    }
    stretch=1;Array.Clear(peakV);Array.Clear(peakA);Array.Clear(peakJ);
    for(int i=0;i<count;i++) {
        double t=Math.Round(durations[i]*1000f)/1000;
        stretch=Math.Max(stretch,.05/t);
        for(int j=0;j<6;j++) {
            double p0=Quantize(positions[i][j])/51200.0,p1=Quantize(positions[i+1][j])/51200.0;
            double v0=Quantize(velocities[i][j])/51200.0,v1=Quantize(velocities[i+1][j])/51200.0;
            double[] bc={p0,p0+v0*t/5,p0+2*v0*t/5,p1-2*v1*t/5,p1-v1*t/5,p1};
            var vb=Enumerable.Range(0,5).Select(k=>5*(bc[k+1]-bc[k])/t).ToArray();
            var ab=Enumerable.Range(0,4).Select(k=>4*(vb[k+1]-vb[k])/t).ToArray();
            var jb=Enumerable.Range(0,3).Select(k=>3*(ab[k+1]-ab[k])/t).ToArray();
            peakV[j]=Math.Max(peakV[j],Bound(vb));peakA[j]=Math.Max(peakA[j],Bound(ab));peakJ[j]=Math.Max(peakJ[j],Bound(jb));
            stretch=Math.Max(stretch,Bound(vb)/(15*.9));stretch=Math.Max(stretch,Math.Sqrt(Bound(ab)/(15*.9)));stretch=Math.Max(stretch,Math.Cbrt(Bound(jb)/(500*.9)));
        }
    }
    if(stretch<=1.00001)break;
    total=Math.Ceiling(total*stretch*1.01*1000)/1000;
}
if(stretch>1.00001)throw new Exception("Timing did not converge");
string F(float v)=>v.ToString("G9",CultureInfo.InvariantCulture)+(v.ToString("G9",CultureInfo.InvariantCulture).IndexOfAny(new[]{'.','E','e'})<0?".0f":"f");
string hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(urdf))).ToLowerInvariant();
var header=new StringBuilder("/* Generated by Tools/FixedArcGenerator. Do not edit the table by hand.\n");
header.AppendLine($"   URDF SHA256: {hash}");
header.AppendLine("   Tool0 circular arc A(-15,75,-300,65,-90,0) -> B(-50.286664,90,-280,30,-90,0)");
header.AppendLine("   -> C(-85.573328,70,-300,70,-90,0); J2+J3+J4=-160 deg. */");
header.AppendLine("#ifndef FIXED_PICK_ARC_H\n#define FIXED_PICK_ARC_H");
header.AppendLine($"#define FIXED_ARC_SEGMENT_COUNT {count}U\n#define FIXED_ARC_DURATION_SEC {F((float)total)}");
header.AppendLine("typedef struct { float duration, target[4], velocity[4]; } FixedArcPoint;");
header.AppendLine("static const FixedArcPoint fixedArcPoints[FIXED_ARC_SEGMENT_COUNT] = {");
for(int i=0;i<count;i++)header.AppendLine($"    {{ {F(durations[i])}, {{ {string.Join(", ",positions[i+1].Take(4).Select(F))} }}, {{ {string.Join(", ",velocities[i+1].Take(4).Select(F))} }} }},");
header.AppendLine("};\n#endif");
File.WriteAllText(Path.Combine(root,"firmware/jixiebi-V6000/USER/Inc/fixed_pick_arc.h"),header.ToString());
string info=$$"""
// Generated by firmware/jixiebi-V6000/Tools/FixedArcGenerator.
namespace SmallProject.Devices.Arm.Kinematic
{
    public static class FixedPickArcInfo
    {
        public const double DurationSeconds = {{total.ToString("F3")}};
        public const string Description = "圆弧插补（{{total.ToString("F3")}}秒，{{(arcRadians*180/Math.PI).ToString("F3")}}°）：(-15,75,-300,65,-90,0) → 连续经过(-50.287,90,-280,30,-90,0) → (-85.573,70,-300,70,-90,0)，中途不停车";
    }
}
""";
File.WriteAllText(Path.Combine(root,"HMIcode/SmallProject-6000/SmallProject/Devices/Arm/Kinematic/FixedPickArcInfo.cs"),info+Environment.NewLine);
var plan=new{urdfSha256=hash,radiusMm=radius,arcAngleDegrees=arcRadians*180/Math.PI,center,ex,ey,middleProgress=middle,knots,progress,positions,velocities,durations,totalSeconds=total,peakVelocityRevPerSecond=peakV,peakAccelerationRevPerSecondSquared=peakA,peakJerkRevPerSecondCubed=peakJ};
File.WriteAllText(Path.Combine(output,"fixed-arc-plan.json"),JsonSerializer.Serialize(plan,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"Generated {count} segments, duration {total:F3}s. Conservative per-axis bounds:");
Console.WriteLine($"Circle radius {radius:F6} mm, arc {arcRadians*180/Math.PI:F6} deg; middle progress {middle:P6}");
for(int j=0;j<6;j++)Console.WriteLine($"J{j+1}: velocity {peakV[j]:F6} rev/s; acceleration {peakA[j]:F6} rev/s^2; jerk {peakJ[j]:F6} rev/s^3");
