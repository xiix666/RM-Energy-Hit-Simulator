using NUnit.Framework;

namespace Simulation.EnergyHit.Tests
{
    /// <summary>
    /// Covers <see cref="EnergyHitStatistics"/>, the static counter hub that feeds both
    /// the on-screen HUD drawn by <c>EnergyHitDebuggerWindow.OnGUI</c> and the
    /// "Energy Hit/Statistics" debugger page drawn by
    /// <c>EnergyHitStatisticsDebuggerPage.OnDraw</c>.
    /// </summary>
    [TestFixture]
    public sealed class EnergyHitStatisticsTests
    {
        [SetUp]
        public void SetUp()
        {
            EnergyHitStatistics.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            EnergyHitStatistics.Reset();
        }

        // ---------- Reset / initial state ----------

        [Test]
        public void Reset_AfterDirtyActivity_ZeroesEveryCounterAndLabel()
        {
            EnergyHitStatistics.RecordFired();
            EnergyHitStatistics.RecordAcceptedHit();
            EnergyHitStatistics.RecordRosFireRequest(false);
            EnergyHitStatistics.RecordProjectileImpact("Armor", 0.01f, true, true);
            EnergyHitStatistics.RecordTargetRejection("Target", "not lit");

            EnergyHitStatistics.Reset();

            Assert.AreEqual(0, EnergyHitStatistics.FiredCount, "FiredCount");
            Assert.AreEqual(0, EnergyHitStatistics.AcceptedHitCount, "AcceptedHitCount");
            Assert.AreEqual(0, EnergyHitStatistics.RosFireRequestCount, "RosFireRequestCount");
            Assert.AreEqual(0, EnergyHitStatistics.RosFireRejectedCount, "RosFireRejectedCount");
            Assert.AreEqual(0, EnergyHitStatistics.EarlyCollisionCount, "EarlyCollisionCount");
            Assert.AreEqual(0, EnergyHitStatistics.RigCollisionCount, "RigCollisionCount");
            Assert.AreEqual(0, EnergyHitStatistics.RejectedTargetCount, "RejectedTargetCount");
            Assert.AreEqual("none", EnergyHitStatistics.LastImpact, "LastImpact");
            Assert.AreEqual("none", EnergyHitStatistics.LastEarlyImpact, "LastEarlyImpact");
            Assert.AreEqual("none", EnergyHitStatistics.LastTargetRejection, "LastTargetRejection");
        }

        [Test]
        public void Reset_IsIdempotent()
        {
            EnergyHitStatistics.Reset();
            EnergyHitStatistics.Reset();

            Assert.AreEqual(0, EnergyHitStatistics.FiredCount);
            Assert.AreEqual(0f, EnergyHitStatistics.HitRate);
        }

        // ---------- Fired / accepted hit counters ----------

        [Test]
        public void RecordFired_IncrementsOnlyFiredCount()
        {
            EnergyHitStatistics.RecordFired();

            Assert.AreEqual(1, EnergyHitStatistics.FiredCount);
            Assert.AreEqual(0, EnergyHitStatistics.AcceptedHitCount);
        }

        [Test]
        public void RecordAcceptedHit_IncrementsOnlyAcceptedHitCount()
        {
            EnergyHitStatistics.RecordAcceptedHit();

            Assert.AreEqual(1, EnergyHitStatistics.AcceptedHitCount);
            Assert.AreEqual(0, EnergyHitStatistics.FiredCount);
        }

        [Test]
        public void RecordFired_CalledRepeatedly_Accumulates()
        {
            for (int i = 0; i < 1000; i++)
            {
                EnergyHitStatistics.RecordFired();
            }

            Assert.AreEqual(1000, EnergyHitStatistics.FiredCount);
        }

        // ---------- HitRate ----------

        [Test]
        public void HitRate_WhenNothingFired_IsZero()
        {
            Assert.AreEqual(0f, EnergyHitStatistics.HitRate);
        }

