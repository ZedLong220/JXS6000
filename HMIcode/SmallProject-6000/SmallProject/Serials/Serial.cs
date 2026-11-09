using RJCP.IO.Ports;
using SharpCompress.Compressors.Xz;
using SmallProject.Devices.Arm;
using SmallProject.Logger;
using SmallProject.Serials.Slcan;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SmallProject.Serials
{
    public class Serial
    {

        private sealed class OutboundCommand
        {
            public required IReadOnlyList<string> Texts { get; init; }
            public TaskCompletionSource<bool>? Completion { get; init; }
            public CancellationToken Cancellation { get; init; }
        }

        private sealed class PendingCanAcknowledgement
        {
            public required int StandardId { get; init; }
            public required byte Dlc { get; init; }
            public required byte Checksum { get; init; }
            public required TaskCompletionSource<bool> Completion { get; init; }
        }

        private readonly ConcurrentQueue<OutboundCommand> DataToSend = new ConcurrentQueue<OutboundCommand>();
        private readonly SemaphoreSlim sendSignal = new SemaphoreSlim(0);
        private readonly object acknowledgementLock = new object();
        private readonly object decoderLock = new object();
        private readonly SlcanReceiveDecoder receiveDecoder = new SlcanReceiveDecoder();
        private PendingCanAcknowledgement? pendingAcknowledgement;
        private int transportFaulted;
        private int controllerState;
        private int controllerError;
        private int controllerWasActive;
        private long controllerStatusVersion;
        private long controllerCompletionCount;
        private int controllerPathCount;
        private int controllerPathIndex = -1;
        private int controllerAxisMask,controllerProtocolVersion;

        public int ControllerState => Volatile.Read(ref controllerState);
        public int ControllerError => Volatile.Read(ref controllerError);
        public long ControllerStatusVersion => Interlocked.Read(ref controllerStatusVersion);
        public long ControllerCompletionCount => Interlocked.Read(ref controllerCompletionCount);
        public int ControllerPathCount => Volatile.Read(ref controllerPathCount);
        public int ControllerPathIndex => Volatile.Read(ref controllerPathIndex);
        public int ControllerAxisMask => Volatile.Read(ref controllerAxisMask);
        public int ControllerProtocolVersion => Volatile.Read(ref controllerProtocolVersion);

        public void PushDataToQueue(string data)
        {
            if(!OpenResult)
            {
                JLog.Info("请先打开连接");
                return;
            }
            if (Volatile.Read(ref transportFaulted) != 0)
            {
                JLog.Info("主控逐帧确认已超时，请断开后重新连接");
                return;
            }
            EnqueueCommand(data, null);
        }

        private static bool TryCreateAcknowledgement(string text, out PendingCanAcknowledgement? pending)
        {
            pending = null;
            string frameText = text.TrimEnd('\r', '\n');
            if (frameText.Length < 5 || frameText[0] != 't') return false;
            CanFrame? frame = SlcanParser.ParseSlcanFrame(frameText);
            if (frame == null) return false;
            int standardId = (frame.Id << 7) | frame.Cmd;
            byte checksum = (byte)(standardId ^ (standardId >> 8) ^ frame.Data.Length);
            foreach (byte value in frame.Data) checksum ^= value;
            pending = new PendingCanAcknowledgement
            {
                StandardId = standardId,
                Dlc = (byte)frame.Data.Length,
                Checksum = checksum,
                Completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
            };
            return true;
        }

        private void EnqueueCommand(string data, TaskCompletionSource<bool>? completion)
        {
            DataToSend.Enqueue(new OutboundCommand { Texts = new[] { data }, Completion = completion });
            try { sendSignal.Release(); } catch (SemaphoreFullException) { }
        }

        public async Task<bool> SendAndWaitForAckAsync(string data, int timeoutMilliseconds = 1500)
        {
            if (!OpenResult) return false;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            EnqueueCommand(data, completion);
            try
            {
                return await completion.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds));
            }
            catch (TimeoutException)
            {
                JLog.Info($"串口命令确认超时: {data.TrimEnd('\r', '\n')}");
                return false;
            }
        }

        public async Task<bool> SendBatchAndWaitForAckAsync(IReadOnlyList<string> commands, CancellationToken cancellationToken = default)
        {
            if (!OpenResult || Volatile.Read(ref transportFaulted) != 0 || commands.Count == 0) return false;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            DataToSend.Enqueue(new OutboundCommand { Texts = commands.ToArray(), Completion = completion, Cancellation = cancellationToken });
            try { sendSignal.Release(); } catch (SemaphoreFullException) { }
            return await completion.Task;
        }

        private bool OpenResult;

        public bool IsOpen => OpenResult;

        //串口工具
        private static SerialPortStream stream;
        public bool Open(string name)
        {
            try
            {
                if (stream != null && stream.IsOpen)
                {
                    stream.Dispose();
                }
                stream = new SerialPortStream(name, 115200, 8, RJCP.IO.Ports.Parity.None, RJCP.IO.Ports.StopBits.One);
                stream.ReadTimeout = 1000;
                
                stream.DataReceived += Stream_DataReceived;
                stream.Open();
                OpenResult = true;
                Volatile.Write(ref transportFaulted, 0);
                while (DataToSend.TryDequeue(out _)) { }
                while (sendSignal.Wait(0)) { }
                lock (decoderLock) receiveDecoder.Reset();
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
            Task.Run(async () =>
            {
                while(OpenResult)
                {
                    try
                    {
                        if (!await sendSignal.WaitAsync(100))
                            continue;
                        while (DataToSend.TryDequeue(out OutboundCommand? command))
                        {
                            bool accepted = true;
                            foreach (string text in command.Texts)
                            {
                                if (command.Cancellation.IsCancellationRequested) { accepted=false; break; }
                                bool requiresControllerAck = TryCreateAcknowledgement(text, out PendingCanAcknowledgement? acknowledgement);
                                if (requiresControllerAck)
                                {
                                    lock (acknowledgementLock) pendingAcknowledgement = acknowledgement;
                                }
                                stream.Write(text);
                                JLog.Info($"TX {text.TrimEnd('\r', '\n')}");
                                if (!requiresControllerAck)
                                {
                                    await Task.Delay(20);
                                    continue;
                                }
                                try
                                {
                                    accepted = await acknowledgement!.Completion.Task.WaitAsync(TimeSpan.FromMilliseconds(1500));
                                }
                                catch (TimeoutException)
                                {
                                    accepted = false;
                                    Interlocked.Exchange(ref transportFaulted, 1);
                                    JLog.Info($"串口命令确认超时，已暂停发送以避免确认错位: {text.TrimEnd('\r', '\n')}");
                                }
                                finally
                                {
                                    lock (acknowledgementLock)
                                    {
                                        if (ReferenceEquals(pendingAcknowledgement, acknowledgement)) pendingAcknowledgement = null;
                                    }
                                }
                                if (!accepted)
                                {
                                    if (Volatile.Read(ref transportFaulted) == 0)
                                        JLog.Info($"主控拒绝命令: {text.TrimEnd('\r', '\n')}");
                                    break;
                                }
                            }
                            command.Completion?.TrySetResult(accepted);
                            if (!accepted && !command.Cancellation.IsCancellationRequested)
                            {
                                while (DataToSend.TryDequeue(out OutboundCommand? abandoned))
                                    abandoned.Completion?.TrySetResult(false);
                                break;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        JLog.Error(e);
                    }
                }
            });
        }

        //数据接收
        private void Stream_DataReceived(object? sender, SerialDataReceivedEventArgs e)
        {
            var sp = sender as SerialPortStream;
            var recieveText = sp.ReadExisting();
            IReadOnlyList<SlcanReceiveEvent> events;
            lock (decoderLock) events = receiveDecoder.Append(recieveText);

            foreach (var receivedEvent in events)
            {
                if (receivedEvent.IsAcknowledgement)
                {
                    PendingCanAcknowledgement? pending;
                    lock (acknowledgementLock) pending = pendingAcknowledgement;
                    if (!receivedEvent.Accepted) pending?.Completion.TrySetResult(false);
                    JLog.Info(receivedEvent.Accepted ? "RX CANable ACK" : "RX CANable NAK");
                    continue;
                }
                string frame = receivedEvent.Frame!;
                var aFrame = SlcanParser.ParseSlcanFrame(frame);
                if(aFrame!=null)
                {
                    if (aFrame.Id == 0 && aFrame.Cmd == 0x3B && aFrame.Data.Length >= 7)
                    {
                        int acknowledgedId = aFrame.Data[2] | (aFrame.Data[3] << 8);
                        PendingCanAcknowledgement? pending;
                        lock (acknowledgementLock) pending = pendingAcknowledgement;
                        if (pending != null && pending.StandardId == acknowledgedId &&
                            pending.Dlc == aFrame.Data[4] && pending.Checksum == aFrame.Data[5])
                        {
                            bool accepted = aFrame.Data[6] != 0;
                            pending.Completion.TrySetResult(accepted);
                            int counter = aFrame.Data[0] | (aFrame.Data[1] << 8);
                            JLog.Info($"RX 主控{(accepted ? "ACK" : "NAK")} #{counter}，CAN ID=0x{acknowledgedId:X3}");
                        }
                        continue;
                    }
                    // Multiple CAN masters share the bus. DLC=0 identifies a
                    // query and must not be interpreted as drive feedback.
                    if (aFrame.Data.Length == 0)
                        continue;

                    switch(aFrame.Cmd) {
                        case 0x38:
                            if (aFrame.Id != 0 || aFrame.Data.Length < 4) break;
                            string[] controllerStates = {
                                "空闲", "装载路径", "启动路径", "连续过渡",
                                "CAN闭环运行", "到位校验", "停止中", "故障锁存"
                            };
                            string[] controllerErrors = {
                                "无", "CAN轨迹下发失败", "驱动板编码器反馈超时",
                                "驱动板未在规定时间内到位", "连续路径协议不匹配或装载确认超时",
                                "驱动路径故障（心跳、限位、堵转或参数检查失败）", "路径起点与编码器位置不一致"
                            };
                            int state = aFrame.Data[0];
                            int error = aFrame.Data[1];
                            Volatile.Write(ref controllerState, state);
                            Volatile.Write(ref controllerError, error);
                            if (aFrame.Data.Length >= 6)
                            {
                                Volatile.Write(ref controllerPathCount, aFrame.Data[4]);
                                Volatile.Write(ref controllerPathIndex,
                                    aFrame.Data[5] == 0xFF ? -1 : aFrame.Data[5]);
                            }
                            if (aFrame.Data.Length >= 8)
                            {
                                Volatile.Write(ref controllerAxisMask, aFrame.Data[6]);
                                Volatile.Write(ref controllerProtocolVersion, aFrame.Data[7]);
                            }
                            Interlocked.Increment(ref controllerStatusVersion);
                            if (state == 0)
                            {
                                if (Interlocked.Exchange(ref controllerWasActive, 0) != 0)
                                    Interlocked.Increment(ref controllerCompletionCount);
                            }
                            else if (state == 2 || state == 4 || state == 5)
                            {
                                Volatile.Write(ref controllerWasActive, 1);
                            }
                            else if (state == 7) Volatile.Write(ref controllerWasActive, 0);
                            string stateText = state < controllerStates.Length ? controllerStates[state] : $"未知状态{state}";
                            string errorText = error < controllerErrors.Length ? controllerErrors[error] : $"未知错误{error}";
                            string capabilities = aFrame.Data.Length >= 8
                                ? $"，参与轴=0x{aFrame.Data[6]:X2}，协议={aFrame.Data[7]}（bit0～4=J1～J5）"
                                : string.Empty;
                            JLog.Info($"主控状态：{stateText}，错误：{errorText}，在线轴=0x{aFrame.Data[2]:X2}，运动轴=0x{aFrame.Data[3]:X2}{capabilities}");
                            break;
                        case 0x21:
                            //电流信息
                            if (aFrame.Data.Length < 4) break;
                            var current = BitConverter.ToSingle(aFrame.Data.Take(4).ToArray());
                            App.Core.Jyker.RecieveCurrent(aFrame.Id, current);
                            JLog.Info($"电流 {current}");
                            break;
                        case 0x22:
                            // 速度信息
                            if (aFrame.Data.Length < 4) break;
                            var velocity = BitConverter.ToSingle(aFrame.Data.Take(4).ToArray());
                            App.Core.Jyker.RecieveVelocity(aFrame.Id, velocity);
                            break;
                        case 0x23:
                            //角度信息
                            if (aFrame.Data.Length < 5) break;
                            var angle = BitConverter.ToSingle(aFrame.Data.Take(4).ToArray());
                            var isFinish = BitConverter.ToBoolean(aFrame.Data.Skip(4).Take(1).ToArray());
                            App.Core.Jyker.RecievePos(aFrame.Id, angle, isFinish);
                            break;
                        
                    }
                }
            }
        }

        public bool SerialDispose()
        {
            try
            {
                OpenResult = false;
                if (stream == null || !stream.IsOpen)
                {
                    return true;
                }

                // 关闭 CAN 传输后再关闭串口。
                stream.Write("C\r");
                stream.Close();
                return true;
            }
            catch (Exception ex)
            {
                JLog.Error(ex);
                return false;
            }
        }

        
    }
}
