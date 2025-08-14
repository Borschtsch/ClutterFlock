# Test-Driven Development Workflow

This document provides comprehensive guidance for maintaining code quality through systematic testing practices in the ClutterFlock project. Follow these procedures to ensure all code changes are properly validated and test failures are handled appropriately.

## ⚠️ CRITICAL: Test Execution Requirements

**ALWAYS use the exact command from VS Code "test with coverage" task:**

- Read the command from `.vscode/tasks.json` under the "test with coverage" task
- Execute the exact command specified in the task's args array
- **NEVER use direct `dotnet test` commands without coverage**
- This ensures consistent coverage collection and reporting
- Coverage data is essential for maintaining code quality standards

**Example workflow:**
1. Read `.vscode/tasks.json` 
2. Find the "test with coverage" task
3. Execute the command from that task's configuration

## Pre-Change Testing Protocol

### Before Making Any Code Changes

1. **Run the complete test suite** to establish a baseline:
   **ALWAYS use VS Code Task: "test with coverage"** (Ctrl+Shift+P → "Tasks: Run Task")

   **NEVER use direct dotnet test commands - always use the VS Code task for consistent coverage collection.**

2. **Document baseline results** in your development notes:

   - Total tests run
   - Any pre-existing failures
   - Overall pass/fail status
   - Code coverage percentage (if available)

3. **Identify and categorize any existing failures**:

   - Test infrastructure issues
   - Known failing tests
   - Environmental dependencies

4. **Ensure clean build state**:
   ```cmd
   dotnet clean
   dotnet build
   ```

### Pre-Change Checklist

- [ ] All tests run successfully (or documented failures noted)
- [ ] Build completes without errors or warnings
- [ ] Development environment is stable
- [ ] Baseline metrics documented

## Post-Change Testing Protocol

### After Making Code Changes

1. **Build the solution first**:

   ```cmd
   dotnet build
   ```

   - If build fails, fix compilation errors before proceeding
   - Do not modify tests to make builds pass

2. **Run the complete test suite**:
   **ALWAYS use VS Code Task: "test with coverage"** (Ctrl+Shift+P → "Tasks: Run Task")

   **NEVER use direct dotnet test commands.**
   dotnet test

   ```

   ```

3. **Analyze test results systematically**:
   - Compare against baseline results
   - Identify new failures introduced by changes
   - Verify expected test behavior changes

### Post-Change Checklist

- [ ] Solution builds successfully
- [ ] All tests run (no test execution failures)
- [ ] New failures analyzed and categorized
- [ ] Test results compared against baseline

## Test Failure Handling Procedures

### When Tests Fail After Code Changes

**DO NOT immediately fix failing tests.** Instead, follow this analysis process:

1. **Categorize the failure type**:

   - **Code Issue**: Your changes broke existing functionality
   - **Test Issue**: Test needs updating due to legitimate behavior change
   - **Infrastructure Issue**: Test environment or setup problem

2. **For Code Issues** (most common):

   - Review your changes for unintended side effects
   - Check if you broke existing contracts or interfaces
   - Verify async/await patterns and thread safety
   - Ensure proper error handling is maintained

3. **For Test Issues** (requires careful consideration):

   - Document why the test behavior should change
   - Verify the new behavior meets requirements
   - Update test expectations only after confirming correctness
   - Consider if the change indicates a design problem

4. **For Infrastructure Issues**:
   - Check test dependencies and setup
   - Verify mock configurations
   - Ensure test isolation and cleanup

### Failure Reporting Process

When you encounter test failures:

1. **Document the failure details**:

   - Which tests failed
   - Error messages and stack traces
   - Your analysis of the root cause
   - Proposed resolution approach

2. **Report before fixing**:

   - Create a clear description of the issue
   - Include your analysis and proposed fix
   - Wait for confirmation before proceeding with test modifications

3. **Never silently fix tests** without understanding why they failed

## Build Error Handling

### When Tests Don't Build

1. **Check compilation errors first**:

   ```cmd
   dotnet build ClutterFlock.Tests
   ```

2. **Common build issues**:

   - Missing using statements
   - Interface changes not reflected in mocks
   - Async method signature changes
   - Namespace or assembly reference issues

3. **Troubleshooting steps**:

   - Verify all project references are correct
   - Check that mock implementations match current interfaces
   - Ensure test utilities are compatible with code changes
   - Validate NuGet package versions if applicable

4. **Escalation procedure**:
   - Document the specific build errors
   - Include relevant code snippets
   - Describe attempted resolution steps
   - Request assistance with complex build issues

## Adding Tests for New Features

### Test Coverage Requirements

When implementing new features, you MUST:

1. **Create corresponding unit tests** for all new public methods
2. **Achieve minimum 80% code coverage** for new code
3. **Include integration tests** for complete workflows
4. **Add performance tests** if the feature affects system performance

### Test Implementation Guidelines

