using System;
using System.Linq;
using Newtonsoft.Json;

namespace SmallProject.JointMoveRecord
{
    public sealed class JointRecordModel
    {
        // Older records have no mask and contain measurements for all six axes.
        public int AxisMask { get; set; } = 0x3F;

        [JsonIgnore] public string RecordedAxes => string.Join("、",Enumerable.Range(0,6)
            .Where(i=>(AxisMask&(1<<i))!=0).Select(i=>$"J{i+1}"));
        [JsonIgnore] public double? RecordedJ1 => RecordedJoint(0,J1);
        [JsonIgnore] public double? RecordedJ2 => RecordedJoint(1,J2);
        [JsonIgnore] public double? RecordedJ3 => RecordedJoint(2,J3);
        [JsonIgnore] public double? RecordedJ4 => RecordedJoint(3,J4);
        [JsonIgnore] public double? RecordedJ5 => RecordedJoint(4,J5);
        [JsonIgnore] public double? RecordedJ6 => RecordedJoint(5,J6);
        [JsonIgnore] public double? RecordedX => AxisMask==0x3F ? X : null;
        [JsonIgnore] public double? RecordedY => AxisMask==0x3F ? Y : null;
        [JsonIgnore] public double? RecordedZ => AxisMask==0x3F ? Z : null;
        [JsonIgnore] public double? RecordedA => AxisMask==0x3F ? A : null;
        [JsonIgnore] public double? RecordedB => AxisMask==0x3F ? B : null;
        [JsonIgnore] public double? RecordedC => AxisMask==0x3F ? C : null;

        private double? RecordedJoint(int axis,double value) => (AxisMask&(1<<axis))!=0 ? value : null;

        public bool ContainsAxes(int[] axes) => AxisMask>0 && AxisMask<=0x3F && axes.Length>0 &&
            axes.Distinct().Count()==axes.Length && axes.All(i=>i>=0 && i<6 && (AxisMask&(1<<i))!=0);

        public double[] MotionTarget(double[] current,int[] axes)
        {
            if(current.Length!=6 || current.Any(v=>!double.IsFinite(v)) || !ContainsAxes(axes))
                throw new ArgumentException("点位缺少所选关节的实测位置，或当前位置无效。");
            var measured=new[]{J1,J2,J3,J4,J5,J6};
            var target=current.ToArray();
            foreach(int axis in axes)
            {
                if(!double.IsFinite(measured[axis])) throw new ArgumentException("点位关节角度无效。");
                target[axis]=measured[axis];
            }
            return target;
        }

        public double J1 { get; set; }
        public double J2 { get; set; }
        public double J3 { get; set; }
        public double J4 { get; set; }
        public double J5 { get; set; }
        public double J6 { get; set; }

        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double A { get; set; }
        public double B { get; set; }
        public double C { get; set; }

        public DateTime AddTime { get; set; }
    }
}
