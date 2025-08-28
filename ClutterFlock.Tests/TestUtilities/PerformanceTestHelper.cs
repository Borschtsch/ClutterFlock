using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ClutterFlock.Models;

namespace ClutterFlock.Tests.TestUtilities
{
    /// <summary>
    /// Utility for monitoring performance metrics during tests
    /// </summary>
    public class PerformanceTestHelper : IDisposable
    {
        private readonly List<PerformanceMetric> _metrics = new();
        private readonly List<ProgressUpdate> _progressUpdates = new();
        private readonly PerformanceCounter? _memoryCounter;
        private readonly PerformanceCounter? _cpuCounter;
        private long _initialMemory;
        private long _peakMemory;
        private Stopwatch? _currentStopwatch;

        public PerformanceTestHelper()
        {
            try
            {
                _memoryCounter = new PerformanceCounter("Process", "Working Set", Process.GetCurrentProcess().ProcessName);
                _cpuCounter = new PerformanceCounter("Process", "% Processor Time", Process.GetCurrentProcess().ProcessName);
            }
            catch
            {
                // Performance counters may not be available in all environments
            }
        }

        /// <summary>
        /// Starts monitoring performance for an operation
        /// </summary>
        public PerformanceMonitor StartMonitoring(string operationName)
        {
            _initialMemory = GC.GetTotalMemory(false);
            _peakMemory = _initialMemory;
            
            return new PerformanceMonitor(this, operationName);
        }

        /// <summary>
        /// Starts simple monitoring without returning a monitor object
        /// </summary>
        public void StartMonitoring()
        {
            _initialMemory = GC.GetTotalMemory(false);
            _peakMemory = _initialMemory;
            _currentStopwatch = Stopwatch.StartNew();
        }

        /// <summary>
        /// Gets current performance metrics
        /// </summary>
        public PerformanceMetric GetCurrentMetrics()
        {
            var currentMemory = GC.GetTotalMemory(false);
            var executionTime = _currentStopwatch?.Elapsed ?? TimeSpan.Zero;
            
            return new PerformanceMetric
            {
                OperationName = "Current",
                ExecutionTime = executionTime,
                MemoryUsageBytes = currentMemory,
                MemoryIncrease = currentMemory - _initialMemory,
                ItemsProcessed = 0,
                ThroughputPerSecond = 0,
                Timestamp = DateTime.Now
            };
        }

        /// <summary>
        /// Records a performance metric
        /// </summary>
        public void RecordMetric(PerformanceMetric metric)
        {
            _metrics.Add(metric);
            
            // Update peak memory if this metric includes memory usage
            if (metric.MemoryUsageBytes > _peakMemory)
            {
                _peakMemory = metric.MemoryUsageBytes;
            }
        }

        /// <summary>
        /// Records a progress update
        /// </summary>
        public void RecordProgress(AnalysisProgress progress)
        {
            _progressUpdates.Add(new ProgressUpdate
            {
                Timestamp = DateTime.Now,
                Progress = progress.CurrentProgress,
                MaxProgress = progress.MaxProgress,
                Message = progress.StatusMessage,
                Phase = progress.Phase
            });
        }

        /// <summary>
        /// Gets current memory usage in bytes
        /// </summary>
        public long GetCurrentMemoryUsage()
        {
            return GC.GetTotalMemory(false);
        }

        /// <summary>
        /// Gets peak memory usage since monitoring started
        /// </summary>
        public long GetPeakMemoryUsage()
        {
            return Math.Max(_peakMemory, GetCurrentMemoryUsage());
        }

        /// <summary>
        /// Gets memory usage increase since monitoring started
        /// </summary>
        public long GetMemoryIncrease()
        {
            return GetCurrentMemoryUsage() - _initialMemory;
        }

        /// <summary>
        /// Gets all recorded metrics
        /// </summary>
        public List<PerformanceMetric> GetMetrics() => new(_metrics);

        /// <summary>
        /// Gets all recorded progress updates
        /// </summary>
        public List<ProgressUpdate> GetProgressUpdates() => new(_progressUpdates);

