using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ClutterFlock.Models;
using ClutterFlock.Services;

namespace ClutterFlock.Tests.TestUtilities
{
    /// <summary>
    /// Mock implementation of IErrorRecoveryService for testing error handling scenarios
    /// </summary>
    public class MockErrorRecoveryService : IErrorRecoveryService
    {
        private readonly List<ErrorEvent> _errorEvents = new();
        private readonly List<SkippedItem> _skippedItems = new();
        private readonly Dictionary<string, RecoveryAction> _configuredActions = new();
        
        // Test configuration
        public RecoveryAction DefaultAction { get; set; } = new() { Type = RecoveryActionType.Skip };
        public bool ThrowOnNextOperation { get; set; }
        public Exception? ExceptionToThrow { get; set; }

        public async Task<RecoveryAction> HandleFileAccessError(string filePath, Exception error)
        {
            RecordError("FileAccess", filePath, error);
            ThrowIfConfigured();
            
            await Task.Delay(1); // Simulate async processing
            
            var key = $"FileAccess:{filePath}";
            return _configuredActions.TryGetValue(key, out var action) ? action : DefaultAction;
        }

        public async Task<RecoveryAction> HandleNetworkError(string networkPath, Exception error)
        {
            RecordError("Network", networkPath, error);
            ThrowIfConfigured();
            
            await Task.Delay(1); // Simulate async processing
            
            var key = $"Network:{networkPath}";
            return _configuredActions.TryGetValue(key, out var action) ? action : DefaultAction;
        }

        public async Task<RecoveryAction> HandleResourceConstraintError(ResourceConstraintType type, Exception error)
        {
            RecordError("ResourceConstraint", type.ToString(), error);
            ThrowIfConfigured();
            
            await Task.Delay(1); // Simulate async processing
            
            var key = $"ResourceConstraint:{type}";
            return _configuredActions.TryGetValue(key, out var action) ? action : DefaultAction;
        }

        public void LogSkippedItem(string path, string reason)
        {
            _skippedItems.Add(new SkippedItem
            {
                Path = path,
                Reason = reason,
                Timestamp = DateTime.Now
            });
        }

        public ErrorSummary GetErrorSummary()
        {
            var summary = new ErrorSummary();
            
            foreach (var errorEvent in _errorEvents)
            {
                switch (errorEvent.Type)
                {
                    case "FileAccess":
                        summary.PermissionErrors++;
                        break;
                    case "Network":
                        summary.NetworkErrors++;
                        break;
                    case "ResourceConstraint":
                        summary.ResourceErrors++;
                        break;
                }
                
                summary.ErrorMessages.Add($"{errorEvent.Type}: {errorEvent.Message}");
            }
            
            summary.SkippedFiles = _skippedItems.Count;
            summary.SkippedPaths = _skippedItems.ConvertAll(item => item.Path);
            summary.LastErrorTime = _errorEvents.Count > 0 ? _errorEvents[^1].Timestamp : DateTime.MinValue;
            
            return summary;
        }

        public void ClearErrorSummary()
        {
            _errorEvents.Clear();
            _skippedItems.Clear();
        }

        // Test utility methods
        public void ConfigureAction(string errorType, string path, RecoveryAction action)
        {
            _configuredActions[$"{errorType}:{path}"] = action;
        }

        public void ConfigureFileAccessAction(string filePath, RecoveryAction action)
        {
            ConfigureAction("FileAccess", filePath, action);
        }

        public void ConfigureNetworkAction(string networkPath, RecoveryAction action)
        {
            ConfigureAction("Network", networkPath, action);
        }

        public void ConfigureResourceConstraintAction(ResourceConstraintType type, RecoveryAction action)
        {
            ConfigureAction("ResourceConstraint", type.ToString(), action);
        }

        public List<ErrorEvent> GetErrorEvents() => new(_errorEvents);
        public List<SkippedItem> GetSkippedItems() => new(_skippedItems);

        public bool HasErrorOfType(string errorType)
        {
            return _errorEvents.Exists(e => e.Type == errorType);
        }

        public int GetErrorCount(string errorType)
        {
            return _errorEvents.FindAll(e => e.Type == errorType).Count;
        }

        public void ClearConfiguration()
        {
            _configuredActions.Clear();
            DefaultAction = new RecoveryAction { Type = RecoveryActionType.Skip };
        }

        private void RecordError(string type, string path, Exception error)
        {
            _errorEvents.Add(new ErrorEvent
            {
                Type = type,
                Path = path,
                Message = error.Message,
                Exception = error,
                Timestamp = DateTime.Now
            });
        }

        private void ThrowIfConfigured()
        {
            if (ThrowOnNextOperation)
            {
                ThrowOnNextOperation = false;
                var exception = ExceptionToThrow ?? new InvalidOperationException("Mock exception");
                ExceptionToThrow = null;
                throw exception;
            }
        }
    }

    /// <summary>
    /// Represents an error event for testing
    /// </summary>
    public class ErrorEvent
    {
        public string Type { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public Exception? Exception { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Represents a skipped item for testing
    /// </summary>
    public class SkippedItem
    {
        public string Path { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
}