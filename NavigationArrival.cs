using UnityEngine;

namespace MapTracker
{
    internal static class NavigationArrival
    {
        internal const int Distance = 5;
        internal static bool Reached(Vector3 origin, Vector3 destination, bool interior, bool teleporting, bool point)
        {
            if (interior || teleporting) return false;
            float x = origin.x - destination.x, z = origin.z - destination.z;
            return x * x + z * z <= Distance * Distance && (point || System.Math.Abs(origin.y - destination.y) <= Distance * 2);
        }
    }
}