        /// <summary>
        /// Validates that performance requirements are met
        /// </summary>
        public PerformanceValidationResult ValidatePerformance(PerformanceRequirements requirements)
        {
            var result = new PerformanceValidationResult();
            
            foreach (var metric in _metrics)
            {
                // Check execution time
                if (requirements.MaxExecutionTime.HasValue && metric.ExecutionTime > requirements.MaxExecutionTime.Value)
                {
                    result.Failures.Add($"Execution time {metric.ExecutionTime} exceeds maximum {requirements.MaxExecutionTime.Value}");
                }
                
                // Check memory usage
                if (requirements.MaxMemoryUsageBytes.HasValue && metric.MemoryUsageBytes > requirements.MaxMemoryUsageBytes.Value)
                {
                    result.Failures.Add($"Memory usage {metric.MemoryUsageBytes:N0} bytes exceeds maximum {requirements.MaxMemoryUsageBytes.Value:N0} bytes");
                }
                
                // Check throughput
                if (requirements.MinThroughputPerSecond.HasValue && metric.ThroughputPerSecond < requirements.MinThroughputPerSecond.Value)
                {
                    result.Failures.Add($"Throughput {metric.ThroughputPerSecond:F2}/sec is below minimum {requirements.MinThroughputPerSecond.Value:F2}/sec");
                }
            }
            
            // Check UI responsiveness
            if (requirements.MaxUIUpdateInterval.HasValue)
            {
                var maxInterval = GetMaxProgressUpdateInterval();
                if (maxInterval > requirements.MaxUIUpdateInterval.Value)
                {
                    result.Failures.Add($"UI update interval {maxInterval} exceeds maximum {requirements.MaxUIUpdateInterval.Value}");
                }
            }
            
            result.IsValid = result.Failures.Count == 0;
            return result;
        }

        /// <summary>
        /// Clears all recorded metrics and progress updates
        /// </summary>
        public void Clear()
        {
            _metrics.Clear();
            _progressUpdates.Clear();
            _initialMemory = GC.GetTotalMemory(false);
            _peakMemory = _initialMemory;
        }

        /// <summary>
        /// Forces garbage collection and returns memory usage
        /// </summary>
        public long ForceGCAndGetMemory()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            return GC.GetTotalMemory(false);
        }

        private TimeSpan GetMaxProgressUpdateInterval()
        {
            if (_progressUpdates.Count < 2) return TimeSpan.Zero;
            
            var maxInterval = TimeSpan.Zero;
            for (int i = 1; i < _progressUpdates.Count; i++)
            {
                var interval = _progressUpdates[i].Timestamp - _progressUpdates[i - 1].Timestamp;
                if (interval > maxInterval)
                    maxInterval = interval;
            }
            
            return maxInterval;
        }

        public void Dispose()
        {
            _memoryCounter?.Dispose();
            _cpuCounter?.Dispose();
        }
    }

    /// <summary>
    /// Performance monitoring session
    /// </summary>
    public class PerformanceMonitor : IDisposable
    {
        private readonly PerformanceTestHelper _helper;
        private readonly string _operationName;
        private readonly Stopwatch _stopwatch;
        private readonly long _startMemory;
        private int _itemsProcessed;

        internal PerformanceMonitor(PerformanceTestHelper helper, string operationName)
        {
            _helper = helper;
            _operationName = operationName;
            _stopwatch = Stopwatch.StartNew();
            _startMemory = GC.GetTotalMemory(false);
        }

        /// <summary>
        /// Records that an item was processed
        /// </summary>
        public void RecordItemProcessed()
        {
            Interlocked.Increment(ref _itemsProcessed);
        }

        /// <summary>
        /// Records multiple items processed
        /// </summary>
        public void RecordItemsProcessed(int count)
        {
            Interlocked.Add(ref _itemsProcessed, count);
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            var endMemory = GC.GetTotalMemory(false);
            
            var metric = new PerformanceMetric
            {
                OperationName = _operationName,
                ExecutionTime = _stopwatch.Elapsed,
                MemoryUsageBytes = endMemory,
                MemoryIncrease = endMemory - _startMemory,
                ItemsProcessed = _itemsProcessed,
                ThroughputPerSecond = _itemsProcessed / Math.Max(_stopwatch.Elapsed.TotalSeconds, 0.001),
                Timestamp = DateTime.Now
            };
            
            _helper.RecordMetric(metric);
        }
    }

    /// <summary>
    /// Represents a performance metric
    /// </summary>
    public class PerformanceMetric
    {
        public string OperationName { get; set; } = string.Empty;
        public TimeSpan ExecutionTime { get; set; }
        public long MemoryUsageBytes { get; set; }
        public long MemoryIncrease { get; set; }
        public int ItemsProcessed { get; set; }
        public double ThroughputPerSecond { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Represents a progress update for performance monitoring
    /// </summary>
    public class ProgressUpdate
    {
        public DateTime Timestamp { get; set; }
        public int Progress { get; set; }
        public int MaxProgress { get; set; }
        public string Message { get; set; } = string.Empty;
        public AnalysisPhase Phase { get; set; }
    }

    /// <summary>
    /// Performance requirements for validation
    /// </summary>
    public class PerformanceRequirements
    {
        public TimeSpan? MaxExecutionTime { get; set; }
        public long? MaxMemoryUsageBytes { get; set; }
        public double? MinThroughputPerSecond { get; set; }
        public TimeSpan? MaxUIUpdateInterval { get; set; }
    }

    /// <summary>
    /// Result of performance validation
    /// </summary>
    public class PerformanceValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Failures { get; set; } = new();
    }
}