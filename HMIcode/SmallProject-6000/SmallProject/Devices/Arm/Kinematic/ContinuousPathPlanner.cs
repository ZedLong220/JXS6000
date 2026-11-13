using System;
using System.Collections.Generic;
using System.Linq;

namespace SmallProject.Devices.Arm.Kinematic
{
    // The reference is joint-linear interpolation mapped through the URDF,
    // not a Cartesian straight line. Knot acceleration is zero on both sides.
    public static class ContinuousPathPlanner
    {
        public sealed class Segment
        {
            public float Duration;
            public float[] End = new float[6], EndVelocity = new float[6];
            public double[][] Controls = Array.Empty<double[]>();
            public double[] ReferenceStart = Array.Empty<double>(), ReferenceEnd = Array.Empty<double>();
        }
        public sealed class Plan
        {
            public float[] Start = new float[6];
            public List<Segment> Segments = new();
            public List<double[]> Samples = new();
            public List<double[]> FirstSamples = new();
            public double MaximumDeviationMm;
            public IEnumerable<IReadOnlyList<double[]>> ValidationPaths()
            {
                // The full curve also covers the first pass when its samples
                // are identical to the prefix. A distinct first pass must
                // still be checked (for example, a different entry velocity).
                bool covered=FirstSamples.Count<=Samples.Count &&
                    FirstSamples.Select((q,i)=>q.SequenceEqual(Samples[i])).All(same=>same);
                if(!covered) yield return FirstSamples;
                yield return Samples;
            }
        }
        private const double Steps = 51200;
        private static double RoundStep(float value) => Math.Round((double)(value * (float)Steps), MidpointRounding.AwayFromZero) / Steps;

