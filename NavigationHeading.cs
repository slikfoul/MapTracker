using System;

namespace MapTracker
{
    internal static class NavigationHeading
    {
        // Positive angles point right relative to the horizontal camera heading.
        internal static float Relative(float x, float z, float cameraX, float cameraZ, float playerX, float playerZ)
        {
            if (x * x + z * z < 0.000001f) return 0;
            if (cameraX * cameraX + cameraZ * cameraZ < 0.000001f)
            { cameraX = playerX; cameraZ = playerZ; }
            if (cameraX * cameraX + cameraZ * cameraZ < 0.000001f)
            { cameraX = 0; cameraZ = 1; }
            double angle = (Math.Atan2(x, z) - Math.Atan2(cameraX, cameraZ)) * 180 / Math.PI;
            if (angle > 180) angle -= 360;
            if (angle < -180) angle += 360;
            return (float)angle;
        }
    }
}
