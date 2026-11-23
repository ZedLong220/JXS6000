using SmallProject.Aliyun;
using SmallProject.MCP;
using SmallProject.Serials.Slcan;
using SmallProject.Devices.Arm.Kinematic;
using SmallProject.Devices.Arm.Kinematic.Models;
using SmallProject.Devices.Arm;
using SmallProject.JointMoveRecord;
using SmallProject.Logger;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Text;
using System.Threading.Tasks;

namespace SmallProject.WindowController
{
    internal class JykerMoveContrl
    {
        private MainWindow M;
        private JykerControlMCP jykerControlMCP;
        private CancellationTokenSource? loopCancellation;
        private bool loopRunning;
        private bool recording;
        internal bool IsBusy => loopRunning || recording;

        public JykerMoveContrl(MainWindow m)
        {
            M = m;
            m.bt_ApplyHomePosition.Click += Bt_ApplyHomePosition_Click; ;
            m.bt_MoveJoint.Click += Bt_MoveJoint_Click;
            m.bt_MoveSingleAxis.Click += Bt_MoveSingleAxis_Click;
            m.bt_StopNow.Click += Bt_StopNow_Click;
            m.bt_EmergencyDisable.Click += Bt_EmergencyDisable_Click;
            m.bt_ConnectAi.Click += Bt_ConnectAi_Click;
            m.bt_AddRecord.Click += Bt_AddRecord_Click;
            m.bt_DeleteRecord.Click += Bt_DeleteRecord_Click;
            m.bt_MoveLoop.Click += Bt_MoveLoop_Click;
            m.bt_MoveLoopStop.Click += Bt_MoveLoopStop_Click;
            m.bt_MoveFivePath.Click += Bt_MoveFivePath_Click;
            m.bt_ClawApply.Click += async (_,_) => await SendGripperAsync((float)m.pg_ClawAngle.Value);
            m.bt_ClawHome.Click += async (_,_) => await SendGripperAsync(0);
            m.bt_ClawFull.Click += async (_,_) => await SendGripperAsync(180);
            m.bt_ClawStop.Click += async (_,_) => await SendGripperCommandAsync(0x56,Array.Empty<byte>());
            m.bt_ClawLoopStart.Click += async (_,_) => await SendGripperCommandAsync(0x57,Array.Empty<byte>());
            m.bt_FixedPickSequence.Click += async (_,_) => await StartFixedPickSequenceAsync();
            m.pg_ClawAngle.ValueChanged += (_,_) => m.tb_ClawAngle.Text=$"{m.pg_ClawAngle.Value:F0}°";
            App.Core.Serial.GripperStatusReceived += (angle,pulse,enabled) => m.Dispatcher.BeginInvoke(new Action(() =>
                m.tb_GripperStatus.Text=$"PWM：{(enabled?"已启用":"已关闭")}；指令角度 {angle:F1}°，脉宽 {pulse}µs（无位置反馈）"));
            App.Core.Jyker.PathValidator = samples => M.JykerPreviewContrl.ValidatePathAsync(samples);
            m.dg_JointRecord.ItemsSource = App.Core.JointRecords;
        }

        //连接语音助手
        private Task SendGripperAsync(float angle) => SendGripperCommandAsync(0x55,BitConverter.GetBytes(angle));

