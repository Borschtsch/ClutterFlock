using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClutterFlock.Models;
using ClutterFlock.Tests.TestUtilities;

namespace ClutterFlock.Tests.Unit.TestUtilities
{
    [TestClass]
    [TestCategory("Unit")]
    [TestCategory("TestUtilities")]
    public class ThreadSafeProgressCollectorTests
    {
        [TestMethod]
        public void AddProgress_SingleThread_AddsProgressCorrectly()
        {
            // Arrange
            var collector = new ThreadSafeProgressCollector();
            var progress = new AnalysisProgress
            {
                CurrentProgress = 10,
                MaxProgress = 100,
                StatusMessage = "Test progress",
                Phase = AnalysisPhase.ScanningFolders
            };

            // Act
            collector.AddProgress(progress);

            // Assert
            Assert.AreEqual(1, collector.Count);
            var snapshot = collector.GetSnapshot();
            Assert.AreEqual(1, snapshot.Count);
            Assert.AreEqual(10, snapshot[0].CurrentProgress);
            Assert.AreEqual(100, snapshot[0].MaxProgress);
            Assert.AreEqual("Test progress", snapshot[0].StatusMessage);
            Assert.AreEqual(AnalysisPhase.ScanningFolders, snapshot[0].Phase);
        }

        [TestMethod]
        public void AddProgress_MultipleUpdates_AddsAllCorrectly()
        {
            // Arrange
            var collector = new ThreadSafeProgressCollector();
            var updates = new[]
            {
                new AnalysisProgress { CurrentProgress = 10, MaxProgress = 100, Phase = AnalysisPhase.ScanningFolders },
                new AnalysisProgress { CurrentProgress = 20, MaxProgress = 100, Phase = AnalysisPhase.ComparingFiles },
                new AnalysisProgress { CurrentProgress = 30, MaxProgress = 100, Phase = AnalysisPhase.AggregatingResults }
            };

            // Act
            foreach (var update in updates)
            {
                collector.AddProgress(update);
            }

            // Assert
            Assert.AreEqual(3, collector.Count);
            var snapshot = collector.GetSnapshot();
            Assert.AreEqual(3, snapshot.Count);
            
            for (int i = 0; i < updates.Length; i++)
            {
                Assert.AreEqual(updates[i].CurrentProgress, snapshot[i].CurrentProgress);
                Assert.AreEqual(updates[i].Phase, snapshot[i].Phase);
            }
        }

