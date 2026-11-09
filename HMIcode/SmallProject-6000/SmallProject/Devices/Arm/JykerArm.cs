using SmallProject.Configs;
using SmallProject.Devices.Arm.CtrlStep;
using SmallProject.Devices.Arm.Kinematic.Models;
using SmallProject.Devices.Arm.Kinematic;
using SmallProject.Logger;
using SmallProject.Serials.Slcan;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Processing.Processors.Transforms;
using Microsoft.VisualBasic.Logging;

namespace SmallProject.Devices.Arm
{
    public class JykerArm
    {
        //角度转换成脉冲 3200 为 360°
        public const double DEG_TO_PULSE = 8.888888889;
        private const double ClosedLoopDriverMaxRevolutionsPerSecond = 4.2;
        private const double ClosedLoopDriverAccelerationRevolutionsPerSecondSquared = 6.25;
        private const double MinimumMoveTimeSeconds = 0.05;
        public const int MaximumContinuousPathPoints = 32;
        public double ContinuousPathDurationSeconds { get; private set; }

        private readonly SemaphoreSlim pathUpload = new(1,1);
        private long lastCurrentQuery;
        public Dof6kinematic dof6Solver;
        public CtrlStepMotor[] motorJ;
        public Joint6D_t currentJoints;
        public Joint6D_t prepareJoints;
        public Pose6D_t preparePose6D;
        private readonly object positionLock = new object();
        private readonly long[] positionVersions = new long[6];

        public JykerArm() {
            dof6Solver = new Dof6kinematic(App.JConfig);
            motorJ = new CtrlStepMotor[6] {
                new CtrlStepMotor(){ AngleLimitMin =-90.1,AngleLimitMax = 90.1,Reduction = App.JConfig.ReductionJ1}
                ,new CtrlStepMotor(){ AngleLimitMin = -250.1,AngleLimitMax = 180.1 , Direction =-1, OffsetAngle = -90,Reduction = App.JConfig.ReductionJ2 }
                ,new CtrlStepMotor(){ AngleLimitMin = -0.1,AngleLimitMax= 510.1,OffsetAngle = 180 , Direction = 1,Reduction = App.JConfig.ReductionJ3}
                ,new CtrlStepMotor(){ AngleLimitMin = -0.1,AngleLimitMax = 180.1 , Direction =-1,Reduction = App.JConfig.ReductionJ4}
                ,new CtrlStepMotor(){ AngleLimitMin = -180.1 , AngleLimitMax = 270.1,Reduction = App.JConfig.ReductionJ5}
                ,new CtrlStepMotor() { AngleLimitMin = -180.1,AngleLimitMax = 180.1,Reduction = App.JConfig.ReductionJ6}
            };
            preparePose6D = new Pose6D_t();
            prepareJoints = Joint6D_t.defult;
            currentJoints = Joint6D_t.defult;
            for(int i=0;i<6;++i) {
                motorJ[i].AngleLimitMin=dof6Solver.Model.LowerDegrees[i]+Joint6D_t.defult.a[i];
                motorJ[i].AngleLimitMax=dof6Solver.Model.UpperDegrees[i]+Joint6D_t.defult.a[i];
            }
        }

        public void LoopStatus()
        {
            if (App.Core?.Serial?.IsOpen != true || pathUpload.CurrentCount == 0) return;
            long now = Environment.TickCount64;
            long previous = Interlocked.Read(ref lastCurrentQuery);
            if (now - previous < 500 ||
                Interlocked.CompareExchange(ref lastCurrentQuery, now, previous) != previous) return;
            for (int i = 0; i < 6; i++) GetStatus(i + 1);
        }

        public void GetStatus(int Id)
        {
            var motor = motorJ[Id - 1];
            var frame = SlcanParser.ParseSlcanQueryFrameStr(Id, 0x21);
            App.Core?.Serial?.PushDataToQueue(frame);
        }

