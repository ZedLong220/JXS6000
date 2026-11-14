using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SmallProject.Configs
{
    public class JConfiguration
    {
        public const double DriverCompatibleMaxAcceleration = 23.0;
        public int ReductionJ1 { get; set; } = 50;
        public int ReductionJ2 { get; set; } = 100;
        public int ReductionJ3 { get; set; } = 100;
        public int ReductionJ4 { get; set; } = 30;
        public int ReductionJ5 { get; set; } = 30;
        public int ReductionJ6 { get; set; } = 1;

        // D_BASE = 0, L_BASE = 161.5, L_ARM = 170, D_ELBOW = 70, L_FOREARM = 117, L_WRIST = 97
        public float L_BASE { get; set; } = 0;
        public float D_BASE { get; set; } = 161.5f;
        public float L_ARM { get; set; } = 170;
        public float D_ELBOW { get; set; } = 70;
        public float L_FOREARM { get; set; } = 117;
        public float L_WRIST { get; set; } = 97;

        // 机械臂关节速度限制，单位 degree/s；电机脉冲频率还会单独限幅。
        public float Speed { get; set; } = 60.0f;
        public double MotorMaxVelocity { get; set; } = 15.0;
        public double MotorMaxAcceleration { get; set; } = DriverCompatibleMaxAcceleration;
        public double MotorMaxJerk { get; set; } = 500.0;

        public bool TryValidateMotion(out string error)
        {
            if(new[]{ReductionJ1,ReductionJ2,ReductionJ3,ReductionJ4,ReductionJ5,ReductionJ6}.Any(r=>r<1 || r>10000))
                error="减速比请输入1～10000的整数。";
            else if(!float.IsFinite(Speed) || Speed<=0 || Speed>180)
                error="关节速度请输入大于0且不超过180°/s的数值。";
            else if(!double.IsFinite(MotorMaxVelocity) || MotorMaxVelocity<=0 || MotorMaxVelocity>30)
                error="电机限速请输入大于0且不超过30圈/s的数值。";
            else if(!double.IsFinite(MotorMaxAcceleration) || MotorMaxAcceleration<=0 ||
                    MotorMaxAcceleration>DriverCompatibleMaxAcceleration)
                error=$"电机加速度请输入大于0且不超过{DriverCompatibleMaxAcceleration:F0}圈/s²的数值。";
            else if(!double.IsFinite(MotorMaxJerk) || MotorMaxJerk<=0 || MotorMaxJerk>5000)
                error="平滑限制请输入大于0且不超过5000圈/s³的数值。";
            else { error=string.Empty; return true; }
            return false;
        }
        //最大电流值 单位（A）
        public float Current { get; set; } = 1f;

        //小智连接的端点
        public string EndpointUrl { get; set; } = Environment.GetEnvironmentVariable("XIAOZHI_MCP_ENDPOINT") ?? string.Empty;

        public string FindModelPath { get; set; } = "DETECT_IMAGE";
    }
}
