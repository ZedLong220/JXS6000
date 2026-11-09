using SmallProject.Devices.Arm.Kinematic.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace SmallProject.Devices.Arm.Kinematic
{
    // Exact URDF chain: metres internally; public poses in mm and RPY degrees.
    public sealed class UrdfKinematics
    {
        private const double Rad = Math.PI / 180, RotationWeight = .2;
        private sealed class Joint
        {
            public string Child = "";
            public double[] Origin = Identity(), Axis = { 1, 0, 0 };
            public int Index = -1;
        }
        private readonly List<Joint> chain = new();
        public double[] LowerDegrees { get; } = new double[6];
        public double[] UpperDegrees { get; } = new double[6];

        public UrdfKinematics(string path)
        {
            var nodes = XDocument.Load(path).Root!.Elements("joint").ToList();
            string parent = "base_link";
            var visited = new HashSet<string>();
            int count = 0;
            while (parent != "tool0")
            {
                if (!visited.Add(parent)) throw new InvalidDataException("URDF 存在环路");
                var node = nodes.Single(j => (string?)j.Element("parent")?.Attribute("link") == parent);
                var origin = node.Element("origin");
                var joint = new Joint {
                    Child = (string)node.Element("child")!.Attribute("link")!,
                    Origin = FromRpy(Tuple((string?)origin?.Attribute("xyz"), "0 0 0"), Tuple((string?)origin?.Attribute("rpy"), "0 0 0")) };
                if ((string?)node.Attribute("type") != "fixed")
                {
                    if ((string?)node.Attribute("name") != $"joint_{count + 1}" || count >= 6)
                        throw new InvalidDataException("需要 joint_1 到 joint_6 的六轴模型");
                    joint.Index = count;
                    joint.Axis = Tuple((string?)node.Element("axis")?.Attribute("xyz"), "1 0 0");
                    double norm = Norm(joint.Axis);
                    if (norm < 1e-12) throw new InvalidDataException("URDF 关节轴无效");
                    joint.Axis = joint.Axis.Select(v => v / norm).ToArray();
                    LowerDegrees[count] = double.Parse((string)node.Element("limit")!.Attribute("lower")!, CultureInfo.InvariantCulture) / Rad;
                    UpperDegrees[count++] = double.Parse((string)node.Element("limit")!.Attribute("upper")!, CultureInfo.InvariantCulture) / Rad;
                }
                chain.Add(joint);
                parent = joint.Child;
            }
            if (count != 6) throw new InvalidDataException("URDF 不是六轴模型");
        }

        public bool WithinLimits(double[] q) => q != null && q.Length == 6 &&
            q.Select((v, i) => double.IsFinite(v) && v >= LowerDegrees[i] - 1e-6 && v <= UpperDegrees[i] + 1e-6).All(v => v);

        // Sum of downstream link translations bounds the TCP lever arm at
        // every configuration, including fixed tool transforms (millimetres).
        public double[] MaximumToolRadiiMillimetres()
        {
            var radii = new double[6];
            double radius = 0;
            for (int i = chain.Count - 1; i >= 0; --i)
            {
                var joint = chain[i];
                if (joint.Index >= 0) radii[joint.Index] = radius;
                radius += 1000 * Math.Sqrt(joint.Origin[3]*joint.Origin[3] +
                    joint.Origin[7]*joint.Origin[7] + joint.Origin[11]*joint.Origin[11]);
            }
            return radii;
        }

        public double[] ToolTransform(double[] degrees)
        {
            if (degrees == null || degrees.Length != 6 || degrees.Any(v => !double.IsFinite(v))) throw new ArgumentException("需要六个有限关节角度");
            return Evaluate(degrees.Select(v => v * Rad).ToArray(), out _, out _);
        }

        public Pose6D_t Forward(double[] degrees)
        {
            var t = ToolTransform(degrees);
            double pitch = Math.Atan2(-t[8], Math.Sqrt(t[0]*t[0] + t[4]*t[4]));
            double roll = Math.Abs(Math.Cos(pitch)) < 1e-9 ? 0 : Math.Atan2(t[9], t[10]);
            double yaw = Math.Abs(Math.Cos(pitch)) < 1e-9 ? Math.Atan2(-t[1], t[5]) : Math.Atan2(t[4], t[0]);
            return new Pose6D_t(t[3]*1000, t[7]*1000, t[11]*1000, roll/Rad, pitch/Rad, yaw/Rad) {
                R = new[] { t[0],t[1],t[2],t[4],t[5],t[6],t[8],t[9],t[10] }, hasR = true };
        }

        // Damped least squares supports this offset wrist. The old spherical
        // wrist analytic solver does not describe the confirmed URDF geometry.
        public bool TryInverse(Pose6D_t pose, double[] seedDegrees, out double[] result,
            double[]? lowerDegrees = null, double[]? upperDegrees = null)
        {
            result = Array.Empty<double>();
            if (new[] {pose.X,pose.Y,pose.Z,pose.A,pose.B,pose.C}.Any(v => !double.IsFinite(v)) || seedDegrees.Length != 6 || seedDegrees.Any(v => !double.IsFinite(v))) return false;
            var lower = LowerDegrees.Select((v,i) => Math.Max(v,lowerDegrees?[i] ?? v)*Rad).ToArray();
            var upper = UpperDegrees.Select((v,i) => Math.Min(v,upperDegrees?[i] ?? v)*Rad).ToArray();
            if (lower.Where((v,i) => v > upper[i]).Any()) return false;
            var target = FromRpy(new[] {pose.X/1000,pose.Y/1000,pose.Z/1000}, new[] {pose.A*Rad,pose.B*Rad,pose.C*Rad});
            var seeds = new List<double[]> { seedDegrees.Select(v => v*Rad).ToArray(), new double[6] };
            for (int k=0;k<12;k++) seeds.Add(Enumerable.Range(0,6).Select(i => lower[i]+(upper[i]-lower[i])*(.5+.38*Math.Sin((k+1)*(i+2)*1.618))).ToArray());
            foreach (var seed in seeds)
            {
                var q = seed.Select((v,i) => Math.Clamp(v,lower[i],upper[i])).ToArray();
                double damping = .003;
                for (int iteration=0;iteration<250;iteration++)
                {
                    var current = Evaluate(q,out var axes,out var pivots);
                    var error = Error(target,current);
                    if (Norm(error.Take(3).ToArray()) < .00002 && Norm(error.Skip(3).ToArray())/RotationWeight < .0001)
                    {
                        result = q.Select(v => v/Rad).ToArray();
                        return true;
                    }
                    var j = new double[6,6];
                    for (int i=0;i<6;i++)
                    {
                        if (upper[i]-lower[i]<1e-12) continue; // physically fixed DOF
                        var velocity = Cross(axes[i],new[] {current[3]-pivots[i][0],current[7]-pivots[i][1],current[11]-pivots[i][2]});
                        for (int row=0;row<3;row++) { j[row,i]=velocity[row]; j[row+3,i]=axes[i][row]*RotationWeight; }
                    }
                    var normal = new double[6,6]; var rhs = new double[6];
                    for (int i=0;i<6;i++)
                    {
                        for (int row=0;row<6;row++) rhs[i]+=j[row,i]*error[row];
                        for (int col=0;col<6;col++) for (int row=0;row<6;row++) normal[i,col]+=j[row,i]*j[row,col];
                        normal[i,i]+=damping*damping;
                    }
                    if (!SolveLinear(normal,rhs,out var delta)) break;
                    double scale = Math.Min(1,.25/Math.Max(1e-12,delta.Max(v => Math.Abs(v))));
                    var candidate = q.Select((v,i) => Math.Clamp(v+delta[i]*scale,lower[i],upper[i])).ToArray();
                    if (Squared(Error(target,Evaluate(candidate,out _,out _))) < Squared(error)) { q=candidate; damping=Math.Max(.00001,damping*.7); }
                    else damping=Math.Min(.5,damping*2);
                }
            }
            return false;
        }

        private double[] Evaluate(double[] q,out double[][] axes,out double[][] pivots)
        {
            axes=new double[6][]; pivots=new double[6][]; var t=Identity();
            foreach (var joint in chain)
            {
                t=Multiply(t,joint.Origin);
                if (joint.Index<0) continue;
                int i=joint.Index;
                pivots[i]=new[] {t[3],t[7],t[11]};
                axes[i]=Enumerable.Range(0,3).Select(row => t[row*4]*joint.Axis[0]+t[row*4+1]*joint.Axis[1]+t[row*4+2]*joint.Axis[2]).ToArray();
                t=Multiply(t,AxisRotation(joint.Axis,q[i]));
            }
            return t;
        }
        private static double[] Error(double[] target,double[] current)
        {
            var r=new double[9];
            for(int i=0;i<3;i++) for(int j=0;j<3;j++) for(int k=0;k<3;k++) r[i*3+j]+=target[i*4+k]*current[j*4+k];
            double angle=Math.Acos(Math.Clamp((r[0]+r[4]+r[8]-1)/2,-1,1));
            var axis=new[] {r[7]-r[5],r[2]-r[6],r[3]-r[1]};
            if(Math.PI-angle<1e-5)
            {
                int i=r[4]>r[0]?1:0; if(r[8]>r[i*3+i]) i=2;
                var a=new double[3]; a[i]=Math.Sqrt(Math.Max(0,(r[i*3+i]+1)/2));
                for(int j=0;j<3;j++) if(i!=j) a[j]=(r[i*3+j]+r[j*3+i])/(4*Math.Max(a[i],1e-12));
                axis=a.Select(v=>v*angle).ToArray();
            }
            else { double f=angle<1e-7?.5:angle/(2*Math.Sin(angle)); axis=axis.Select(v=>v*f).ToArray(); }
            return new[] {target[3]-current[3],target[7]-current[7],target[11]-current[11],axis[0]*RotationWeight,axis[1]*RotationWeight,axis[2]*RotationWeight};
        }
        private static bool SolveLinear(double[,] a,double[] b,out double[] result)
        {
            result=new double[6];
            for(int i=0;i<6;i++)
            {
                int pivot=i;
                for(int row=i+1;row<6;row++) if(Math.Abs(a[row,i])>Math.Abs(a[pivot,i])) pivot=row;
                if(Math.Abs(a[pivot,i])<1e-16) return false;
                for(int col=i;col<6;col++) (a[i,col],a[pivot,col])=(a[pivot,col],a[i,col]);
                (b[i],b[pivot])=(b[pivot],b[i]);
                for(int row=i+1;row<6;row++) { double f=a[row,i]/a[i,i]; for(int col=i;col<6;col++) a[row,col]-=f*a[i,col]; b[row]-=f*b[i]; }
            }
            for(int row=5;row>=0;row--) { double v=b[row]; for(int col=row+1;col<6;col++) v-=a[row,col]*result[col]; result[row]=v/a[row,row]; }
            return true;
        }
        private static double[] Identity() => new double[] {1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1};
        private static double[] Multiply(double[] a,double[] b)
        {
            var r=new double[16];
            for(int i=0;i<4;i++) for(int j=0;j<4;j++) for(int k=0;k<4;k++) r[i*4+j]+=a[i*4+k]*b[k*4+j];
            return r;
        }
        private static double[] FromRpy(double[] p,double[] rpy)
        {
            var r=Multiply(Multiply(AxisRotation(new double[] {0,0,1},rpy[2]),AxisRotation(new double[] {0,1,0},rpy[1])),AxisRotation(new double[] {1,0,0},rpy[0]));
            r[3]=p[0];r[7]=p[1];r[11]=p[2];return r;
        }
        private static double[] AxisRotation(double[] a,double angle)
        {
            double c=Math.Cos(angle),s=Math.Sin(angle),v=1-c,x=a[0],y=a[1],z=a[2];
            return new double[] {c+x*x*v,x*y*v-z*s,x*z*v+y*s,0,y*x*v+z*s,c+y*y*v,y*z*v-x*s,0,z*x*v-y*s,z*y*v+x*s,c+z*z*v,0,0,0,0,1};
        }
        private static double[] Cross(double[] a,double[] b)=>new[] {a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]};
        private static double Squared(double[] v)=>v.Sum(x=>x*x);
        private static double Norm(double[] v)=>Math.Sqrt(Squared(v));
        private static double[] Tuple(string? v,string fallback)=>(v??fallback).Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(s=>double.Parse(s,CultureInfo.InvariantCulture)).ToArray();
    }
}