        //更新电流
        public void RecieveCurrent(int Id,float value)
        {
            var motor = motorJ[Id - 1];
            motor.Current = Math.Abs(value);
        }
        //更新速度
        public void RecieveVelocity(int Id, float velocity)
        {
            var motor = motorJ[Id - 1];
            motor.Velocity = Math.Abs(velocity);
        }
        //更新位置
        public void RecievePos(int Id, float value,bool isFinish)
        {
            if (Id < 1 || Id > motorJ.Length || !float.IsFinite(value)) return;
            var motor = motorJ[Id - 1];
            double directionAndReduction = motor.Direction * motor.Reduction;
            if (directionAndReduction == 0) return;
            lock (positionLock)
            {
                // CAN 0x23 reports motor revolutions. Convert it to the model
                // joint angle used by the UI and point records.
                motor.Angle = value * 360.0 / directionAndReduction;
                motor.IsFinish = isFinish;
                positionVersions[Id - 1]++;
            }
        }

        public async Task<double[]?> ReadCurrentModelJointsAsync(int timeoutMilliseconds = 1500)
        {
            if (!App.Core.Serial.IsOpen) return null;

            long[] versions;
            lock (positionLock) versions=positionVersions.ToArray();
            for (int i=0;i<6;++i)
                App.Core.Serial.PushDataToQueue(SlcanParser.ParseSlcanQueryFrameStr(i+1,0x23));

            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < timeoutMilliseconds)
            {
                lock (positionLock)
                {
                    if (Enumerable.Range(0,6).All(i=>positionVersions[i]>versions[i]))
                    {
                        double[] joints = JointCoordinateMap.ToModel(currentJoints.a);
                        for(int i=0;i<6;++i) joints[i]=motorJ[i].Angle;
                        currentJoints=new Joint6D_t(JointCoordinateMap.ToLegacy(joints));
                        return joints;
                    }
                }
                await Task.Delay(20);
            }
            return null;
        }

        // Older UI/voice entry points use the same checked six-axis path.
        public Func<IReadOnlyList<double[]>, Task<(bool ok, string reason)>>? PathValidator { get; set; }
        public async void Move(double[] angles)
        {
            if (!System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(()=>Move(angles));
                return;
            }
            if (PathValidator==null) { JLog.Info("运动路径检查尚未就绪"); return; }
            try { await QueueContinuousPathAsync(new[]{angles},PathValidator,repeat:false); }
            catch(Exception ex) { StopNow(); JLog.Error(ex); }
        }

        private double CalculateMoveTime(IReadOnlyList<double> from, IReadOnlyList<double> to)
        {
            var speed = App.JConfig.Speed;
            if(!float.IsFinite(speed) || speed<=0) throw new ArgumentException("速度必须大于0");
            double time = from.Zip(to, (a, b) => Math.Abs(b - a)).Max() / speed;
            for (int i = 0; i < 6; i++)
            {
                double distanceRev = Math.Abs(to[i] - from[i]) * motorJ[i].Reduction / 360.0;
                double acceleration = ClosedLoopDriverAccelerationRevolutionsPerSecondSquared;
                double velocity = ClosedLoopDriverMaxRevolutionsPerSecond;
                double accelerationDistance = velocity * velocity / acceleration;
                double driverTime = distanceRev <= accelerationDistance
                    ? 2.0 * Math.Sqrt(distanceRev / acceleration)
                    : 2.0 * velocity / acceleration +
                      (distanceRev - accelerationDistance) / velocity;
                time = Math.Max(time, driverTime);
            }
            return Math.Max(time, MinimumMoveTimeSeconds);
        }

        private float ToMotorRevolutions(int index, double legacyAngle)
        {
            double relativeAngle = legacyAngle - Joint6D_t.defult.a[index];
            return Convert.ToSingle(relativeAngle * motorJ[index].Direction *
                                    motorJ[index].Reduction / 360.0);
        }