        [TestMethod]
        public async Task AddProgress_ConcurrentAccess_HandlesThreadSafely()
        {
            // Arrange
            var collector = new ThreadSafeProgressCollector();
            const int threadCount = 10;
            const int updatesPerThread = 100;
            var tasks = new List<Task>();

            // Act - Multiple threads adding progress concurrently
            for (int t = 0; t < threadCount; t++)
            {
                int threadId = t;
                tasks.Add(Task.Run(() =>
                {
                    for (int i = 0; i < updatesPerThread; i++)
                    {
                        var progress = new AnalysisProgress
                        {
                            CurrentProgress = i,
                            MaxProgress = updatesPerThread,
                            StatusMessage = $"Thread {threadId} - Update {i}",
                            Phase = (AnalysisPhase)(i % 6) // Cycle through phases
                        };
                        collector.AddProgress(progress);
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // Assert
            Assert.AreEqual(threadCount * updatesPerThread, collector.Count);
            var snapshot = collector.GetSnapshot();
            Assert.AreEqual(threadCount * updatesPerThread, snapshot.Count);

            // Verify all updates were captured
            var threadMessages = snapshot.Select(p => p.StatusMessage).ToList();
            for (int t = 0; t < threadCount; t++)
            {
                for (int i = 0; i < updatesPerThread; i++)
                {
                    var expectedMessage = $"Thread {t} - Update {i}";
                    Assert.IsTrue(threadMessages.Contains(expectedMessage), 
                        $"Missing expected message: {expectedMessage}");
                }
            }
        }

        [TestMethod]
        public async Task GetSnapshot_ConcurrentAccess_ReturnsConsistentData()
        {
            // Arrange
            var collector = new ThreadSafeProgressCollector();
            var addTask = Task.Run(async () =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    collector.AddProgress(new AnalysisProgress
                    {
                        CurrentProgress = i,
                        MaxProgress = 1000,
                        StatusMessage = $"Update {i}",
                        Phase = AnalysisPhase.ScanningFolders
                    });
                    
                    if (i % 10 == 0)
                        await Task.Yield(); // Allow other threads to run
                }
            });

            var snapshotTasks = new List<Task<List<AnalysisProgress>>>();
            
            // Act - Take snapshots while updates are being added
            for (int i = 0; i < 10; i++)
            {
                snapshotTasks.Add(Task.Run(async () =>
                {
                    await Task.Delay(10); // Stagger snapshot requests
                    return collector.GetSnapshot();
                }));
            }

            await Task.WhenAll(addTask);
            var snapshots = await Task.WhenAll(snapshotTasks);

            // Assert - All snapshots should be valid (no exceptions thrown)
            foreach (var snapshot in snapshots)
            {
                Assert.IsNotNull(snapshot);
                Assert.IsTrue(snapshot.Count >= 0);
                
                // Verify snapshot integrity - all items should have valid data
                foreach (var item in snapshot)
                {
                    Assert.IsNotNull(item.StatusMessage);
                    Assert.IsTrue(item.CurrentProgress >= 0);
                    Assert.IsTrue(item.MaxProgress > 0);
                }
            }
        }

        [TestMethod]
        public void GetSnapshot_EmptyCollector_ReturnsEmptyList()
        {
            // Arrange
            var collector = new ThreadSafeProgressCollector();

            // Act
            var snapshot = collector.GetSnapshot();

            // Assert
            Assert.IsNotNull(snapshot);
            Assert.AreEqual(0, snapshot.Count);
            Assert.AreEqual(0, collector.Count);
        }

        [TestMethod]
        public void Clear_WithData_RemovesAllItems()
        {
            // Arrange
            var collector = new ThreadSafeProgressCollector();
            collector.AddProgress(new AnalysisProgress { CurrentProgress = 10, MaxProgress = 100 });
            collector.AddProgress(new AnalysisProgress { CurrentProgress = 20, MaxProgress = 100 });
            
            Assert.AreEqual(2, collector.Count);

            // Act
            collector.Clear();

            // Assert
            Assert.AreEqual(0, collector.Count);
            var snapshot = collector.GetSnapshot();
            Assert.AreEqual(0, snapshot.Count);
        }

        [TestMethod]
        public void CreateProgress_ReturnsValidProgressInstance()
        {
            // Arrange
            var collector = new ThreadSafeProgressCollector();

            // Act
            var progress = collector.CreateProgress();

            // Assert
            Assert.IsNotNull(progress);
            Assert.IsInstanceOfType(progress, typeof(Progress<AnalysisProgress>));
        }

        [TestMethod]
        public void GetSnapshot_IndependentCopies_ModificationDoesNotAffectOriginal()
        {
            // Arrange
            var collector = new ThreadSafeProgressCollector();
            collector.AddProgress(new AnalysisProgress { CurrentProgress = 10, MaxProgress = 100 });

            // Act
            var snapshot1 = collector.GetSnapshot();
            var snapshot2 = collector.GetSnapshot();
            
            // Modify one snapshot
            snapshot1.Add(new AnalysisProgress { CurrentProgress = 20, MaxProgress = 100 });

            // Assert
            Assert.AreEqual(2, snapshot1.Count);
            Assert.AreEqual(1, snapshot2.Count);
            Assert.AreEqual(1, collector.Count);
        }
    }
}