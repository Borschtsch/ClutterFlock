using ClutterFlock.Models;

namespace ClutterFlock.Tests.TestUtilities
{
    /// <summary>
    /// Thread-safe collector for AnalysisProgress updates during testing.
    /// Prevents InvalidOperationException when accessing progress collections
    /// that are being modified by concurrent progress reporting callbacks.
    /// 
    /// Usage Pattern for Integration Tests:
    /// 1. Create collector: var progressCollector = new ThreadSafeProgressCollector();
    /// 2. Create progress: var progress = progressCollector.CreateProgress();
    /// 3. Pass progress to async operations that report progress
    /// 4. Use GetSnapshot() to safely access collected progress for assertions
    /// 
    /// This pattern prevents "Collection was modified; enumeration operation may not execute"
    /// exceptions that occur when LINQ operations are performed on collections being
    /// modified by concurrent progress reporting callbacks.
    /// </summary>
    public class ThreadSafeProgressCollector
    {
        private readonly List<AnalysisProgress> _progressUpdates = new();
        private readonly object _lock = new();

        /// <summary>
        /// Adds a progress update in a thread-safe manner
        /// </summary>
        /// <param name="progress">The progress update to add</param>
        public void AddProgress(AnalysisProgress progress)
        {
            lock (_lock)
            {
                _progressUpdates.Add(progress);
            }
        }

        /// <summary>
        /// Gets a thread-safe snapshot of all collected progress updates
        /// </summary>
        /// <returns>A new list containing all progress updates</returns>
        public List<AnalysisProgress> GetSnapshot()
        {
            lock (_lock)
            {
                return new List<AnalysisProgress>(_progressUpdates);
            }
        }

        /// <summary>
        /// Gets the current count of progress updates in a thread-safe manner
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _progressUpdates.Count;
                }
            }
        }

        /// <summary>
        /// Clears all collected progress updates in a thread-safe manner
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _progressUpdates.Clear();
            }
        }

        /// <summary>
        /// Creates a Progress&lt;AnalysisProgress&gt; that reports to this collector
        /// </summary>
        /// <returns>A Progress instance that adds updates to this collector</returns>
        public Progress<AnalysisProgress> CreateProgress()
        {
            return new Progress<AnalysisProgress>(AddProgress);
        }
    }
}