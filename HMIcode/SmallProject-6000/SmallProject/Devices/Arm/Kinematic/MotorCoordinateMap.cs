using System;

namespace SmallProject.Devices.Arm.Kinematic
{
    // Preserve model joint signs. The six installed motors use the opposite
    // motor-coordinate direction to the original harness. Targets, velocities
    // and encoder feedback share these directions through motorJ.
    public static class MotorCoordinateMap
    {
        private static readonly int[] Directions = { -1, 1, -1, 1, -1, -1 };

        public static int Direction(int axis)
        {
            if(axis<0 || axis>=Directions.Length) throw new ArgumentOutOfRangeException(nameof(axis));
            return Directions[axis];
        }
    }
}
