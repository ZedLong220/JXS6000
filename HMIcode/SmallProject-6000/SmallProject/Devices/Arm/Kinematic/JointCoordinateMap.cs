using System;

namespace SmallProject.Devices.Arm.Kinematic
{
    // Preserve the existing CAN/AI coordinates; the UI uses URDF assembly zero.
    public static class JointCoordinateMap
    {
        private static readonly double[] LegacyHome = { 0, 0, 180, 0, -90, 0 };
        public static double[] ToModel(double[] legacy) => Convert(legacy, -1);
        public static double[] ToLegacy(double[] model) => Convert(model, 1);
        private static double[] Convert(double[] values, int direction)
        {
            if (values == null || values.Length != 6) throw new ArgumentException("需要六个关节角度");
            var result = new double[6];
            for (int i = 0; i < 6; i++)
            {
                if (!double.IsFinite(values[i])) throw new ArgumentException("关节角度必须是有限数字");
                result[i] = values[i] + direction * LegacyHome[i];
            }
            return result;
        }
    }
}