        private async Task StartFixedPickSequenceAsync()
        {
            if(!App.Core.Serial.IsOpen) { JLog.Info("请先打开连接。"); return; }
            if(loopRunning || recording || App.Core.Serial.ControllerState!=0) {
                JLog.Info("主控当前不在空闲状态，请先停止现有运动。");
                return;
            }
            loopRunning=true;
            loopCancellation=new CancellationTokenSource();
            CancellationToken token=loopCancellation.Token;
            long statusVersion=App.Core.Serial.ControllerStatusVersion;
            long completionCount=App.Core.Serial.ControllerCompletionCount;
            SetFixedSequenceControls(false);

            M.bt_MoveLoopStop.IsEnabled=true;
            try {
                var frame=SmallProject.Serials.Slcan.SlcanParser.ParseSlcanFrameStr(0,0x58,Array.Empty<byte>());
                if(!await App.Core.Serial.SendBatchAndWaitForAckAsync(new[]{frame},token))
                {
                    JLog.Info("固定取放路径被主控拒绝：请确认主控空闲并已更新固件。");
                    return;
                }
                JLog.Info($"固定取放路径已启动：主控确认J1～J5在线且位于零点（J6不参与），按6.56秒设定运动到 (-15,75,-300,65,-90,0)，J3到-240°左右时舵机到90° → 到位后舵机回0°，等待1秒 → {FixedPickArcInfo.Description} → 终点J1=-85.573°时舵机90°，等待1秒 → J1～J5按6.56秒设定同步回零，从回零同步启动计时，第3.28秒令舵机回0°。圆弧已按当前URDF及电机限速15圈/秒、加速度15圈/秒²校核，实际以编码器到位为准。J6保持原位。");
                if(!await WaitForMovementCompletionAsync(statusVersion,completionCount,token)) return;

                int[] fixedAxes={0,1,2,3,4};
                double[]? actual=await App.Core.Jyker.ReadCurrentModelJointsAsync(
                    jointIndices:fixedAxes,cancellationToken:token);
                if(actual==null)
                    JLog.Info("固定取放路径已完成，但最终编码器位置刷新超时。");
                else
                    JLog.Info($"固定取放路径已完成：{string.Join("，",fixedAxes.Select(i=>$"J{i+1}={actual[i]:F3}°"))}。");
            }
            catch(OperationCanceledException) { JLog.Info("固定取放路径已停止。"); }
            catch(Exception ex) {
                App.Core.Jyker.StopNow();
                JLog.Error(ex);
                JLog.Info("固定取放路径异常，已请求停止并保持当前位置。");
            }
            finally {
                loopRunning=false;
                loopCancellation?.Dispose();
                loopCancellation=null;
                SetFixedSequenceControls(App.Core.Serial.IsOpen);
                M.bt_MoveLoopStop.IsEnabled=false;
            }
        }

        private void SetFixedSequenceControls(bool enabled)
        {
            M.bt_MoveJoint.IsEnabled=enabled;
            M.bt_MoveSingleAxis.IsEnabled=enabled;
            M.bt_MoveFivePath.IsEnabled=enabled;
            M.bt_MoveLoop.IsEnabled=enabled;
            M.bt_FixedPickSequence.IsEnabled=enabled;
            M.bt_ApplyHomePosition.IsEnabled=enabled;
            M.bt_AddRecord.IsEnabled=enabled;
            M.bt_DeleteRecord.IsEnabled=enabled;
            M.bt_MoveArmHand.IsEnabled=enabled;
            M.bt_ConnectAi.IsEnabled=enabled;
            M.bt_ClawApply.IsEnabled=enabled;
            M.bt_ClawHome.IsEnabled=enabled;
            M.bt_ClawFull.IsEnabled=enabled;
            M.bt_ClawStop.IsEnabled=enabled;
            M.bt_ClawLoopStart.IsEnabled=enabled;
            M.pg_ClawAngle.IsEnabled=enabled;
        }

        private async Task SendGripperCommandAsync(int command,byte[] data)
        {
            if(!App.Core.Serial.IsOpen) { JLog.Info("请先打开连接。"); return; }
            if(loopRunning || App.Core.Serial.ControllerState!=0)
            {
                JLog.Info("主控正在执行路径，请先停止或等待完成后再操作夹爪。");
                return;
            }
            try {
                var frame=SmallProject.Serials.Slcan.SlcanParser.ParseSlcanFrameStr(0,command,data);
                if(!await App.Core.Serial.SendBatchAndWaitForAckAsync(new[]{frame}))
                    JLog.Info("夹爪指令未获主控确认，请检查连接与主控固件。");
            }
            catch(Exception ex) { JLog.Error(ex); }
        }

        private void Bt_ConnectAi_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            //AiAssistant aiAssistant = new AiAssistant();
            //aiAssistant.Init();
            Task.Run(async () =>
            {
                jykerControlMCP = new JykerControlMCP();
                await jykerControlMCP.Init();
            });
            
        }

