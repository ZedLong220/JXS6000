using SmallProject.Configs;
using SmallProject.Devices.Arm.CtrlStep;
using SmallProject.Devices.Arm.Kinematic.Models;
using System;
using System.IO;
using System.Linq;

namespace SmallProject.Devices.Arm.Kinematic
{
    // Compatibility boundary for existing CAN/AI callers using legacy angles.
    public sealed class Dof6kinematic
    {
        public const double RAD_TO_DEG = 180 / Math.PI;
        public UrdfKinematics Model { get; }
        public Dof6kinematic(JConfiguration armConfig)
            : this(Path.Combine(AppContext.BaseDirectory, "Viewer", "URDF", "jxs6000.urdf")) { }
        public Dof6kinematic(string path) => Model = new UrdfKinematics(path);
        public bool SolveFK(Joint6D_t joint, Pose6D_t pose)
        {
            if (joint.a.Length != 6 || joint.a.Any(v => !double.IsFinite(v))) return false;
            var p = Model.Forward(JointCoordinateMap.ToModel(joint.a));
            pose.X=p.X; pose.Y=p.Y; pose.Z=p.Z;
            pose.A=p.A; pose.B=p.B; pose.C=p.C; pose.R=p.R; pose.hasR=true;
            return true;
        }
        public bool SolveIK(Pose6D_t pose, Joint6D_t last, out IKSolves_t solutions)
        {
            solutions = new IKSolves_t();
            var lower=Model.LowerDegrees.ToArray(); var upper=Model.UpperDegrees.ToArray();
            lower[5]=upper[5]=0;
            if (!Model.TryInverse(pose, JointCoordinateMap.ToModel(last.a), out var q,lower,upper)) return false;
            solutions.config[0] = new Joint6D_t(JointCoordinateMap.ToLegacy(q));
            solutions.solFlag[0] = new[] { 1, 1, 1 };
            return true;
        }
        public bool AfterSolveIK(Pose6D_t pose, Joint6D_t current, Joint6D_t last, CtrlStepMotor[] motors, out Joint6D_t result)
        {
            result = new Joint6D_t();
            var lower = JointCoordinateMap.ToModel(motors.Select(m => m.AngleLimitMin).ToArray());
            var upper = JointCoordinateMap.ToModel(motors.Select(m => m.AngleLimitMax).ToArray());
            lower[5]=upper[5]=0;
            if (!Model.TryInverse(pose, JointCoordinateMap.ToModel(current.a), out var q, lower, upper)) return false;
            result = new Joint6D_t(JointCoordinateMap.ToLegacy(q));
            return true;
        }
    }
}
