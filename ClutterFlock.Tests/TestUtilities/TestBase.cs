using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ClutterFlock.Tests.TestUtilities;

namespace ClutterFlock.Tests.TestUtilities
{
    /// <summary>
    /// Base class for all tests providing common setup and utilities
    /// </summary>
    [TestClass]
    public abstract class TestBase
    {
        protected TestDataGenerator TestDataGenerator { get; private set; } = null!;
        protected MockFileSystem MockFileSystem { get; private set; } = null!;
        protected string? TempDirectory { get; private set; }

        [TestInitialize]
        public virtual void TestInitialize()
        {
            TestDataGenerator = new TestDataGenerator();
            MockFileSystem = new MockFileSystem();
        }

        [TestCleanup]
        public virtual void TestCleanup()
        {
            MockFileSystem?.Clear();
            
            if (!string.IsNullOrEmpty(TempDirectory))
            {
                TestDataGenerator.CleanupTempDirectory(TempDirectory);
                TempDirectory = null;
            }
        }

        /// <summary>
        /// Creates a temporary directory for integration tests
        /// </summary>
        protected string CreateTempDirectory()
        {
            TempDirectory = Path.Combine(Path.GetTempPath(), "ClutterFlockTest_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(TempDirectory);
            return TempDirectory;
        }

        /// <summary>
        /// Asserts that two values are approximately equal (for floating point comparisons)
        /// </summary>
        protected static void AssertApproximatelyEqual(double expected, double actual, double tolerance = 0.01, string? message = null)
        {
            var difference = Math.Abs(expected - actual);
            Assert.IsTrue(difference <= tolerance, 
                message ?? $"Expected {expected}, but was {actual}. Difference {difference} exceeds tolerance {tolerance}");
        }

        /// <summary>
        /// Asserts that an operation completes within the specified time
        /// </summary>
        protected static void AssertCompletesWithinTime(Action action, TimeSpan maxDuration, string? message = null)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            action();
            stopwatch.Stop();
            
            Assert.IsTrue(stopwatch.Elapsed <= maxDuration,
                message ?? $"Operation took {stopwatch.Elapsed} but should complete within {maxDuration}");
        }

        /// <summary>
        /// Asserts that an async operation completes within the specified time
        /// </summary>
        protected static async Task AssertCompletesWithinTimeAsync(Func<Task> asyncAction, TimeSpan maxDuration, string? message = null)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await asyncAction();
            stopwatch.Stop();
            
            Assert.IsTrue(stopwatch.Elapsed <= maxDuration,
                message ?? $"Operation took {stopwatch.Elapsed} but should complete within {maxDuration}");
        }
    }

    /// <summary>
    /// Test categories for organizing and filtering tests
    /// </summary>
    public static class TestCategories
    {
        public const string Unit = "Unit";
        public const string Integration = "Integration";
        public const string Performance = "Performance";
        public const string LongRunning = "LongRunning";
    }
}