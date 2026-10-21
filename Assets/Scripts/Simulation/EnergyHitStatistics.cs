using UnityEngine;

namespace Simulation.EnergyHit
{
    /// <summary>
    /// Runtime counters for successfully launched projectiles and accepted hits.
    /// An accepted hit is a hit that actually changes an active fan state.
    /// </summary>
    public static class EnergyHitStatistics
    {
        public static int FiredCount { get; private set; }
        public static int AcceptedHitCount { get; private set; }
        public static int RosFireRequestCount { get; private set; }
        public static int RosFireRejectedCount { get; private set; }
        public static int EarlyCollisionCount { get; private set; }
        public static int RigCollisionCount { get; private set; }
        public static int RejectedTargetCount { get; private set; }
        public static string LastImpact { get; private set; } = "none";
        public static string LastEarlyImpact { get; private set; } = "none";
        public static string LastTargetRejection { get; private set; } = "none";

        public static float HitRate => FiredCount > 0
            ? (float)AcceptedHitCount / FiredCount
            : 0f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            Reset();
        }

        public static void RecordFired()
        {
            FiredCount++;
        }

        public static void RecordAcceptedHit()
        {
            AcceptedHitCount++;
        }

        public static void RecordRosFireRequest(bool accepted)
        {
            RosFireRequestCount++;
            if (!accepted) RosFireRejectedCount++;
        }

        public static void RecordProjectileImpact(string colliderName, float flightSeconds,
            bool hitOwnRig, bool accepted)
        {
            LastImpact = $"{(accepted ? "HIT" : "BLOCKED")} {colliderName} ({flightSeconds * 1000f:0}ms)";
            if (flightSeconds < 0.08f)
            {
                EarlyCollisionCount++;
                LastEarlyImpact = LastImpact;
                Debug.LogWarning($"Projectile collided early with {LastEarlyImpact}.");
            }
            if (hitOwnRig) RigCollisionCount++;
        }

        public static void RecordTargetRejection(string targetName, string reason)
        {
            RejectedTargetCount++;
            LastTargetRejection = $"{targetName}: {reason}";
        }

        public static void Reset()
        {
            FiredCount = 0;
            AcceptedHitCount = 0;
            RosFireRequestCount = 0;
            RosFireRejectedCount = 0;
            EarlyCollisionCount = 0;
            RigCollisionCount = 0;
            RejectedTargetCount = 0;
            LastImpact = "none";
            LastEarlyImpact = "none";
            LastTargetRejection = "none";
        }
    }
}