        public async Task<bool> QueueContinuousPathAsync(
            IReadOnlyList<double[]> legacyTargets,
            Func<IReadOnlyList<double[]>, Task<(bool ok, string reason)>> validatePath,
            bool repeat = true, CancellationToken cancellationToken = default, int[]? jointIndices = null)
        {
            if (!await pathUpload.WaitAsync(0,cancellationToken)) return false;
            try
            {
            int[] axes=jointIndices ?? new[]{0,1,2,3,4,5};
            if(axes.Length==0 || axes.Length>6 || axes.Distinct().Count()!=axes.Length || axes.Any(i=>i<0 || i>=6)) return false;
            if (!App.Core.Serial.IsOpen || legacyTargets.Count == 0 ||
                legacyTargets.Count > MaximumContinuousPathPoints)
                return false;
            if (legacyTargets.Any(target => target.Length != 6 ||
                                            target.Any(value => !double.IsFinite(value))))
                return false;

            double[]? start = await ReadCurrentModelJointsAsync();
            if (start == null) { JLog.Info("路径启动失败：J1～J6编码器新反馈不完整，请检查节点1～6。"); return false; }
            var times = new List<double>();
            double[] previous = JointCoordinateMap.ToLegacy(start);
            foreach (var target in legacyTargets) { times.Add(CalculateMoveTime(previous,target)); previous=target; }
            ContinuousPathPlanner.Plan plan;
            try
            {
                plan = ContinuousPathPlanner.Build(dof6Solver.Model, start,
                    legacyTargets.Select(JointCoordinateMap.ToModel).ToArray(), times,
                    axes.Select(i=>motorJ[i].Direction*motorJ[i].Reduction/360.0).ToArray(), repeat,
                    jointIndices:axes);
            }
            catch (ArgumentException ex) { JLog.Info("路径规划失败："+ex.Message); return false; }
            foreach (var samples in new[] { plan.FirstSamples, plan.Samples })
            {
                cancellationToken.ThrowIfCancellationRequested();
                var validation = await validatePath(samples);
                if (!validation.ok) { JLog.Info("实际连续曲线检查失败："+validation.reason); return false; }
            }
            cancellationToken.ThrowIfCancellationRequested();
            var commands = MultiAxisProtocol.Encode(plan,axes,repeat)
                .Select(command=>SlcanParser.ParseSlcanFrameStr(0,command.Code,command.Data)).ToList();
            bool accepted = await App.Core.Serial.SendBatchAndWaitForAckAsync(commands,cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!accepted)
            {
                JLog.Info($"连续路径逐帧确认失败，共 {commands.Count} 帧");
            }
            else
            {
                ContinuousPathDurationSeconds=plan.Segments.Sum(s=>s.Duration);
                JLog.Info($"连续曲线已确认：{plan.Segments.Count}段，规划偏差界 {plan.MaximumDeviationMm:F3}mm（容差1mm），总时间 {ContinuousPathDurationSeconds:F3}s。");
            }
            return accepted;
            }
            finally { pathUpload.Release(); }
        }

        //设置当前位置为零位
        public void ApplyHomePosition()
        {
            var canFrame = SlcanParser.ParseSlcanFrameStr(0, 0x15);
            App.Core.Serial?.PushDataToQueue(canFrame);
        }
        //设置立刻停止
        public void StopNow()
        {
            var canFrame = SlcanParser.ParseSlcanFrameStr(0, 0x04);
            App.Core.Serial?.PushDataToQueue(canFrame);
        }
        //生成逆解解算结果
        public bool SolveIK(double[] vals)
        {
            var preparePose6D = new Pose6D_t(vals);
            var res =dof6Solver.AfterSolveIK(preparePose6D, prepareJoints, currentJoints, motorJ, out Joint6D_t _outputJoints);
            if(res)
            {
                prepareJoints = _outputJoints;
                this.preparePose6D = preparePose6D;
            }
            return res;
        }
        //设置轴的最大限制电流
        public void SetLimitCurrent(int Id,float current)
        {
            if(current>3000)
            {
                JLog.Info("电流最大设置为3000mA.");
                return;
            }
            if(current<=0)
            {
                JLog.Info("电流设置值有误.");
                return;
            }
            var canFrame = SlcanParser.ParseSlcanFrameStr(Id, 0x12, current / 1000);
            App.Core.Serial?.PushDataToQueue(canFrame);
        }
    }
}
