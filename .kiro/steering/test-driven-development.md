# Testing policy

Use integration and component tests with coverage. Do not add isolated unit tests,
reflection tests of implementation details, or mocked service implementations.
Exercise public interfaces with real temporary files and verify observable results.
Use an STA thread for WPF window integration scenarios. Await operations directly;
do not use arbitrary sleeps to guess when work finished.

Run the VS Code `test with coverage` task, whose command is defined in
`.vscode/tasks.json`, or `pwsh -NoProfile -File scripts/test-integration.ps1`.
The shared script builds the Release solution, runs integration tests, publishes
TRX and Cobertura reports, and checks 75% line / 60% branch coverage.
Use `-NoBuild` only after building the current sources in Release.

Before changes, record the baseline. After changes, run the suite and investigate
failures before modifying expectations. Preserve behavioral regression scenarios.
