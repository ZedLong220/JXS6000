using SmallProject.Devices.Arm.Kinematic;
using SmallProject.Devices.Arm.Kinematic.Models;
using SmallProject.Logger;
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace SmallProject.WindowController
{
    internal sealed class JykerKinematicContrl
    {
        private readonly MainWindow window;
        private bool solving;
        private bool updatingJointInputs;
        public JykerKinematicContrl(MainWindow window)
        {
            this.window = window;
            window.bt_FK.Click += (_, _) => CalculateForward();
            window.bt_IK.Click += async (_, _) => await CalculateInverseAsync();
            window.bt_GetCurrentAngle.Click += (_, _) => ShowCurrentPosition();
            ApplyModelJoints(JointCoordinateMap.ToModel(App.Core.Jyker.prepareJoints.a));
            for (int i=0;i<6;i++)
            {
                int axis=i;
                JointInputs[i].TextChanged += (_, _) =>
                {
                    if (!updatingJointInputs) MotionSelections[axis].IsChecked=true;
                };
            }
        }
        public int[] SelectedMotionAxes => Enumerable.Range(0,6)
            .Where(i=>MotionSelections[i].IsChecked==true).ToArray();

        public bool TryReadMotionTargets(int[] axes, out double[] angles)
        {
            angles=JointCoordinateMap.ToModel(App.Core.Jyker.currentJoints.a);
            if (axes.Length==0)
            {
                JLog.Info("请修改目标角度或勾选至少一个参与运动的关节。");
                return false;
            }
            foreach (int axis in axes)
            {
                if (!TryReadModelJoint(axis,out double target)) return false;
                angles[axis]=target;
            }
            return true;
        }
        public bool TryReadModelJoints(out double[] angles)
        {
            if (!TryReadValues(JointInputs, "关节角度", out angles)) return false;
            if (App.Core.Jyker.dof6Solver.Model.WithinLimits(angles)) return true;
            JLog.Info("关节角度超出 URDF 模型限位。");
            return false;
        }
        public bool TryReadPathTolerance(out double toleranceMm)
        {
            toleranceMm=0;
            if(!TryReadValues(new[]{window.tb_PathTolerance},"圆滑范围",out var values)) return false;
            toleranceMm=values[0];
            if(toleranceMm>=1 && toleranceMm<=50) return true;
            JLog.Info("圆滑范围请输入1～50mm；越大越平滑，但会绕过点位，精度优先可设1mm。");
            return false;
        }
        public bool TryReadModelJoint(int index, out double angle)
        {
            angle=0;
            if(index<0 || index>=6 || !TryReadValues(new[]{JointInputs[index]},$"J{index+1}目标角度",out var values)) return false;
            angle=values[0];
            var model=App.Core.Jyker.dof6Solver.Model;
            if(angle>=model.LowerDegrees[index] && angle<=model.UpperDegrees[index]) return true;
            JLog.Info($"J{index+1}目标超出模型限位：{model.LowerDegrees[index]:F1}°～{model.UpperDegrees[index]:F1}°。");
            return false;
        }
        public bool CalculateForward()
        {
            if (!TryReadModelJoints(out var q)) return false;
            return ApplyModelJoints(q);
        }
        public bool ApplyModelJoints(double[] q, bool updatePreview = true)
        {
            var arm = App.Core.Jyker;
            if (q == null || q.Length != 6 ||
                !arm.dof6Solver.Model.WithinLimits(q)) return false;
            arm.prepareJoints = new Joint6D_t(JointCoordinateMap.ToLegacy(q));
            arm.preparePose6D = arm.dof6Solver.Model.Forward(q);
            updatingJointInputs=true;
            try { ShowValues(JointInputs, q); }
            finally { updatingJointInputs=false; }
            var p = arm.preparePose6D;
            ShowValues(PoseInputs, new[] {p.X,p.Y,p.Z,p.A,p.B,p.C});
            if (updatePreview) window.JykerPreviewContrl.SetJoints(q);
            return true;
        }
        public async Task<bool> CalculateInverseAsync()
        {
            if (solving || !TryReadValues(PoseInputs, "末端位姿", out var values)) return false;
            solving = true;
            window.bt_IK.IsEnabled = false;
            try
            {
                var model = App.Core.Jyker.dof6Solver.Model;
                var seed = JointCoordinateMap.ToModel(App.Core.Jyker.prepareJoints.a);
                var pose = new Pose6D_t(values);
                var answer = await Task.Run(() => {
                    var lower=model.LowerDegrees.ToArray(); var upper=model.UpperDegrees.ToArray();
                    bool ok = model.TryInverse(pose, seed, out var q,lower,upper);
                    return (ok, q);
                });
                if (!answer.ok) { JLog.Info("六轴模式未找到满足限位的逆解，请调整目标位置和姿态。"); return false; }
                ApplyModelJoints(answer.q);
                foreach (var selection in MotionSelections) selection.IsChecked=true;
                JLog.Info("URDF 逆解完成；已更新预览并勾选六轴，尚未发送电机指令。可取消不需要运动的轴。");
                return true;
            }
            finally { solving = false; window.bt_IK.IsEnabled = true; }
        }
        public void ShowCurrentPosition() => ApplyModelJoints(JointCoordinateMap.ToModel(App.Core.Jyker.currentJoints.a));
        private TextBox[] JointInputs => new[] { window.tb_Joint1,window.tb_Joint2,window.tb_Joint3,window.tb_Joint4,window.tb_Joint5,window.tb_Joint6 };
        private CheckBox[] MotionSelections => new[] { window.cb_MoveJ1,window.cb_MoveJ2,window.cb_MoveJ3,window.cb_MoveJ4,window.cb_MoveJ5,window.cb_MoveJ6 };
        private TextBox[] PoseInputs => new[] { window.tb_X,window.tb_Y,window.tb_Z,window.tb_A,window.tb_B,window.tb_C };
        private static bool TryReadValues(TextBox[] inputs, string description, out double[] values)
        {
            values = new double[inputs.Length];
            for (int i=0;i<inputs.Length;i++)
            {
                string text=inputs[i].Text.Trim();
                bool parsed=double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out values[i]) ||
                    double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]);
                if (!parsed || !double.IsFinite(values[i]))
                {
                    JLog.Info($"{description}第 {i+1} 项不是有限数字。");
                    inputs[i].Focus(); inputs[i].SelectAll(); return false;
                }
            }
            return true;
        }
        private static void ShowValues(TextBox[] inputs, double[] values)
        {
            for (int i=0;i<inputs.Length;i++) inputs[i].Text=values[i].ToString("0.#####",CultureInfo.InvariantCulture);
        }
    }
}
