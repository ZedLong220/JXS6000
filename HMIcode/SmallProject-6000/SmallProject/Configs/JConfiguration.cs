using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SmallProject.Configs
{
    public class JConfiguration
    {
        public int ReductionJ1 { get; set; } = 30;
        public int ReductionJ2 { get; set; } = 150;
        public int ReductionJ3 { get; set; } = 150;
        public int ReductionJ4 { get; set; } = 100;
        public int ReductionJ5 { get; set; } = 100;
        public int ReductionJ6 { get; set; } = 1;

        // D_BASE = 0, L_BASE = 161.5, L_ARM = 170, D_ELBOW = 70, L_FOREARM = 117, L_WRIST = 97
        public float L_BASE { get; set; } = 0;
        public float D_BASE { get; set; } = 161.5f;
        public float L_ARM { get; set; } = 170;
        public float D_ELBOW { get; set; } = 70;
        public float L_FOREARM { get; set; } = 117;
        public float L_WRIST { get; set; } = 97;

        // 机械臂关节速度限制，单位 degree/s；电机脉冲频率还会单独限幅。
        public float Speed { get; set; } = 20.0f;
        //最大电流值 单位（A）
        public float Current { get; set; } = 1f;

        //小智连接的端点
        public string EndpointUrl { get; set; } = Environment.GetEnvironmentVariable("XIAOZHI_MCP_ENDPOINT") ?? string.Empty;

        public string FindModelPath { get; set; } = "DETECT_IMAGE";
    }
}
