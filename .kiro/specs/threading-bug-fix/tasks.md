# Implementation Plan

- [x] 1. Create thread-safe progress collector utility class


  - Create `ThreadSafeProgressCollector` class in `ClutterFlock.Tests/TestUtilities/` directory
  - Implement thread-safe `AddProgress` method using lock synchronization
  - Implement `GetSnapshot` method that returns a safe copy of collected progress updates
  - Add `Count` property with proper locking for thread-safe access
  - Write unit tests to verify concurrent add/read operations work correctly
  - _Requirements: 2.1, 2.2_



- [ ] 2. Fix the failing integration test using thread-safe collection
  - Modify `ProgressReporting_Integration_FlowsCorrectlyThroughComponents` test in `ComponentIntegrationTests.cs`
  - Replace `List<AnalysisProgress> progressUpdates` with `ThreadSafeProgressCollector`
  - Update progress callback to use `collector.AddProgress(p)` instead of direct list addition
  - Replace direct collection access with `collector.GetSnapshot()` before LINQ operations


  - Ensure all collection enumeration uses the snapshot to prevent concurrent modification
  - _Requirements: 1.1, 1.2, 3.1_

- [ ] 3. Validate the fix and run comprehensive tests
  - Run the specific failing test multiple times to ensure consistent success
  - Execute the complete integration test suite to verify no regressions



  - Run tests with coverage to ensure the fix doesn't impact coverage metrics
  - Verify the test still validates progress reporting behavior correctly
  - Test with different levels of concurrent progress updates to ensure robustness
  - _Requirements: 1.3, 3.2, 3.3_

- [ ] 4. Review and update other integration tests for similar issues
  - Scan all integration test files for similar progress collection patterns
  - Identify any other tests that access collections modified by progress callbacks
  - Update any other tests found to use the thread-safe progress collector
  - Ensure consistent patterns across all integration tests for progress reporting
  - Document the thread-safe progress collection pattern for future test development
  - _Requirements: 2.1, 2.3_