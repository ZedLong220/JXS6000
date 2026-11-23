using Masuit.Tools;
using Microsoft.Win32;
using MotorControl.Logger;
using MotorControl.Serials;
using MotorControl.Serials.Slcan;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MotorControl
{
    public partial class Form1 : Form
    {
        private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
        private const int DwmwaUseImmersiveDarkMode = 20;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

        Serial Serial;
        //坐标列表
        List<float> positions = new List<float>();
        //记录获取位置信息的时间
        DateTime PosTime = DateTime.Now;
        //记录获取速递信息的时间
        DateTime VelocityTime = DateTime.Now;
        //当前位置
        float PosNow = 0;
        //当前速度
        float VelocityNow = 0;
        //停止运动标志
        bool IsStop = false;
        private readonly List<TeachingSample> teachingSamples = new List<TeachingSample>();
        private System.Windows.Forms.Timer teachingTimer;
        private CancellationTokenSource replayCancellation;
        private bool teachingSession;
        private bool teachingRecording;
        private bool waitingForTeachingBrake;
        private int teachingNodeId;
        private DateTime teachingStartTime;
        private Button bt_TeachEnter;
        private Button bt_TeachExit;
        private Button bt_TeachReplay;
        private Button bt_TeachSave;
        private Button bt_TeachClear;
        private Label lb_TeachStatus;
        private NumericUpDown num_TeachAssist;

        private sealed class TeachingSample
        {
            public float Position;
            public float TimeSeconds;
        }
        public Form1()
        {
            InitializeComponent();
            ApplyModernTheme();
            ApplyDarkTitleBar();
            InitializeTeachingControls();
        }

        private void ApplyDarkTitleBar()
        {
            try
            {
                var enabled = 1;
                var result = DwmSetWindowAttribute(Handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
                if (result != 0)
                {
                    DwmSetWindowAttribute(Handle, DwmwaUseImmersiveDarkModeBefore20H1, ref enabled, sizeof(int));
                }
            }
            catch (DllNotFoundException)
            {
                // Older Windows versions can still use the dark client-area theme.
            }
            catch (EntryPointNotFoundException)
            {
                // Older Windows versions can still use the dark client-area theme.
            }
        }

        private void ApplyModernTheme()
        {
            var windowBackColor = Color.FromArgb(18, 18, 18);
            var controlBackColor = Color.FromArgb(38, 38, 38);
            var primaryTextColor = Color.FromArgb(232, 232, 232);
            var secondaryTextColor = Color.FromArgb(190, 190, 190);

            BackColor = windowBackColor;
            ForeColor = primaryTextColor;
            StartPosition = FormStartPosition.CenterScreen;

            foreach (Control control in Controls)
            {
                if (control is Label label)
                {
                    label.ForeColor = secondaryTextColor;
                }
                else if (control is NumericUpDown numeric)
                {
                    numeric.BackColor = controlBackColor;
                    numeric.ForeColor = primaryTextColor;
                    numeric.BorderStyle = BorderStyle.FixedSingle;
                }
                else if (control is ComboBox combo)
                {
                    combo.BackColor = controlBackColor;
                    combo.ForeColor = primaryTextColor;
                    combo.FlatStyle = FlatStyle.Flat;
                }
                else if (control is ListBox list)
                {
                    list.BackColor = controlBackColor;
                    list.ForeColor = primaryTextColor;
                    list.BorderStyle = BorderStyle.FixedSingle;
                }
            }

            foreach (Button button in Controls.OfType<Button>())
            {
                StyleButton(button, Color.FromArgb(48, 48, 48), primaryTextColor);
            }

            foreach (Label valueLabel in new[] { lb_ID, lb_Pos, lb_Velocity, lb_Current, lb_LimitCurrent, lb_LimitVelocity, lb_LimitStatus })
            {
                valueLabel.ForeColor = Color.FromArgb(102, 204, 255);
            }
        }

        private static void StyleButton(Button button, Color backColor, Color foreColor)
        {
            button.UseVisualStyleBackColor = false;
            button.FlatStyle = FlatStyle.Standard;
            button.BackColor = backColor;
            button.ForeColor = foreColor;
            button.Cursor = Cursors.Hand;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            Serial = new Serial();
            Serial.GetResInfo += Serial_GetResInfo;
        }

        //加载串口
        private void cb_Com_DropDown(object sender, EventArgs e)
        {
            var list = new List<string>();
            const string keyPath = @"HARDWARE\DEVICEMAP\SERIALCOMM";
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(keyPath))
            {
                if (key != null)
                {
                    cb_ComList.Items.Clear();
                    foreach (string valueName in key.GetValueNames())
                    {
                        if (!valueName.Contains("USB")) continue;
                        string portName = key.GetValue(valueName) as string;
                        var caption = $"{portName}_{valueName}";
                        Match match = Regex.Match(caption, @"COM\d+");
                        if (match.Success)
                        {
                            string comPortName = match.Value; // 提取匹配到的 COM 名称
                            cb_ComList.Items.Add(comPortName);
                            list.Add(comPortName);
                        }
                    }
                }
            }
        }

        //打开连接
        private void bt_Link_Click(object sender, EventArgs e)
        {
            if (bt_Link.Text == "断开连接")
            {
                Serial?.SerialDispose();
                cb_ComList.Enabled = true;
                bt_Link.Text = "连接";
                SetButtomState(false);

            }
            else
            {
                var name = cb_ComList.SelectedItem + "";
                var res = Serial.Open(name);
                if (res)
                {
                    cb_ComList.Enabled = false;
                    bt_Link.Text = "断开连接";
                    SetButtomState(true);
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 窗口关闭时停止后台发送线程，避免串口对象和任务残留。
            if (replayCancellation != null)
            {
                replayCancellation.Cancel();
                SendTeachingCommand(0x46);
                replayCancellation = null;
            }
            if (teachingSession)
                SendTeachingCommand(0x41);
            teachingTimer?.Stop();
            Serial?.SerialDispose();
            base.OnFormClosing(e);
        }

        private void InitializeTeachingControls()
        {
            // Designer controls end near y=494; keep the teaching panel in a separate row below them.
            ClientSize = new Size(Math.Max(ClientSize.Width, 820), 635);
            var panel = new Panel {
                Name = "teachingPanel",
                Location = new Point(10, 510),
                Size = new Size(775, 100),
                BorderStyle = BorderStyle.FixedSingle
            };
            var title = new Label {
                Text = "示教与轨迹回放",
                Location = new Point(10, 8),
                AutoSize = true,
                ForeColor = Color.FromArgb(232, 232, 232)
            };
            var assistLabel = new Label {
                Text = "辅助电流(mA)",
                Location = new Point(130, 10),
                AutoSize = true,
                ForeColor = Color.FromArgb(190, 190, 190)
            };
            num_TeachAssist = new NumericUpDown {
                Location = new Point(215, 7),
                Size = new Size(75, 24),
                Minimum = -4500,
                Maximum = 4500,
                Increment = 50,
                Value = 0
            };
            bt_TeachEnter = CreateTeachingButton("进入示教", 10, 42, 105);
            bt_TeachExit = CreateTeachingButton("退出示教", 120, 42, 105);
            bt_TeachReplay = CreateTeachingButton("演示轨迹", 300, 42, 105);
            bt_TeachSave = CreateTeachingButton("保存轨迹", 410, 42, 105);
            bt_TeachClear = CreateTeachingButton("清除轨迹", 520, 42, 105);
            lb_TeachStatus = new Label {
                Text = "未进入示教",
                Location = new Point(640, 47),
                Size = new Size(125, 23),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(102, 204, 255)
            };
            panel.Controls.AddRange(new Control[] {
                title, assistLabel, num_TeachAssist, bt_TeachEnter, bt_TeachExit,
                bt_TeachReplay, bt_TeachSave, bt_TeachClear, lb_TeachStatus
            });
            Controls.Add(panel);

            bt_TeachEnter.Click += bt_TeachEnter_Click;
            bt_TeachExit.Click += bt_TeachExit_Click;
            bt_TeachReplay.Click += bt_TeachReplay_Click;
            bt_TeachSave.Click += bt_TeachSave_Click;
            bt_TeachClear.Click += bt_TeachClear_Click;

            teachingTimer = new System.Windows.Forms.Timer { Interval = 50 };
            teachingTimer.Tick += teachingTimer_Tick;
        }

        private Button CreateTeachingButton(string text, int x, int y, int width)
        {
            var button = new Button {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, 30),
                FlatStyle = FlatStyle.Standard,
                BackColor = Color.FromArgb(48, 48, 48),
                ForeColor = Color.FromArgb(232, 232, 232),
                UseVisualStyleBackColor = false,
                Cursor = Cursors.Hand
            };
            return button;
        }

        private void SetButtomState(bool state)
        {

        }
        //读取信息
        private void bt_ReadInfo_Click(object sender, EventArgs e)
        {
            var Id = (int)numID.Value;
            var frame = SlcanParser.ParseSlcanFrameStr(Id, 0x21);
            Serial?.PushDataToQueue(frame);
            Thread.Sleep(5);
            frame = SlcanParser.ParseSlcanFrameStr(Id, 0x22);
            Serial?.PushDataToQueue(frame);
            Thread.Sleep(5);
            frame = SlcanParser.ParseSlcanFrameStr(Id, 0x23);
            Serial?.PushDataToQueue(frame);
            Thread.Sleep(5);
            frame = SlcanParser.ParseSlcanFrameStr(Id, 0x26);
            Serial?.PushDataToQueue(frame);
            Thread.Sleep(5);
            //限制速度
            frame = SlcanParser.ParseSlcanFrameStr(Id, 0x27);
            Serial?.PushDataToQueue(frame);
            Thread.Sleep(5);
            //Kp
            frame = SlcanParser.ParseSlcanFrameStr(Id, 0x28);
            Serial?.PushDataToQueue(frame);
            Thread.Sleep(5);
            //Kv
            frame = SlcanParser.ParseSlcanFrameStr(Id, 0x29);
            Serial?.PushDataToQueue(frame);
            Thread.Sleep(5);
            //Ki
            frame = SlcanParser.ParseSlcanFrameStr(Id, 0x2A);
            Serial?.PushDataToQueue(frame);
            Thread.Sleep(5);
            //Kd
            frame = SlcanParser.ParseSlcanFrameStr(Id, 0x2B);
            Serial?.PushDataToQueue(frame);
        }

        //读取数据回调
        private void Serial_GetResInfo(int Id, float value, InfoType type)
        {
            try
            {
                this.Invoke(new Action(() => {
                    switch (type)
                    {
                        case InfoType.Current:
                            lb_Current.Text = value + "";
                            break;
                        case InfoType.Velocity:
                            lb_Velocity.Text = value + "";
                            VelocityTime = DateTime.Now;
                            VelocityNow = value;
                            break;
                        case InfoType.Position:
                            lb_Pos.Text = value + "";
                            PosTime = DateTime.Now;
                            PosNow = value;
                            if (teachingRecording && Id == teachingNodeId)
                            {
                                teachingSamples.Add(new TeachingSample {
                                    Position = value,
                                    TimeSeconds = (float)(DateTime.UtcNow - teachingStartTime).TotalSeconds
                                });
                            }
                            break;
                        case InfoType.CurrentLimit:
                            lb_LimitCurrent.Text = value + "";
                            break;
                        case InfoType.VelocityLimit:
                            lb_LimitVelocity.Text = value + "";
                            break;
                        case InfoType.Kp:
                            num_Kp.Value = (decimal)value;
                            break;
                        case InfoType.Kv:
                            num_Kv.Value = (decimal)value;
                            break;
                        case InfoType.Ki:
                            num_Ki.Value = (decimal)value;
                            break;
                        case InfoType.Kd:
                            num_Kd.Value = (decimal)value;
                            break;
                        case InfoType.LimitReached:
                            lb_LimitStatus.Text = GetLimitStatusText((int)value);
                            lb_LimitStatus.ForeColor = Color.Red;
                            break;
                        case InfoType.TeachingStatus:
                            HandleTeachingStatus(Id, (int)value);
                            break;
                    }
                    lb_ID.Text = Id + "";
                }));
            }
            catch
            {
            }
        }

        private static string GetLimitStatusText(int side)
        {
            switch (side)
            {
                case 1:
                    return "左限位触发";
                case 2:
                    return "右限位触发";
                case 3:
                    return "左右限位触发";
                default:
                    return "限位触发";
            }
        }

        private void HandleTeachingStatus(int id, int state)
        {
            if (!teachingSession && !waitingForTeachingBrake && state == 0)
                return;

            teachingNodeId = id;
            lb_TeachStatus.Text = GetTeachingStatusText(state);
            lb_TeachStatus.ForeColor = state == 2 ? Color.LightGreen : Color.FromArgb(102, 204, 255);
            if (state == 2 && teachingSession && !teachingRecording)
            {
                teachingRecording = true;
                teachingStartTime = DateTime.UtcNow;
                teachingSamples.Clear();
                lb_TeachStatus.Text = "示教记录中";
            }
            else if (state == 0 && teachingSession && !teachingRecording)
            {
                // 进入示教被驱动板拒绝时，结束本地会话，避免继续发送心跳。
                teachingSession = false;
                teachingTimer.Stop();
                lb_TeachStatus.Text = "进入示教失败";
                lb_TeachStatus.ForeColor = Color.OrangeRed;
            }
            else if (state == 0 && waitingForTeachingBrake)
            {
                teachingRecording = false;
                waitingForTeachingBrake = false;
                teachingTimer.Stop();
                lb_TeachStatus.Text = "已抱闸";
                lb_TeachStatus.ForeColor = Color.LightGreen;
            }
        }

        private static string GetTeachingStatusText(int state)
        {
            switch (state)
            {
                case 1: return "预励磁/松闸中";
                case 2: return "已松闸";
                case 3: return "正在抱闸";
                default: return "已抱闸";
            }
        }

        private void SendTeachingCommand(int command)
        {
            var id = (int)numID.Value;
            Serial?.PushDataToQueue(SlcanParser.ParseSlcanFrameStr(id, command));
        }

        private void SendTeachingIntCommand(int command, int value)
        {
            var id = (int)numID.Value;
            Serial?.PushDataToQueue(SlcanParser.ParseSlcanFrameInt(id, command, value, false).ToStr());
        }

        private void bt_TeachEnter_Click(object sender, EventArgs e)
        {
            if (teachingSession)
                return;

            teachingNodeId = (int)numID.Value;
            teachingSession = true;
            teachingRecording = false;
            waitingForTeachingBrake = false;
            teachingSamples.Clear();
            SendTeachingIntCommand(0x40, (int)num_TeachAssist.Value);
            teachingTimer.Start();
            lb_TeachStatus.Text = "等待驱动板松闸";
        }

        private void bt_TeachExit_Click(object sender, EventArgs e)
        {
            if (!teachingSession)
                return;

            teachingRecording = false;
            teachingSession = false;
            waitingForTeachingBrake = true;
            SendTeachingCommand(0x41);
            teachingTimer.Start();
            lb_TeachStatus.Text = "正在抱闸";
        }

        private void teachingTimer_Tick(object sender, EventArgs e)
        {
            if (!teachingSession && !waitingForTeachingBrake)
                return;

            if (waitingForTeachingBrake)
            {
                // 退出示教后只查询刹车状态，不再发送心跳或位置查询。
                SendTeachingCommand(0x44);
                return;
            }

            // 心跳和位置查询共用一个 50 ms 周期；驱动板 200 ms 无心跳会自动抱闸。
            SendTeachingCommand(0x42);
            if (teachingRecording)
                Serial?.PushDataToQueue(SlcanParser.ParseSlcanFrameStr(teachingNodeId, 0x23));
            else
                SendTeachingCommand(0x44);
        }

        private void bt_TeachSave_Click(object sender, EventArgs e)
        {
            if (teachingSamples.Count == 0)
            {
                MessageBox.Show("当前没有可保存的示教轨迹。", "示教");
                return;
            }

            using (var dialog = new SaveFileDialog {
                Filter = "轨迹文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
                FileName = "teaching-trajectory.csv",
                Title = "保存示教轨迹"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                using (var writer = new StreamWriter(dialog.FileName, false, Encoding.UTF8))
                {
                    writer.WriteLine("time_s,position_turn");
                    foreach (var sample in teachingSamples)
                        writer.WriteLine(sample.TimeSeconds.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + "," +
                                         sample.Position.ToString("F6", System.Globalization.CultureInfo.InvariantCulture));
                }
                lb_TeachStatus.Text = "轨迹已保存";
            }
        }

        private void bt_TeachClear_Click(object sender, EventArgs e)
        {
            if (replayCancellation != null)
            {
                replayCancellation.Cancel();
                SendTeachingCommand(0x46);
                replayCancellation = null;
                bt_TeachReplay.Text = "演示轨迹";
            }
            teachingSamples.Clear();
            list_Postions.Items.Clear();
            lb_TeachStatus.Text = "轨迹已清除";
        }

        private void bt_TeachReplay_Click(object sender, EventArgs e)
        {
            if (replayCancellation != null)
            {
                replayCancellation.Cancel();
                SendTeachingCommand(0x46);
                replayCancellation = null;
                bt_TeachReplay.Text = "演示轨迹";
                lb_TeachStatus.Text = "回放已停止";
                return;
            }

            if (teachingSamples.Count < 2)
            {
                MessageBox.Show("至少需要两条示教轨迹点。", "示教");
                return;
            }

            var samples = teachingSamples.Select(sample => new TeachingSample {
                Position = sample.Position,
                TimeSeconds = sample.TimeSeconds
            }).ToList();
            var id = (int)numID.Value;
            replayCancellation = new CancellationTokenSource();
            var token = replayCancellation.Token;
            bt_TeachReplay.Text = "停止演示";
            lb_TeachStatus.Text = "准备轨迹回放";
            SendTeachingCommand(0x45);
            Task.Run(() => ReplayTeachingTrajectory(id, samples, token), token);
        }

        private void ReplayTeachingTrajectory(int id, List<TeachingSample> samples, CancellationToken token)
        {
            try
            {
                Thread.Sleep(120);
                for (int i = 1; i < samples.Count && !token.IsCancellationRequested; i++)
                {
                    float duration = samples[i].TimeSeconds - samples[i - 1].TimeSeconds;
                    if (duration < 0.03f) duration = 0.03f;
                    if (duration > 2.0f) duration = 2.0f;
                    Serial?.PushDataToQueue(SlcanParser.ParseSlcanFrameStr(
                        id, 0x06, samples[i].Position, duration));
                    Thread.Sleep((int)(duration * 1000.0f));
                }
            }
            finally
            {
                if (!token.IsCancellationRequested)
                    Serial?.PushDataToQueue(SlcanParser.ParseSlcanFrameStr(id, 0x46));
                BeginInvoke(new Action(() => {
                    replayCancellation = null;
                    bt_TeachReplay.Text = "演示轨迹";
                    lb_TeachStatus.Text = token.IsCancellationRequested ? "回放已停止" : "回放完成，正在抱闸";
                }));
            }
        }

        //使能(失能)电机
        private void bt_Enable_Click(object sender, EventArgs e)
        {
            var Id = (int)numID.Value;
            // 0x01 carries a uint32_t: 1 enables the drive and 0 disables it.
            var enable = bt_Enable.Text.Equals("使能电机");
            var frame = SlcanParser.ParseSlcanFrameInt(Id, 0x01, enable ? 1 : 0, resACK: false).ToStr();
            Serial?.PushDataToQueue(frame);
            if (enable)
            {
                bt_Enable.Text = "失能电机";
            }
            else
            {
                bt_Enable.Text = "使能电机";
            }
        }

        //校准电机
        private void bt_Call_Click(object sender, EventArgs e)
        {
            var Id = (int)numID.Value;
            var frame = SlcanParser.ParseSlcanFrameStr(Id, 0x02);
            Serial?.PushDataToQueue(frame);
        }

        //转一圈
        private void bt_PosOnce_Click(object sender, EventArgs e)
        {
            var Id = (int)numID.Value;
            var frame = SlcanParser.ParseSlcanFrameStr(Id, 0x05, 1);
            Serial?.PushDataToQueue(frame);
        }

        //以一圈每秒的速度旋转
        private void bt_SpeedOnce_Click(object sender, EventArgs e)
        {
            var Id = (int)numID.Value;
            var frame = SlcanParser.ParseSlcanFrameStr(Id, 0x04, 1);
            Serial?.PushDataToQueue(frame);
        }

        //以1A的电流旋转
        private void bt_InOnce_Click(object sender, EventArgs e)
        {
            var Id = (int)numID.Value;
            var frame = SlcanParser.ParseSlcanFrameStr(Id, 0x03, 1);
            Serial?.PushDataToQueue(frame);
        }

        //马上停止
        private void bt_StopNow_Click(object sender, EventArgs e)
        {
            var Id = (int)numID.Value;
            var frame = SlcanParser.ParseSlcanFrameStr(Id, 0x04, 0);
            Serial?.PushDataToQueue(frame);
            IsStop = true;
        }
        //转到xx圈，绝度位移
        private void bt_SetPosCom_Click(object sender, EventArgs e)
        {
            var Id = (int)numID.Value;
            var value = (float)num_Position.Value;
            var frame = SlcanParser.ParseSlcanFrameStr(Id, 0x05, value);
            Serial?.PushDataToQueue(frame);
        }
        //设置以xx速度运行
        private void bt_SetVelocityCom_Click(object sender, EventArgs e)
        {
            var Id = (int)numID.Value;
            var value = (float)num_Velocity.Value;
            var frame = SlcanParser.ParseSlcanFrameStr(Id, 0x04, value);
            Serial?.PushDataToQueue(frame);
        }
        //以xx的电流运行电机
        private void bt_SetCurrentCom_Click(object sender, EventArgs e)
        {
            var Id = (int)numID.Value;
            // The UI is in mA, while CAN command 0x03 is a float in amperes.
            var value = (float)num_Current.Value / 1000.0f;
            var frame = SlcanParser.ParseSlcanFrameStr(Id, 0x03, value);
            Serial?.PushDataToQueue(frame);
        }
        //设置限制电流
        private void bt_SetLimitCurrent_Click(object sender, EventArgs e)
        {
            var Id = (int)numID.Value;
            var value = ((float)(numLimitCurrent.Value)) * 0.001f;
            var frame = SlcanParser.ParseSlcanFrameStr(Id, 0x12, value, resACK: true);
            Serial?.PushDataToQueue(frame);
        }

        //设置限制速度
        private void bt_SetLimitVelocity_Click(object sender, EventArgs e)
        {
            var Id = (int)numID.Value;
            var value = (float)num_LimitVelocity.Value;
            var frame = SlcanParser.ParseSlcanFrameStr(Id, 0x13, value, resACK: true);
            Serial?.PushDataToQueue(frame);
        }

        //设置当前为起始点
        private void bt_SetThisIsHome_Click(object sender, EventArgs e)
        {
            var Id = (int)numID.Value;
            var frame = SlcanParser.ParseSlcanFrameStr(Id, 0x15);
            Serial?.PushDataToQueue(frame);
        }

        //恢复出厂默认值
        private void bt_ReSet_Click(object sender, EventArgs e)
        {
            var Id = (int)numID.Value;
            var frame = SlcanParser.ParseSlcanFrameStr(Id, 0x7E);
            Serial?.PushDataToQueue(frame);
            Thread.Sleep(50);
            frame = SlcanParser.ParseSlcanFrameStr(Id, 0x7F);
            Serial?.PushDataToQueue(frame);
        }

        //清除硬件过流故障锁存
        private void bt_ClearFault_Click(object sender, EventArgs e)
        {
            var Id = (int)numID.Value;
            var frame = SlcanParser.ParseSlcanFrameStr(Id, 0x7C);
            Serial?.PushDataToQueue(frame);
            JLog.Info($"已发送清除驱动故障命令，ID={Id}");
        }

        //设置 Kp
        private void bt_SetKp_Click(object sender, EventArgs e)
        {
            var Id = (int)numID.Value;
            var value = (int)num_Kp.Value;
            var frame = SlcanParser.ParseSlcanFrameInt(Id, 0x17, value).ToStr();
            Serial?.PushDataToQueue(frame);
        }

        //设置 Kv
        private void bt_SetKv_Click(object sender, EventArgs e)
        {
            var Id = (int)numID.Value;
            var value = (int)num_Kv.Value;
            var frame = SlcanParser.ParseSlcanFrameInt(Id, 0x18, value).ToStr();
            Serial?.PushDataToQueue(frame);
        }

        //设置 Ki
        private void bt_SetKi_Click(object sender, EventArgs e)
        {
            var Id = (int)numID.Value;
            var value = (int)num_Ki.Value;
            var frame = SlcanParser.ParseSlcanFrameInt(Id, 0x19, value).ToStr();
            Serial?.PushDataToQueue(frame);
        }

        //设置 Kd
        private void bt_SetKd_Click(object sender, EventArgs e)
        {
            var Id = (int)numID.Value;
            var value = (int)num_Kd.Value;
            var frame = SlcanParser.ParseSlcanFrameInt(Id, 0x1A, value).ToStr();
            Serial?.PushDataToQueue(frame);
        }

        //删除最后一个位置
        private void bt_DeleteLast_Click(object sender, EventArgs e)
        {
            if (list_Postions.Items.Count == 0)
            {
                MessageBox.Show("没有得删了哟");
            }
            list_Postions.Items.RemoveAt(list_Postions.Items.Count - 1);
        }

        //设置 A 相电流
        private void bt_ACurrent_Click(object sender, EventArgs e)
        {
            var Id = (int)numID.Value;
            var value = (int)num_ACurrent.Value;
            var frame = SlcanParser.ParseSlcanFrameInt(Id, 0x08, value).ToStr();
            Serial?.PushDataToQueue(frame);
        }

        //设置 B 相电流
        private void bt_BCurrent_Click(object sender, EventArgs e)
        {
            var Id = (int)numID.Value;
            var value = (int)num_BCurrent.Value;
            var frame = SlcanParser.ParseSlcanFrameInt(Id, 0x09, value).ToStr();
            Serial?.PushDataToQueue(frame);
        }

        //记录当前位置
        private void bt_RecordPos_Click(object sender, EventArgs e)
        {
            Task.Run(() => {
                var Id = (int)numID.Value;
                var frame = SlcanParser.ParseSlcanFrameStr(Id, 0x23);
                Serial?.PushDataToQueue(frame);
                int count = 0;
                while (true && count < 10)
                {
                    JLog.Info(PosTime.ToString());
                    count++;
                    if (PosTime.AddSeconds(1) > DateTime.Now)
                    {
                        this.Invoke(new Action(() => {
                            //一秒之内获取的，还比较新鲜，就用这个
                            list_Postions.Items.Add(PosNow);
                        }));
                        return;
                    }
                    Thread.Sleep(10);
                }

                this.Invoke(new Action(() => {
                    MessageBox.Show("获取当前位置失败");
                }));


            });

        }

        //循环运动
        private void bt_LoopPos_Click(object sender, EventArgs e)
        {
            IsStop = false;
            Task.Run(() =>
            {
                while (!IsStop)
                {

                    var items = list_Postions.Items;
                    if (items.Count == 0)
                    {
                        IsStop = true;
                        JLog.Info("没有循环数据");
                    }


                    for (int i = 0; i < items.Count; i++)
                    {
                        // 发送移动命令
                        var val = Convert.ToString(items[i]);
                        if (!float.TryParse(val, out float res))
                        {
                            continue;
                        }
                        //循环间隔时间
                        Thread.Sleep((int)num_LoopSleepTime.Value);
                        while (Math.Abs(PosNow - res) > 0.01 && !IsStop)
                        {
                            var Id = (int)numID.Value;
                            var frame = SlcanParser.ParseSlcanFrameStr(Id, 0x05, res);
                            Serial?.PushDataToQueue(frame);
                            this.Invoke(new Action(() =>
                            {
                                //设置select;
                                list_Postions.SelectedItem = items[i];
                            }));
                            Thread.Sleep(100);
                            //读取当前位置
                            Id = (int)numID.Value;
                            frame = SlcanParser.ParseSlcanFrameStr(Id, 0x23);
                            Serial?.PushDataToQueue(frame);
                            Thread.Sleep(10);
                        }
                    }

                }
            });
        }


        //打开说明文档
        private void bt_OpenDocunment_Click(object sender, EventArgs e)
        {
            string pdfPath = @"电机说明文档.pdf";

            if (!System.IO.File.Exists(pdfPath))
            {
                MessageBox.Show("文件不存在！");
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo()
                {
                    FileName = pdfPath,
                    UseShellExecute = true  // 必须为 true 才能使用系统默认程序
                });
            }
            catch (Exception ex)
            {
                JLog.Error(ex);
            }
        }
    }
}