        public static Plan Build(UrdfKinematics model, double[] start,
            IReadOnlyList<double[]> targets, IReadOnlyList<double> initialSeconds,
            double[] motorScale, bool repeat, double toleranceMm = 1.0,
            double maxVelocity = 4.2, double maxAcceleration = 6.25, int[]? jointIndices = null)
        {
            var axes=jointIndices ?? new[] {0,1,2,3,4,5};
            int axisCount=axes.Length;
            if (axisCount==0 || axisCount>6 || axes.Distinct().Count()!=axisCount || axes.Any(i=>i<0 || i>=6))
                throw new ArgumentException("物理轴只能选J1～J6，且不能重复");
            if (targets.Count == 0 || targets.Count > 32 || initialSeconds.Count != targets.Count ||
                motorScale.Length != axisCount || motorScale.Any(v => !double.IsFinite(v) || Math.Abs(v) < .001) ||
                !double.IsFinite(toleranceMm) || toleranceMm <= 0 || !double.IsFinite(maxVelocity) ||
                !double.IsFinite(maxAcceleration) || maxVelocity <= 0 || maxAcceleration <= 0)
                throw new ArgumentException("连续路径参数无效");
            var q = new List<double[]> { start.ToArray() };
            q.AddRange(targets.Select(v => v.ToArray()));
            if (q.Any(v => !model.WithinLimits(v))) throw new ArgumentException("路径角度超出 URDF 范围");
            if (q.Any(v => Enumerable.Range(0, 6).Any(i => !axes.Contains(i) && Math.Abs(v[i]-start[i]) > 1e-8)))
                throw new ArgumentException("路径包含未参与的关节运动");
            if (repeat && q[^1].Zip(start).Any(p => Math.Abs(p.First-p.Second) > 1e-6))
                throw new ArgumentException("循环路径终点必须回到起点");
            if (repeat) q[^1] = start.ToArray();
            int originalCount = targets.Count;
            var originalTimes = initialSeconds.Select(t => Math.Ceiling(t*1000)/1000).ToArray();
            if (originalTimes.Any(t => !double.IsFinite(t) || t < .05 || t > 120)) throw new ArgumentException("路径段时间无效");
            var weights = model.MaximumToolRadiiMillimetres().Select(r => r*Math.PI/180).ToArray();
            var original = q.ToArray();
            var entry=original.Select(p=>p.ToArray()).ToArray();
            var exit=original.Select(p=>p.ToArray()).ToArray();
            var entryV=original.Select(_=>new double[6]).ToArray();
            var exitV=original.Select(_=>new double[6]).ToArray();
            var blendSeconds=new double[originalCount+1];
            // Round only a short neighbourhood of each corner. A polynomial
            // spanning a whole long leg would need an almost zero corner
            // speed to meet a 1 mm tolerance.
            for (int i=1;i<originalCount;++i)
            {
                var a=original[i].Zip(original[i-1],(x,y)=>x-y).ToArray();
                var b=original[i+1].Zip(original[i],(x,y)=>x-y).ToArray();
                double na=Math.Sqrt(a.Sum(v=>v*v)), nb=Math.Sqrt(b.Sum(v=>v*v));
                if (na<1e-9 || nb<1e-9) continue;
                double cosine=a.Zip(b,(x,y)=>x*y).Sum()/(na*nb);
                if (cosine < -.999999) continue; // physical reversal must stop
                if (cosine > .999999)
                {
                    double rate=Math.Min(na/originalTimes[i-1],nb/originalTimes[i]);
                    foreach(int j in axes) entryV[i][j]=exitV[i][j]=rate*a[j]/na;
                    continue;
                }
                double wa=Enumerable.Range(0,6).Sum(j=>weights[j]*Math.Abs(a[j]));
                double wb=Enumerable.Range(0,6).Sum(j=>weights[j]*Math.Abs(b[j]));
                double fa=Math.Min(.2,toleranceMm*.85/Math.Max(1e-12,wa));
                double fb=Math.Min(.2,toleranceMm*.85/Math.Max(1e-12,wb));
                for(int j=0;j<6;++j) { entry[i][j]-=a[j]*fa; exit[i][j]+=b[j]*fb; }
                double t=.05;
                for(int attempt=0;attempt<8;++attempt)
                {
                    foreach(int j in axes) { entryV[i][j]=2*a[j]*fa/t; exitV[i][j]=2*b[j]*fb/t; }
                    var controls=Controls(entry[i],exit[i],entryV[i],exitV[i],t);
                    double stretch=1;
                    for(int axis=0;axis<axisCount;++axis)
                    {
                        var c=controls.Select(v=>v[axes[axis]]*motorScale[axis]).ToArray();
                        var vb=Enumerable.Range(0,5).Select(k=>5*(c[k+1]-c[k])/t).ToArray();
                        var ab=Enumerable.Range(0,4).Select(k=>4*(vb[k+1]-vb[k])/t).ToArray();
                        stretch=Math.Max(stretch,Bound(vb,4)/(maxVelocity*.95));
                        stretch=Math.Max(stretch,Math.Sqrt(Bound(ab,4)/(maxAcceleration*.95)));
                    }
                    if(stretch<=1) break;
                    t=Math.Ceiling(t*stretch*1.01*1000)/1000;
                }
                blendSeconds[i]=t;
            }
            q=new List<double[]> { start.ToArray() };
            var velocities=new List<double[]> { new double[6] };
            var segmentTimes=new List<double>();
            var referenceA=new List<double[]>(); var referenceB=new List<double[]>();
            void Add(double[] end,double[] speed,double time,double[] a,double[] b)
            {
                q.Add(end); velocities.Add(speed); segmentTimes.Add(Math.Max(.05,Math.Ceiling(time*1000)/1000));
                referenceA.Add(a); referenceB.Add(b);
            }
            for(int i=0;i<originalCount;++i)
            {
                double length=Math.Sqrt(original[i+1].Zip(original[i],(x,y)=>(x-y)*(x-y)).Sum());
                double remaining=Math.Sqrt(entry[i+1].Zip(exit[i],(x,y)=>(x-y)*(x-y)).Sum());
                Add(entry[i+1],entryV[i+1],originalTimes[i]*(length<1e-9?1:remaining/length),original[i],original[i+1]);
                if(blendSeconds[i+1]>0) Add(exit[i+1],exitV[i+1],blendSeconds[i+1],original[i+1],original[i+1]);
            }
            int n=segmentTimes.Count;
            if(n>32) throw new ArgumentException("圆滑过渡后超过32段，请减少点位数量");
            var times=segmentTimes.ToArray(); var velocity=velocities.ToArray();
            var result = new Plan { Start=new float[axisCount] };
            for (int axis=0; axis<axisCount; ++axis) result.Start[axis]=(float)(start[axes[axis]]*motorScale[axis]);
            // Quantize exactly as the protocol and drive. Recheck the actual
            // wire curve, including the first lap's zero entry velocity.
            for (int attempt=0; attempt<12; ++attempt)
            {
                result.Segments.Clear(); result.Samples.Clear(); result.FirstSamples.Clear();
                result.MaximumDeviationMm=0;
                double stretch = 1;
                var actualStart = start.ToArray();
                for (int axis=0; axis<axisCount; ++axis) actualStart[axes[axis]]=RoundStep(result.Start[axis])/motorScale[axis];
                var actualPrevious = actualStart;
                var actualV0 = new double[6];
                for (int axis=0; axis<axisCount; ++axis) actualV0[axes[axis]]=RoundStep((float)(velocity[0][axes[axis]]*motorScale[axis]))/motorScale[axis];
                for (int i=0; i<n; ++i)
                {
                    var segment = new Segment { Duration=(float)times[i], End=new float[axisCount], EndVelocity=new float[axisCount] };
                    segment.ReferenceStart=referenceA[i]; segment.ReferenceEnd=referenceB[i];
                    var actualEnd = q[i+1].ToArray(); var actualV1 = new double[6];
                    for (int axis=0; axis<axisCount; ++axis)
                    {
                        segment.End[axis]=(float)(q[i+1][axes[axis]]*motorScale[axis]);
                        segment.EndVelocity[axis]=(float)(velocity[i+1][axes[axis]]*motorScale[axis]);
                        actualEnd[axes[axis]]=RoundStep(segment.End[axis])/motorScale[axis];
                        actualV1[axes[axis]]=RoundStep(segment.EndVelocity[axis])/motorScale[axis];
                        if (Math.Abs(segment.End[axis])>40000 || Math.Abs(result.Start[axis])>40000)
                            throw new ArgumentException("电机累计圈数超出驱动容量");
                    }
                    segment.Controls=Controls(actualPrevious,actualEnd,actualV0,actualV1,times[i]);
                    var first = i==0 ? Controls(actualPrevious,actualEnd,new double[6],actualV1,times[i]) : segment.Controls;
                    foreach (var controls in new[] { segment.Controls, first })
                    {
                        double deviation=Deviation(controls,referenceA[i],referenceB[i],weights);
                        result.MaximumDeviationMm=Math.Max(result.MaximumDeviationMm,deviation);
                        // Leave 0.1 mm for float polynomial arithmetic and
                        // 1 ms interpolation; this excludes servo error.
                        if (deviation>toleranceMm*.9 || controls.Any(c => !model.WithinLimits(c)))
                            throw new ArgumentException("量化后的路径超出容差或关节限位，请将端点移离限位");
                        for (int axis=0; axis<axisCount; ++axis)
                        {
                            var b=controls.Select(c => c[axes[axis]]*motorScale[axis]).ToArray();
                            var vb=Enumerable.Range(0,5).Select(k => 5*(b[k+1]-b[k])/times[i]).ToArray();
                            var ab=Enumerable.Range(0,4).Select(k => 4*(vb[k+1]-vb[k])/times[i]).ToArray();
                            stretch=Math.Max(stretch,Bound(vb,4)/(maxVelocity*.98));
                            stretch=Math.Max(stretch,Math.Sqrt(Bound(ab,4)/(maxAcceleration*.98)));
                        }
                    }
                    AddSamples(result.Samples,segment.Controls);
                    if (i==0) AddSamples(result.FirstSamples,first);
                    result.Segments.Add(segment);
                    actualPrevious=actualEnd; actualV0=actualV1;
                }
                if (stretch<=1.00001) return result;
                stretch*=1.01;
                for (int i=0; i<n; ++i) { times[i]=Math.Ceiling(times[i]*stretch*1000)/1000; if (times[i]>120) throw new ArgumentException("路径速度过低，单段超过120秒"); }
                foreach (var v in velocity.Distinct()) foreach (int j in axes) v[j]/=stretch;
            }
            throw new ArgumentException("连续路径时间规划未收敛");
        }
        public static double[][] Controls(double[] p0,double[] p1,double[] v0,double[] v1,double t) =>
            Enumerable.Range(0,6).Select(k => Enumerable.Range(0,6).Select(j => k switch {
                0=>p0[j], 1=>p0[j]+v0[j]*t/5, 2=>p0[j]+2*v0[j]*t/5,
                3=>p1[j]-2*v1[j]*t/5, 4=>p1[j]-v1[j]*t/5, _=>p1[j] }).ToArray()).ToArray();