        [Test]
        public void HitRate_WhenNothingFiredButHitRecorded_IsZero()
        {
            // Guards the divide-by-zero path: the FiredCount > 0 check must win.
            EnergyHitStatistics.RecordAcceptedHit();

            Assert.AreEqual(0f, EnergyHitStatistics.HitRate);
        }

        [Test]
        public void HitRate_ComputesAcceptedOverFired()
        {
            for (int i = 0; i < 4; i++)
            {
                EnergyHitStatistics.RecordFired();
            }

            for (int i = 0; i < 3; i++)
            {
                EnergyHitStatistics.RecordAcceptedHit();
            }

            Assert.AreEqual(0.75f, EnergyHitStatistics.HitRate, 1e-6f);
        }

        [Test]
        public void HitRate_WhenEveryShotHits_IsOne()
        {
            for (int i = 0; i < 5; i++)
            {
                EnergyHitStatistics.RecordFired();
                EnergyHitStatistics.RecordAcceptedHit();
            }

            Assert.AreEqual(1f, EnergyHitStatistics.HitRate, 1e-6f);
        }

        [Test]
        public void HitRate_WhenNothingHit_IsZero()
        {
            EnergyHitStatistics.RecordFired();
            EnergyHitStatistics.RecordFired();

            Assert.AreEqual(0f, EnergyHitStatistics.HitRate);
        }

        [Test]
        public void HitRate_IsClampedByNeitherBound_WhenMoreHitsThanShots()
        {
            // Degenerate but tolerated: verifies no hidden clamping in the property.
            EnergyHitStatistics.RecordFired();
            EnergyHitStatistics.RecordAcceptedHit();
            EnergyHitStatistics.RecordAcceptedHit();

            Assert.AreEqual(2f, EnergyHitStatistics.HitRate, 1e-6f);
        }

        // ---------- ROS fire requests ----------

        [Test]
        public void RecordRosFireRequest_Accepted_IncrementsRequestOnly()
        {
            EnergyHitStatistics.RecordRosFireRequest(true);

            Assert.AreEqual(1, EnergyHitStatistics.RosFireRequestCount);
            Assert.AreEqual(0, EnergyHitStatistics.RosFireRejectedCount);
        }

        [Test]
        public void RecordRosFireRequest_Rejected_IncrementsRequestAndRejection()
        {
            EnergyHitStatistics.RecordRosFireRequest(false);

            Assert.AreEqual(1, EnergyHitStatistics.RosFireRequestCount);
            Assert.AreEqual(1, EnergyHitStatistics.RosFireRejectedCount);
        }

        [Test]
        public void RecordRosFireRequest_MixedSequence_TracksBothTotals()
        {
            EnergyHitStatistics.RecordRosFireRequest(true);
            EnergyHitStatistics.RecordRosFireRequest(false);
            EnergyHitStatistics.RecordRosFireRequest(false);
            EnergyHitStatistics.RecordRosFireRequest(true);

            Assert.AreEqual(4, EnergyHitStatistics.RosFireRequestCount);
            Assert.AreEqual(2, EnergyHitStatistics.RosFireRejectedCount);
        }

        // ---------- Projectile impact ----------

        [Test]
        public void RecordProjectileImpact_Accepted_FormatsLastImpactAsHit()
        {
            EnergyHitStatistics.RecordProjectileImpact("Armor", 0.25f, false, true);

            Assert.AreEqual("HIT Armor (250ms)", EnergyHitStatistics.LastImpact);
        }

        [Test]
        public void RecordProjectileImpact_Rejected_FormatsLastImpactAsBlocked()
        {
            EnergyHitStatistics.RecordProjectileImpact("Wall", 0.25f, false, false);

            Assert.AreEqual("BLOCKED Wall (250ms)", EnergyHitStatistics.LastImpact);
        }