1. **Follow the existing test structure**:

   ```
   ClutterFlock.Tests/
   ├── Unit/[Component]/[FeatureName]Tests.cs
   ├── Integration/[WorkflowName]Tests.cs
   └── Performance/[PerformanceName]Tests.cs
   ```

2. **Use appropriate test categories**:

   ```csharp
   [TestCategory("Unit")]
   [TestCategory("Integration")]
   [TestCategory("Performance")]
   ```

3. **Follow naming conventions**:

   - Test methods: `MethodName_Scenario_ExpectedResult`
   - Test classes: `[ComponentName]Tests`
   - Mock classes: `Mock[InterfaceName]`

4. **Include comprehensive test scenarios**:
   - Happy path functionality
   - Error conditions and edge cases
   - Boundary value testing
   - Cancellation and timeout scenarios

### New Feature Test Checklist

- [ ] Unit tests created for all public methods
- [ ] Integration tests cover complete workflows
- [ ] Performance tests added if applicable
- [ ] Error handling scenarios tested
- [ ] Async operations include cancellation tests
- [ ] Mock objects created for dependencies
- [ ] Test data and utilities updated as needed

## Refactoring Guidelines

### When Refactoring Existing Code

1. **Run tests before refactoring**:

   - Establish baseline test results
   - Ensure all tests pass before changes

2. **Refactor incrementally**:

   - Make small, focused changes
   - Run tests after each significant change
   - Maintain test coverage throughout

3. **Update tests appropriately**:

   - Tests should continue to pass after refactoring
   - Update test names if method names change
   - Modify test setup if constructor signatures change
   - Keep test intent and coverage the same

4. **Validate refactoring success**:
   - All existing tests still pass
   - Code coverage remains the same or improves
   - Performance characteristics are maintained

## Automation Integration

### Command Line Test Execution

Use these commands for different testing scenarios:

```cmd
# Run all tests
dotnet test

# Run only unit tests
dotnet test --filter TestCategory=Unit

# Run only integration tests
dotnet test --filter TestCategory=Integration

# Run with code coverage
dotnet test --collect:"XPlat Code Coverage"

# Run with detailed output
dotnet test --logger:console;verbosity=detailed
```

### Build Process Integration

### Optional Build-Time Testing

To integrate tests into your build process:

1. **Create a build script** (build-and-test.cmd):

   ```cmd
   @echo off
   echo Building solution...
   dotnet build
   if %ERRORLEVEL% neq 0 (
       echo Build failed!
       exit /b %ERRORLEVEL%
   )

   echo Running tests...
   dotnet test --no-build
   if %ERRORLEVEL% neq 0 (
       echo Tests failed!
       exit /b %ERRORLEVEL%
   )

   echo Build and tests completed successfully!
   ```

2. **Use the script for complete validation**:
   ```cmd
   build-and-test.cmd
   ```

### Continuous Integration Preparation

Structure your testing for CI/CD integration:

1. **Test categorization** enables selective execution
2. **Parallel execution** reduces build times
3. **Code coverage reporting** tracks quality metrics
4. **Test result exports** integrate with build systems

### Test Result Reporting

Generate comprehensive test reports:

```cmd
# Generate XML test results
dotnet test --logger:trx

# Generate HTML coverage report (requires ReportGenerator)
dotnet test --collect:"XPlat Code Coverage" --results-directory:./TestResults
```

## Quality Gates and Standards

### Before Committing Code

- [ ] All tests pass (or documented failures explained)
- [ ] Code coverage meets minimum requirements (80%)
- [ ] Build completes without warnings
- [ ] New features have corresponding tests
- [ ] Test failures have been properly analyzed and resolved

### Code Review Requirements

When reviewing code changes:

- [ ] Verify test coverage for new functionality
- [ ] Check that existing tests still pass
- [ ] Validate test quality and completeness
- [ ] Ensure proper error handling is tested
- [ ] Confirm async operations include cancellation tests

## Emergency Procedures

### When Tests Block Development

If tests are preventing critical work:

1. **Document the blocking issue** thoroughly
2. **Isolate the problematic tests** using test filters
3. **Continue development** with remaining test coverage
4. **Prioritize fixing** the test issues
5. **Never disable tests permanently** without proper analysis

### Recovery from Test Infrastructure Failures

If the entire test suite fails to run:

1. **Check basic build functionality** first
2. **Verify test project references** and dependencies
3. **Test with minimal test cases** to isolate issues
4. **Restore from known good state** if necessary
5. **Document and report** infrastructure problems

## Best Practices Summary

1. **Always run tests before and after code changes**
2. **Analyze test failures before fixing them**
3. **Report test issues rather than silently fixing them**
4. **Maintain comprehensive test coverage for new features**
5. **Use appropriate test categories and organization**
6. **Integrate testing into your development workflow**
7. **Document and communicate test-related issues**
8. **Never compromise on test quality for speed**

Following these guidelines ensures that ClutterFlock maintains high code quality and reliability through systematic test-driven development practices.
