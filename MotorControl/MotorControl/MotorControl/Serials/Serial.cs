using MotorControl.Logger;
using MotorControl.Serials.Slcan;
using RJCP.IO.Ports;
using System;
using System.Collections.Concurrent;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MotorControl.Serials
{
    internal enum InfoType
    {
        Current,
        Velocity,
        Position,
        CurrentLimit,
        VelocityLimit,
        Offset,
        Kp,
        Kv,
        Ki,
        Kd,
        LimitReached,
        TeachingStatus
    }

    internal class Serial
    {
        // 发送队列会同时被界面线程和后台发送线程访问，必须使用线程安全容器。
        private readonly ConcurrentQueue<string> DataToSend = new ConcurrentQueue<string>();
        private readonly object streamLock = new object();
        private CancellationTokenSource sendCancellation;
        private Task sendTask;
        //接收缓冲区（解决串口半包问题）
        private string receiveBuffer = "";
        private readonly object bufferLock = new object();
        public event Action<int, float, InfoType> GetResInfo;

        public void PushDataToQueue(string data)
        {
            if (!OpenResult)
            {
                JLog.Info("请先打开连接");
                return;
            }
            if (string.IsNullOrEmpty(data))
                return;

            DataToSend.Enqueue(data);
            //JLog.Info($"加入队列:{data}");
        }

        private volatile bool OpenResult;

        //串口工具
        private SerialPortStream stream;
        public bool Open(string name)
        {
            try
            {
                SerialDispose();

                stream = new SerialPortStream(name, 115200, 8, RJCP.IO.Ports.Parity.None, RJCP.IO.Ports.StopBits.One);
                stream.ReadTimeout = 1000;

                stream.DataReceived += Stream_DataReceived;
                stream.Open();
                OpenResult = true;
                LoopSend();
                //设置波特率
                PushDataToQueue("S8\r");
                //开始
                PushDataToQueue("O\r");
            }
            catch (Exception)
            {
                OpenResult = false;

            }
            return OpenResult;
        }

        //循环发送数据
        private void LoopSend()
        {
            sendCancellation = new CancellationTokenSource();
            CancellationToken token = sendCancellation.Token;
            sendTask = Task.Run(() =>
            {
                while (OpenResult && !token.IsCancellationRequested)
                {
                    string send = null;
                    try
                    {
                        if (!DataToSend.TryDequeue(out send))
                        {
                            token.WaitHandle.WaitOne(5);
                            continue;
                        }

                        lock (streamLock)
                        {
                            if (stream != null && stream.IsOpen)
                                stream.Write(send);
                        }

                        // SLCAN 适配器需要一个很短的帧间隔。
                        token.WaitHandle.WaitOne(10);
                    }
                    catch (Exception e)
                    {
                        JLog.Error(e);
                        // 写串口失败时不要静默丢掉控制命令；连接仍有效时放回队列。
                        if (!string.IsNullOrEmpty(send) && OpenResult && !token.IsCancellationRequested)
                            DataToSend.Enqueue(send);
                        token.WaitHandle.WaitOne(20);
                    }
                }
            });
        }

        //数据接收
        private void Stream_DataReceived(object sender, RJCP.IO.Ports.SerialDataReceivedEventArgs e)
        {
            var sp = sender as SerialPortStream;
            var recieveText = sp.ReadExisting();

            //追加到缓冲区，按 \r 分割出完整帧
            lock (bufferLock)
            {
                receiveBuffer += recieveText;
            }

            //循环处理所有完整帧（以\r结尾）
            while (true)
            {
                string frame;
                lock (bufferLock)
                {
                    int idx = receiveBuffer.IndexOf('\r');
                    if (idx < 0) break;
                    frame = receiveBuffer.Substring(0, idx);
                    receiveBuffer = receiveBuffer.Substring(idx + 1);
                }

                if (string.IsNullOrEmpty(frame)) continue;
                var aFrame = SlcanParser.ParseSlcanFrame(frame);
                if (aFrame != null)
                {
                    switch (aFrame.Cmd)
                    {
                        case 0x21:
                            //电流信息(原始单位为A，转换为mA显示)
                            {
                                var current = BitConverter.ToSingle(aFrame.Data.Take(4).ToArray(), 0);
                                GetResInfo?.Invoke(aFrame.Id, current * 1000f, InfoType.Current);
                                JLog.Info($"电流 {current}");
                            }
                            break;
                        case 0x22:
                            //速度信息
                            {
                                var velocity = BitConverter.ToSingle(aFrame.Data.Take(4).ToArray(), 0);
                                GetResInfo?.Invoke(aFrame.Id, velocity, InfoType.Velocity);
                            }
                            break;
                        case 0x23:
                            //角度信息
                            {
                                var angle = BitConverter.ToSingle(aFrame.Data.Take(4).ToArray(), 0);
                                GetResInfo?.Invoke(aFrame.Id, angle, InfoType.Position);
                            }
                            break;
                        case 0x26:
                            //限制电流
                            {
                                var currentLimit = BitConverter.ToSingle(aFrame.Data.Take(4).ToArray(), 0);
                                GetResInfo?.Invoke(aFrame.Id, currentLimit, InfoType.CurrentLimit);
                            }
                            break;
                        case 0x27:
                            //限制速度
                            {
                                var velocityLimit = BitConverter.ToSingle(aFrame.Data.Take(4).ToArray(), 0);
                                GetResInfo?.Invoke(aFrame.Id, velocityLimit, InfoType.VelocityLimit);
                            }
                            break;
                        case 0x28:
                            //Kp
                            {
                                var kp = BitConverter.ToSingle(aFrame.Data.Take(4).ToArray(), 0);
                                GetResInfo?.Invoke(aFrame.Id, kp, InfoType.Kp);
                            }
                            break;
                        case 0x29:
                            //Kv
                            {
                                var kv = BitConverter.ToSingle(aFrame.Data.Take(4).ToArray(), 0);
                                GetResInfo?.Invoke(aFrame.Id, kv, InfoType.Kv);
                            }
                            break;
                        case 0x2A:
                            //Ki
                            {
                                var ki = BitConverter.ToSingle(aFrame.Data.Take(4).ToArray(), 0);
                                GetResInfo?.Invoke(aFrame.Id, ki, InfoType.Ki);
                            }
                            break;
                        case 0x2B:
                            //Kd
                            {
                                var kd = BitConverter.ToSingle(aFrame.Data.Take(4).ToArray(), 0);
                                GetResInfo?.Invoke(aFrame.Id, kd, InfoType.Kd);
                            }
                            break;
                        case 0x32:
                            //限位触发反馈：data[0] = 1左，2右，3左右。
                            {
                                float side = aFrame.Data.Length > 0 ? aFrame.Data[0] : 0;
                                GetResInfo?.Invoke(aFrame.Id, side, InfoType.LimitReached);
                                JLog.Info($"限位触发，方向代码 {side}");
                            }
                            break;
                        case 0x40:
                        case 0x41:
                        case 0x42:
                        case 0x43:
                        case 0x44:
                        case 0x45:
                        case 0x46:
                            // data[0]=示教状态，data[1]=刹车抱闸，data[2]=限位，data[3]=故障。
                            if (aFrame.Data.Length > 0)
                                GetResInfo?.Invoke(aFrame.Id, aFrame.Data[0], InfoType.TeachingStatus);
                            break;
                    }
                }
            }
        }

        public bool SerialDispose()
        {
            OpenResult = false;

            CancellationTokenSource cancellation = sendCancellation;
            if (cancellation != null)
                cancellation.Cancel();

            SerialPortStream oldStream = stream;
            if (oldStream != null)
            {
                try
                {
                    // 关闭 SLCAN 通道，命令必须以回车结尾。
                    lock (streamLock)
                    {
                        if (oldStream.IsOpen)
                            oldStream.Write("C\r");
                    }
                }
                catch (Exception e)
                {
                    JLog.Error(e);
                }

                try
                {
                    oldStream.DataReceived -= Stream_DataReceived;
                    oldStream.Close();
                    oldStream.Dispose();
                }
                catch (Exception e)
                {
                    JLog.Error(e);
                }
            }

            Task oldSendTask = sendTask;
            if (oldSendTask != null && !oldSendTask.IsCompleted)
            {
                try
                {
                    oldSendTask.Wait(200);
                }
                catch (Exception e)
                {
                    JLog.Error(e);
                }
            }

            if (cancellation != null)
                cancellation.Dispose();

            sendCancellation = null;
            sendTask = null;
            stream = null;
            while (DataToSend.TryDequeue(out _)) { }
            return true;
        }
    }
}
