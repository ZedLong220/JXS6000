using System;
using System.Collections.Generic;
using System.Linq;
using SmallProject.Configs;

namespace SmallProject.Devices.Arm.Kinematic
{
    public static class MotionTiming
    {
        public static double MinimumSeconds(IReadOnlyList<double> from,IReadOnlyList<double> to,
            IReadOnlyList<double> reductions,int[] axes,JConfiguration settings)
        {
            if(!settings.TryValidateMotion(out string error)) throw new ArgumentException(error);
            if(from.Count!=6 || to.Count!=6 || reductions.Count!=6 || axes.Length==0 ||
                axes.Distinct().Count()!=axes.Length || axes.Any(i=>i<0 || i>=6) ||
                from.Any(v=>!double.IsFinite(v)) || to.Any(v=>!double.IsFinite(v)) ||
                reductions.Any(v=>!double.IsFinite(v) || v<=0)) throw new ArgumentException("运动时间参数无效。");
            double time=axes.Max(i=>Math.Abs(to[i]-from[i]))/settings.Speed;
            double acceleration=settings.MotorMaxAcceleration,velocity=settings.MotorMaxVelocity;
            double accelerationDistance=velocity*velocity/acceleration;
            foreach(int i in axes)
            {
                double distance=Math.Abs(to[i]-from[i])*reductions[i]/360.0;
                double driverTime=distance<=accelerationDistance ? 2*Math.Sqrt(distance/acceleration)
                    : 2*velocity/acceleration+(distance-accelerationDistance)/velocity;
                time=Math.Max(time,driverTime);
            }
            return Math.Max(time,.05);
        }
    }
}
