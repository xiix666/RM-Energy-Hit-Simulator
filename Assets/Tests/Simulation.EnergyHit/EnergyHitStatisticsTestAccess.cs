namespace Simulation.EnergyHit.Tests
{
    // Thin forwarding adapter: assertions still exercise the production counters,
    // without an invalid asmdef reference to the predefined Assembly-CSharp.
    internal static class EnergyHitStatistics
    {
        private static System.Type Type => EnergyHitTestReflection.GetType("Simulation.EnergyHit.EnergyHitStatistics");
        private static T Get<T>(string name) => (T)Type.GetProperty(name).GetValue(null);
        public static int FiredCount => Get<int>("FiredCount");
        public static int AcceptedHitCount => Get<int>("AcceptedHitCount");
        public static int RosFireRequestCount => Get<int>("RosFireRequestCount");
        public static int RosFireRejectedCount => Get<int>("RosFireRejectedCount");
        public static int EarlyCollisionCount => Get<int>("EarlyCollisionCount");
        public static int RigCollisionCount => Get<int>("RigCollisionCount");
        public static int RejectedTargetCount => Get<int>("RejectedTargetCount");
        public static float HitRate => Get<float>("HitRate");
        public static string LastImpact => Get<string>("LastImpact");
        public static string LastEarlyImpact => Get<string>("LastEarlyImpact");
        public static string LastTargetRejection => Get<string>("LastTargetRejection");
        public static void Reset() => EnergyHitTestReflection.InvokeStatic(Type, "Reset");
        public static void RecordFired() => EnergyHitTestReflection.InvokeStatic(Type, "RecordFired");
        public static void RecordAcceptedHit() => EnergyHitTestReflection.InvokeStatic(Type, "RecordAcceptedHit");
        public static void RecordRosFireRequest(bool accepted) =>
            EnergyHitTestReflection.InvokeStatic(Type, "RecordRosFireRequest", accepted);
        public static void RecordTargetRejection(string targetName, string reason) =>
            EnergyHitTestReflection.InvokeStatic(Type, "RecordTargetRejection", targetName, reason);
        public static void RecordProjectileImpact(string colliderName, float flightSeconds, bool hitOwnRig, bool accepted) =>
            EnergyHitTestReflection.InvokeStatic(Type, "RecordProjectileImpact", colliderName, flightSeconds, hitOwnRig, accepted);
    }
}