        [Test]
        public void RecordProjectileImpact_SlowFlight_DoesNotCountAsEarly()
        {
            EnergyHitStatistics.RecordProjectileImpact("Armor", 0.25f, false, true);

            Assert.AreEqual(0, EnergyHitStatistics.EarlyCollisionCount);
            Assert.AreEqual("none", EnergyHitStatistics.LastEarlyImpact);
        }

        [Test]
        public void RecordProjectileImpact_FlightBelowThreshold_CountsAsEarly()
        {
            EnergyHitStatistics.RecordProjectileImpact("MuzzleShroud", 0.05f, false, false);

            Assert.AreEqual(1, EnergyHitStatistics.EarlyCollisionCount);
            Assert.AreEqual("BLOCKED MuzzleShroud (50ms)", EnergyHitStatistics.LastEarlyImpact);
        }

        [Test]
        public void RecordProjectileImpact_FlightExactlyAtThreshold_IsNotEarly()
        {
            // The source uses a strict "< 0.08f" comparison.
            EnergyHitStatistics.RecordProjectileImpact("Armor", 0.08f, false, true);

            Assert.AreEqual(0, EnergyHitStatistics.EarlyCollisionCount);
            Assert.AreEqual("none", EnergyHitStatistics.LastEarlyImpact);
        }

        [Test]
        public void RecordProjectileImpact_FlightJustBelowThreshold_IsEarly()
        {
            EnergyHitStatistics.RecordProjectileImpact("Armor", 0.07f, false, true);

            Assert.AreEqual(1, EnergyHitStatistics.EarlyCollisionCount);
            Assert.AreEqual("HIT Armor (70ms)", EnergyHitStatistics.LastEarlyImpact);
        }

        [Test]
        public void RecordProjectileImpact_ZeroFlightTime_IsEarly()
        {
            EnergyHitStatistics.RecordProjectileImpact("InsideRig", 0f, false, false);

            Assert.AreEqual(1, EnergyHitStatistics.EarlyCollisionCount);
            Assert.AreEqual("BLOCKED InsideRig (0ms)", EnergyHitStatistics.LastEarlyImpact);
        }

        [Test]
        public void RecordProjectileImpact_NegativeFlightTime_IsEarly()
        {
            EnergyHitStatistics.RecordProjectileImpact("InsideRig", -0.5f, false, false);

            Assert.AreEqual(1, EnergyHitStatistics.EarlyCollisionCount);
            Assert.AreEqual("BLOCKED InsideRig (-500ms)", EnergyHitStatistics.LastEarlyImpact);
        }

        [Test]
        public void RecordProjectileImpact_RepeatedEarlyCollisions_Accumulate()
        {
            EnergyHitStatistics.RecordProjectileImpact("A", 0.01f, false, false);
            EnergyHitStatistics.RecordProjectileImpact("B", 0.02f, false, false);
            EnergyHitStatistics.RecordProjectileImpact("C", 0.03f, false, false);

            Assert.AreEqual(3, EnergyHitStatistics.EarlyCollisionCount);
            Assert.AreEqual("BLOCKED C (30ms)", EnergyHitStatistics.LastEarlyImpact);
        }

        [Test]
        public void RecordProjectileImpact_HitOwnRig_CountsRigCollision()
        {
            EnergyHitStatistics.RecordProjectileImpact("GimbalYaw", 0.02f, true, false);

            Assert.AreEqual(1, EnergyHitStatistics.RigCollisionCount);
        }

        [Test]
        public void RecordProjectileImpact_NotOwnRig_DoesNotCountRigCollision()
        {
            EnergyHitStatistics.RecordProjectileImpact("Armor", 0.02f, false, true);

            Assert.AreEqual(0, EnergyHitStatistics.RigCollisionCount);
        }