        // Stop motion while retaining closed-loop holding torque.
        private void Bt_StopNow_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            loopCancellation?.Cancel();
            App.Core.Jyker.StopNow();
            JLog.Info("已请求停止运动，驱动板继续抱住当前位置。");
        }

        private void Bt_EmergencyDisable_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            loopCancellation?.Cancel();
            App.Core.Jyker.EmergencyDisable();
            JLog.Info("已请求紧急失能：六轴驱动输出将关闭。");
        }

        //设置当前位置为0位
        private void Bt_ApplyHomePosition_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (loopRunning || recording || App.Core.Serial.ControllerState != 0)
            {
                JLog.Info("请先停止路径并确认电机静止，再设置当前位置为零。");
                return;
            }
            App.Core.Jyker.ApplyHomePosition();
            JLog.Info("已请求将在线关节当前位置设为零；驱动板将持续保持位置力矩，不会主动失能。");
        }

        private async void Bt_AddRecord_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (recording) return;
            if (loopRunning || App.Core.Serial.ControllerState!=0)
            {
                JLog.Info("请先停止运动并确认电机静止，再记录点位。");
                return;
            }
            if (!App.Core.Serial.IsOpen)
            {
                JLog.Info("请先连接机械臂，再记录点位。");
                return;
            }

            int[] axes=M.JykerKinematicContrl.SelectedMotionAxes;
            if(axes.Length==0)
            {
                JLog.Info("请先勾选要记录的J轴；记录读取实际编码器，无需填写目标角度。");
                return;
            }
            string axisNames=string.Join("、",axes.Select(i=>$"J{i+1}"));
            recording=true;
            M.bt_AddRecord.IsEnabled = false;
            try
            {
                double[]? joints = await App.Core.Jyker.ReadCurrentModelJointsAsync(jointIndices:axes);
                if (joints == null)
                {
                    JLog.Info($"记录点位失败：{axisNames}编码器新反馈不完整，请检查勾选轴对应的驱动板。");
                    return;
                }

                var pose = App.Core.Jyker.dof6Solver.Model.Forward(joints);
                var record = new JointRecordModel
                {
                    AddTime = DateTime.Now,
                    AxisMask = axes.Aggregate(0,(mask,i)=>mask|(1<<i)),
                    J1 = Math.Round(joints[0], 3),
                    J2 = Math.Round(joints[1], 3),
                    J3 = Math.Round(joints[2], 3),
                    J4 = Math.Round(joints[3], 3),
                    J5 = Math.Round(joints[4], 3),
                    J6 = Math.Round(joints[5], 3),
                    X = Math.Round(pose.X, 3),
                    Y = Math.Round(pose.Y, 3),
                    Z = Math.Round(pose.Z, 3),
                    A = Math.Round(pose.A, 3),
                    B = Math.Round(pose.B, 3),
                    C = Math.Round(pose.C, 3)
                };

                App.Core.JointRecords.Add(record);
                if (!JointRecordRes.Write(App.Core.JointRecords))
                {
                    App.Core.JointRecords.Remove(record);
                    JLog.Info("记录点位失败：无法写入点位文件。");
                    return;
                }

                M.dg_JointRecord.SelectedItem = record;
                M.dg_JointRecord.ScrollIntoView(record);
                M.JykerKinematicContrl.ApplyModelJoints(joints);
                JLog.Info($"已记录点位：{string.Join(", ",axes.Select(i=>$"J{i+1}={joints[i]:F3}°"))}；未勾选轴未记录。");
            }
            catch (Exception ex)
            {
                JLog.Error(ex);
                JLog.Info("记录点位失败，请检查编码器反馈。");
            }
            finally
            {
                recording=false;
                M.bt_AddRecord.IsEnabled = App.Core.Serial.IsOpen && !loopRunning;
            }
        }

        private void Bt_DeleteRecord_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (M.dg_JointRecord.SelectedItem is not JointRecordModel record)
            {
                JLog.Info("请先在点位表格中选择一条记录。");
                return;
            }

            int index = App.Core.JointRecords.IndexOf(record);
            App.Core.JointRecords.Remove(record);
            if (!JointRecordRes.Write(App.Core.JointRecords))
            {
                App.Core.JointRecords.Insert(index < 0 ? 0 : index, record);
                JLog.Info("删除点位失败：无法写入点位文件。");
                return;
            }
            JLog.Info("已删除所选点位。");
        }

        private async void Bt_MoveLoop_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (loopRunning || recording || App.Core.Serial.ControllerState!=0)
            {
                JLog.Info("主控正在执行路径，请先停止或等待完成。");
                return;
            }
            if (!App.Core.Serial.IsOpen)
            {
                JLog.Info("请先连接机械臂，再启动固定往返运动。");
                return;
            }

            loopRunning = true;
            loopCancellation = new CancellationTokenSource();
            CancellationToken token = loopCancellation.Token;
            M.bt_MoveLoop.IsEnabled = false;
            M.bt_FixedPickSequence.IsEnabled = false;
            M.bt_MoveSingleAxis.IsEnabled = M.bt_MoveFivePath.IsEnabled = M.bt_MoveJoint.IsEnabled = false;
            M.bt_MoveLoopStop.IsEnabled = true;
            M.bt_AddRecord.IsEnabled = false;
            M.bt_DeleteRecord.IsEnabled = false;
            JLog.Info("正在读取编码器并检查固定往返路径。");

            try
            {
                double[]? current = await App.Core.Jyker.ReadCurrentModelJointsAsync();
                if (current == null)
                {
                    JLog.Info("固定往返停止：未收到关节2和关节3的编码器位置。");
                    return;
                }
                const double zeroToleranceDegrees = 0.5;
                if (Math.Abs(current[1]) > zeroToleranceDegrees ||
                    Math.Abs(current[2]) > zeroToleranceDegrees)
                {
                    JLog.Info($"固定往返停止：请先回零，当前 J2={current[1]:F3}°，J3={current[2]:F3}°。");
                    return;
                }
                App.Core.Jyker.currentJoints = new Joint6D_t(
                    JointCoordinateMap.ToLegacy(current));

                double[] zero = current.ToArray();
                zero[1] = 0.0;
                zero[2] = 0.0;
                double[] point1 = zero.ToArray();
                point1[1] = 40.0;
                point1[2] = -40.0;
                double[] point2 = zero.ToArray();
                point2[1] = 10.0;
                point2[2] = -50.0;
                double[] point3 = zero.ToArray();
                point3[1] = 60.0;
                point3[2] = -10.0;
                double[] point4 = zero.ToArray();
                point4[1] = 50.0;
                point4[2] = -100.0;
                double[][] modelPath = { zero, point1, point2, point3, point4, zero };

                double[][] path = modelPath.Skip(1)
                    .Select(JointCoordinateMap.ToLegacy).ToArray();
                long statusVersion = App.Core.Serial.ControllerStatusVersion;
                long completionCount = App.Core.Serial.ControllerCompletionCount;
                if (!await App.Core.Jyker.QueueContinuousPathAsync(path,
                        samples => M.JykerPreviewContrl.ValidatePathAsync(samples,token),
                        repeat: false, cancellationToken: token, jointIndices: new[]{1,2}))
                {
                    JLog.Info("固定往返停止：路径逐帧确认失败。");
                    return;
                }

                M.JykerKinematicContrl.ApplyModelJoints(point1);
                JLog.Info("固定往返已启动：J2/J3 按 0°,0° -> 40°,-40°"
                    + " -> 10°,-50° -> 60°,-10° -> 50°,-100° -> 0°,0° 运动。");
                if (!await WaitForMovementCompletionAsync(
                        statusVersion, completionCount, token))
                    return;
                M.JykerKinematicContrl.ApplyModelJoints(zero);
                App.Core.Jyker.currentJoints = new Joint6D_t(JointCoordinateMap.ToLegacy(zero));
                JLog.Info("固定往返完成：编码器已确认 J2/J3 回到 0°,0°。");
            }
            catch (OperationCanceledException)
            {
                JLog.Info("固定往返已停止。");
            }
            catch (Exception ex)
            {
                App.Core.Jyker.StopNow();
                JLog.Error(ex);
                JLog.Info("固定往返异常，已发送停止命令。");
            }
            finally
            {
                loopRunning = false;
                loopCancellation?.Dispose();
                loopCancellation = null;
                bool connected = App.Core.Serial.IsOpen;
                M.bt_MoveLoop.IsEnabled = connected;
                M.bt_FixedPickSequence.IsEnabled = connected;
                M.bt_MoveSingleAxis.IsEnabled = M.bt_MoveFivePath.IsEnabled = M.bt_MoveJoint.IsEnabled = connected;
                M.bt_MoveLoopStop.IsEnabled = false;
                M.bt_AddRecord.IsEnabled = connected;
                M.bt_DeleteRecord.IsEnabled = connected;
            }
        }

        private void Bt_MoveLoopStop_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (!loopRunning) return;
            loopCancellation?.Cancel();
            App.Core.Jyker.StopNow();
        }

        private async Task<bool> WaitForMovementCompletionAsync(
            long previousStatusVersion, long previousCompletionCount,
            CancellationToken token, bool repeating = false)
        {
            double timeoutMilliseconds = Math.Max(120000,App.Core.Jyker.ContinuousPathDurationSeconds*1000+20000);
            var startedAt = DateTime.UtcNow;
            while (repeating || (DateTime.UtcNow - startedAt).TotalMilliseconds < timeoutMilliseconds)
            {
                token.ThrowIfCancellationRequested();
                if (!App.Core.Serial.IsOpen)
                {
                    JLog.Info("路径停止：机械臂连接已断开。");
                    return false;
                }
                bool receivedCurrentMoveStatus =
                    App.Core.Serial.ControllerStatusVersion > previousStatusVersion;
                if (receivedCurrentMoveStatus &&
                    (App.Core.Serial.ControllerError != 0 ||
                     App.Core.Serial.ControllerState == 7))
                {
                    JLog.Info("路径停止：主控已进入故障状态。");
                    return false;
                }
                if (App.Core.Serial.ControllerCompletionCount > previousCompletionCount)
                    return true;
                await Task.Delay(25, token);
            }

            App.Core.Jyker.StopNow();
            JLog.Info("路径停止：等待运动完成超时。");
            return false;
        }


        private async void Bt_MoveFivePath_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (loopRunning || recording || !App.Core.Serial.IsOpen) return;
            if(App.Core.Serial.ControllerState!=0)
            {
                JLog.Info("主控正在执行路径，请先停止或等待完成。");
                return;
            }
            var records=App.Core.JointRecords.ToArray();
            if (records.Length<2) { JLog.Info("请先记录至少两个点位。"); return; }
            int[] axes=M.JykerKinematicContrl.SelectedMotionAxes;
            if(axes.Length==0) { JLog.Info("请先勾选参与点位连续运动的J轴。"); return; }
            int missing=Array.FindIndex(records,r=>!r.ContainsAxes(axes));
            if(missing>=0)
            {
                string missingAxes=string.Join("、",axes.Where(i=>!records[missing].ContainsAxes(new[]{i})).Select(i=>$"J{i+1}"));
                JLog.Info($"第{missing+1}条点位未记录{missingAxes}的实测位置，请补录点位或取消勾选这些轴。");
                return;
            }
            string axisNames=string.Join("、",axes.Select(i=>$"J{i+1}"));
            bool repeat=M.cb_RepeatFivePath.IsChecked==true;
            if(!M.JykerKinematicContrl.TryReadPathTolerance(out double toleranceMm)) return;
            loopRunning=true; loopCancellation=new CancellationTokenSource();
            var token=loopCancellation.Token;
            M.bt_MoveSingleAxis.IsEnabled=M.bt_MoveFivePath.IsEnabled=M.bt_MoveLoop.IsEnabled=M.bt_MoveJoint.IsEnabled=false;
            M.bt_FixedPickSequence.IsEnabled=false;
            M.bt_AddRecord.IsEnabled=M.bt_DeleteRecord.IsEnabled=false;
            M.bt_MoveLoopStop.IsEnabled=true;
            try
            {
                var current=await App.Core.Jyker.ReadCurrentModelJointsAsync(jointIndices:axes,cancellationToken:token);
                if(current==null) { JLog.Info($"点位路径停止：{axisNames}编码器反馈不完整。"); return; }
                var targets=records.Select(r=>r.MotionTarget(current,axes)).ToList();
                if(repeat)
                {
                    // A closed loop has no privileged first point. Start from
                    // the recorded point nearest the current encoder position
                    // while preserving the table's circular order. This avoids
                    // a needless long positioning move (and its strict final
                    // arrival check) when the arm is already at record 2 or 3.
                    var loopEntry=ContinuousPathPlanner.RotateLoopToNearest(targets,current,axes);
                    int startIndex=loopEntry.StartIndex;
                    targets=loopEntry.Targets.ToList();
                    bool needsApproach=loopEntry.MaximumErrorDegrees>0.1;
                    if(needsApproach)
                    {
                        long approachVersion=App.Core.Serial.ControllerStatusVersion;
                        long approachCompletion=App.Core.Serial.ControllerCompletionCount;
                        if(!await App.Core.Jyker.QueueContinuousPathAsync(
                            new[]{JointCoordinateMap.ToLegacy(targets[0])},
                            samples=>M.JykerPreviewContrl.ValidatePathAsync(samples,token),false,token,
                            jointIndices:axes,toleranceMm:1.0))
                        {
                            JLog.Info("点位循环未启动：无法到达第1个记录点。"); return;
                        }
                        JLog.Info($"{axisNames}正在进入循环：先到达最近的第{startIndex+1}个记录点，再连续循环。");
                        if(!await WaitForMovementCompletionAsync(approachVersion,approachCompletion,token)) return;
                    }
                    else JLog.Info($"{axisNames}已在第{startIndex+1}个记录点附近，直接开始连续循环。");
                    // Put several visual laps in one 32-block drive cycle. The
                    // crossings between those laps are ordinary one-piece
                    // corner blends; only the much less frequent buffer wrap
                    // has to pass the exact encoder start with a split blend.
                    // This shortens the visible gap without exceeding the
                    // configured velocity, acceleration or jerk limits.
                    int packedLaps=Math.Min(3,Math.Max(1,15/targets.Count));
                    var oneLap=targets.Skip(1).Concat(targets.Take(1)).Select(q=>q.ToArray()).ToArray();
                    targets=Enumerable.Range(0,packedLaps).SelectMany(_=>oneLap.Select(q=>q.ToArray())).ToList();
                    targets.RemoveAt(targets.Count-1); // QueueContinuousPath appends the exact repeat closure.
                    JLog.Info($"循环衔接优化：每{packedLaps}圈才经过一次驱动缓存接缝，其余圈连续圆滑通过。");
                }
                long version=App.Core.Serial.ControllerStatusVersion;
                long completion=App.Core.Serial.ControllerCompletionCount;
                if(!await App.Core.Jyker.QueueContinuousPathAsync(targets.Select(JointCoordinateMap.ToLegacy).ToArray(),
                    samples=>M.JykerPreviewContrl.ValidatePathAsync(samples,token),repeat,token,jointIndices:axes,toleranceMm:toleranceMm)) {
                    JLog.Info("点位路径未启动：请查看规划、碰撞检查或逐帧确认日志。"); return;
                }
                JLog.Info($"{axisNames}点位路径已提交：按表格顺序执行{records.Length}个点位"+
                    (repeat?"，从最近记录点按表格环形顺序闭合循环，点击停止路径结束。":"，最终点停车并等待参与轴编码器确认。"));
                if(!await WaitForMovementCompletionAsync(version,completion,token,repeat)) return;
                var actual=await App.Core.Jyker.ReadCurrentModelJointsAsync(jointIndices:axes,cancellationToken:token);
                if(actual!=null) M.JykerKinematicContrl.ApplyModelJoints(actual);
                JLog.Info("点位路径完成：所有参与轴已通过最终编码器到位检查。");
            }
            catch(OperationCanceledException) { JLog.Info("点位路径已停止。"); }
            catch(Exception ex) { App.Core.Jyker.StopNow(); JLog.Error(ex); }
            finally
            {
                loopRunning=false; loopCancellation?.Dispose(); loopCancellation=null;
                bool connected=App.Core.Serial.IsOpen;
                M.bt_MoveSingleAxis.IsEnabled=M.bt_MoveFivePath.IsEnabled=M.bt_MoveLoop.IsEnabled=M.bt_MoveJoint.IsEnabled=connected;
                M.bt_FixedPickSequence.IsEnabled=connected;
                M.bt_AddRecord.IsEnabled=M.bt_DeleteRecord.IsEnabled=connected;
                M.bt_MoveLoopStop.IsEnabled=false;
            }
        }

        //运动机械臂
        private async void Bt_MoveJoint_Click(object sender, System.Windows.RoutedEventArgs e) => await MoveToTargetAsync(false);

        private async void Bt_MoveSingleAxis_Click(object sender, System.Windows.RoutedEventArgs e) => await MoveToTargetAsync(true);

        private async Task MoveToTargetAsync(bool singleAxis)
        {
            if (loopRunning || recording) return;
            if (!App.Core.Serial.IsOpen) { JLog.Info("请先连接机械臂。"); return; }
            if(App.Core.Serial.ControllerState!=0)
            {
                JLog.Info("主控正在执行路径，请先停止或等待完成。");
                return;
            }
            int[] axes=singleAxis ? new[]{M.cb_SingleAxis.SelectedIndex}
                : M.JykerKinematicContrl.SelectedMotionAxes;
            if(!M.JykerKinematicContrl.TryReadMotionTargets(axes,out var angles)) return;
            string axisNames=string.Join("、",axes.Select(i=>$"J{i+1}"));
            JLog.Info($"运动目标：{string.Join("，",axes.Select(i=>$"J{i+1}={angles[i]:F3}°"))}；只等待所选节点编码器，未选关节按最近位置（首次0°）检查碰撞。");
            loopRunning=true; loopCancellation=new CancellationTokenSource();
            var token=loopCancellation.Token;
            M.bt_MoveSingleAxis.IsEnabled = M.bt_MoveJoint.IsEnabled = false;
            M.bt_MoveFivePath.IsEnabled=M.bt_MoveLoop.IsEnabled=false;
            M.bt_FixedPickSequence.IsEnabled=false;
            M.bt_MoveLoopStop.IsEnabled=true;
            try
            {
                if (!App.Core.Serial.IsOpen) { JLog.Info("请先连接机械臂。"); return; }
                long version=App.Core.Serial.ControllerStatusVersion, completion=App.Core.Serial.ControllerCompletionCount;
                if(!await App.Core.Jyker.QueueContinuousPathAsync(new[]{JointCoordinateMap.ToLegacy(angles)},
                    samples=>M.JykerPreviewContrl.ValidatePathAsync(samples,token),false,token,jointIndices:axes)) return;
                if(!await WaitForMovementCompletionAsync(version,completion,token)) return;
                var actual=await App.Core.Jyker.ReadCurrentModelJointsAsync(jointIndices:axes,cancellationToken:token);
                if(actual!=null) {
                    M.JykerKinematicContrl.ApplyModelJoints(actual);
                    JLog.Info($"{axisNames}运动完成，参与轴编码器已确认到位。");
                }
                else JLog.Info("运动已完成，但刷新编码器位置超时。");
            }
            catch(OperationCanceledException) { }
            catch(Exception ex) { App.Core.Jyker.StopNow(); JLog.Error(ex); }
            finally {
                loopRunning=false; loopCancellation?.Dispose(); loopCancellation=null;
                M.bt_MoveSingleAxis.IsEnabled=M.bt_MoveJoint.IsEnabled=M.bt_MoveFivePath.IsEnabled=M.bt_MoveLoop.IsEnabled=App.Core.Serial.IsOpen;
                M.bt_FixedPickSequence.IsEnabled=App.Core.Serial.IsOpen;
                M.bt_MoveLoopStop.IsEnabled=false;
            }
        }


    }
}
