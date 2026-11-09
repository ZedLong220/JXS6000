using System;
using System.Collections.Generic;
using System.Linq;

namespace SmallProject.Devices.Arm.Kinematic
{
    // Explicit participating axes and per-block sequences. All payloads fit
    // classic CAN; the transport waits for a controller ACK for every frame.
    public static class MultiAxisProtocol
    {
        public sealed class Command
        {
            public int Code;
            public byte[] Data = Array.Empty<byte>();
        }
        private static byte[] Float(float value) => BitConverter.IsLittleEndian ?
            BitConverter.GetBytes(value) : BitConverter.GetBytes(value).Reverse().ToArray();

        public static List<Command> Encode(ContinuousPathPlanner.Plan plan,int[] axes,bool repeat)
        {
            if (axes.Length<1 || axes.Length>5 || axes.Distinct().Count()!=axes.Length || axes.Any(i=>i<0 || i>4) ||
                plan.Start.Length!=axes.Length || plan.Segments.Count<1 || plan.Segments.Count>32)
                throw new ArgumentException("五轴路径协议参数无效");
            byte mask=(byte)axes.Sum(i=>1<<i);
            var commands=new List<Command> { new() {Code=0x50,Data=new byte[]{3,mask}} };
            for(int i=0;i<axes.Length;++i)
                commands.Add(new() {Code=0x51,Data=new[]{(byte)(axes[i]+1)}.Concat(Float(plan.Start[i])).ToArray()});
            for(int seq=0;seq<plan.Segments.Count;++seq)
            {
                var segment=plan.Segments[seq];
                if(segment.End.Length!=axes.Length || segment.EndVelocity.Length!=axes.Length)
                    throw new ArgumentException("五轴段数据不完整");
                commands.Add(new() {Code=0x52,Data=Float(segment.Duration).Concat(new[]{(byte)seq,(byte)(seq>>8)}).ToArray()});
                for(int i=0;i<axes.Length;++i) for(int kind=0;kind<=1;++kind)
                    commands.Add(new() {Code=0x53,Data=new[]{(byte)(axes[i]+1),(byte)kind,(byte)seq,(byte)(seq>>8)}
                        .Concat(Float(kind==0?segment.End[i]:segment.EndVelocity[i])).ToArray()});
                commands.Add(new() {Code=0x54,Data=new[]{(byte)seq,(byte)(seq>>8)}});
            }
            commands.Add(new() {Code=0x3A,Data=new[]{repeat?(byte)2:(byte)1}});
            return commands;
        }
    }
}