        public static double[] Evaluate(double[][] controls,double s)
        {
            var row=controls.Select(v => v.ToArray()).ToArray();
            for (int k=5;k>0;--k) for (int i=0;i<k;++i) for (int j=0;j<6;++j) row[i][j]=row[i][j]*(1-s)+row[i+1][j]*s;
            return row[0];
        }
        private static double Deviation(double[][] controls,double[] a,double[] b,double[] w)
        {
            var d=b.Zip(a,(x,y)=>x-y).ToArray(); double norm=d.Sum(x=>x*x);
            return controls.Max(c => {
                double s=norm<1e-18 ? 0 : Math.Clamp(c.Zip(a,(x,y)=>x-y).Zip(d,(x,y)=>x*y).Sum()/norm,0,1);
                return Enumerable.Range(0,6).Sum(j=>w[j]*Math.Abs(c[j]-a[j]-s*d[j]));
            });
        }
        private static double Bound(double[] controls,int depth)
        {
            if (depth==0) return controls.Max(Math.Abs);
            var row=controls.ToArray(); int degree=row.Length-1;
            var left=new double[row.Length]; var right=new double[row.Length]; left[0]=row[0]; right[degree]=row[degree];
            for (int k=1;k<=degree;++k) { for(int i=0;i<=degree-k;++i) row[i]=(row[i]+row[i+1])*.5; left[k]=row[0]; right[degree-k]=row[degree-k]; }
            return Math.Max(Bound(left,depth-1),Bound(right,depth-1));
        }
        private static void AddSamples(List<double[]> samples,double[][] controls)
        {
            // Derivative control polygon bounds joint travel for any interval.
            double rate=Enumerable.Range(0,5).Max(k=>Enumerable.Range(0,6).Max(j=>5*Math.Abs(controls[k+1][j]-controls[k][j])));
            int count=Math.Max(20,(int)Math.Ceiling(rate/.25));
            if (count>20000) throw new ArgumentException("路径采样数量过大");
            for(int i=0;i<=count;++i) samples.Add(Evaluate(controls,(double)i/count));
        }
    }
}