        [Test]
        public void RecordProjectileImpact_EarlyAndOwnRig_CountsBothIndependently()
        {
            EnergyHitStatistics.RecordProjectileImpact("GimbalYaw", 0.01f, true, false);

            Assert.AreEqual(1, EnergyHitStatistics.EarlyCollisionCount);
            Assert.AreEqual(1, EnergyHitStatistics.RigCollisionCount);
        }

        [Test]
        public void RecordProjectileImpact_MillisecondsAreRoundedDown()
        {
            EnergyHitStatistics.RecordProjectileImpact("Armor", 0.1234f, false, true);

            Assert.AreEqual("HIT Armor (123ms)", EnergyHitStatistics.LastImpact);
        }

        [Test]
        public void RecordProjectileImpact_MillisecondsAreRoundedUp()
        {
            EnergyHitStatistics.RecordProjectileImpact("Armor", 0.1236f, false, true);

            Assert.AreEqual("HIT Armor (124ms)", EnergyHitStatistics.LastImpact);
        }

        [Test]
        public void RecordProjectileImpact_NullColliderName_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => EnergyHitStatistics.RecordProjectileImpact(null, 0.25f, false, true));
            Assert.AreEqual("HIT  (250ms)", EnergyHitStatistics.LastImpact);
        }

        [Test]
        public void RecordProjectileImpact_EmptyColliderName_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => EnergyHitStatistics.RecordProjectileImpact(string.Empty, 0.25f, false, false));
            Assert.AreEqual("BLOCKED  (250ms)", EnergyHitStatistics.LastImpact);
        }

        // ---------- Target rejection ----------

        [Test]
        public void RecordTargetRejection_IncrementsAndFormats()
        {
            EnergyHitStatistics.RecordTargetRejection("Fan_03", "target not illuminated");

            Assert.AreEqual(1, EnergyHitStatistics.RejectedTargetCount);
            Assert.AreEqual("Fan_03: target not illuminated", EnergyHitStatistics.LastTargetRejection);
        }

        [Test]
        public void RecordTargetRejection_CalledRepeatedly_KeepsOnlyTheLatest()
        {
            EnergyHitStatistics.RecordTargetRejection("Fan_01", "first reason");
            EnergyHitStatistics.RecordTargetRejection("Fan_02", "second reason");

            Assert.AreEqual(2, EnergyHitStatistics.RejectedTargetCount);
            Assert.AreEqual("Fan_02: second reason", EnergyHitStatistics.LastTargetRejection);
        }

        [Test]
        public void RecordTargetRejection_DoesNotAffectHitCounters()
        {
            EnergyHitStatistics.RecordTargetRejection("Fan_01", "cooldown");

            Assert.AreEqual(0, EnergyHitStatistics.FiredCount);
            Assert.AreEqual(0, EnergyHitStatistics.AcceptedHitCount);
            Assert.AreEqual(0, EnergyHitStatistics.EarlyCollisionCount);
        }

        // ---------- Integrated HUD scenario ----------

        [Test]
        public void CombinedScenario_ProducesHudReadableTotals()
        {
            for (int i = 0; i < 10; i++)
            {
                EnergyHitStatistics.RecordFired();
            }

            for (int i = 0; i < 7; i++)
            {
                EnergyHitStatistics.RecordAcceptedHit();
            }

            EnergyHitStatistics.RecordProjectileImpact("EarlyPlate", 0.01f, true, false);
            EnergyHitStatistics.RecordTargetRejection("Fan_02", "not lit");

            Assert.AreEqual(10, EnergyHitStatistics.FiredCount);
            Assert.AreEqual(7, EnergyHitStatistics.AcceptedHitCount);
            Assert.AreEqual(0.7f, EnergyHitStatistics.HitRate, 1e-6f);
            Assert.AreEqual(1, EnergyHitStatistics.EarlyCollisionCount);
            Assert.AreEqual(1, EnergyHitStatistics.RigCollisionCount);
            Assert.AreEqual(1, EnergyHitStatistics.RejectedTargetCount);
        }
    }
}
