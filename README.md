# ClutterFlock

[![codecov](https://codecov.io/gh/Borschtsch/ClutterFlock/branch/main/graph/badge.svg)](https://codecov.io/gh/Borschtsch/ClutterFlock)
[![Build Status](https://github.com/Borschtsch/ClutterFlock/workflows/build-test-and-release/badge.svg)](https://github.com/Borschtsch/ClutterFlock/actions)

This tool can help you to optimize your decades old back-ups or find latest versions of the folders within your back-ups.
Imagine you took photos and never sorted them, but moved them around, replaced back-up drives and now you have multiples copies of the same photos from the same day in multiple places. How to find all the folders with the same data, not just individual files?

I ran into the problem of huge unsorted back-ups and no tool on the market that I could possibly find could help me with that, it either takes too long to scan the files or too much of my time to crawl through the files.

ClutterFlock supports filtering folders by the minimum size and minimum similarity level. Also tool can show content of the folder duplicates in a directory WinMerge style with only the files present in both directories or including the unique files.
See this simplified UI below. No settings, no smart features, just getting you straght to your data.

![Alt text](/Screenshot/Main.jpg?raw=true "Example analysis")

# Important note
This tool it not designed to manage your back-ups. This tool is not intended to find all file duplicates in your back-ups. 
Its focus is finding similar folders that could store the same files (using file name, file size and hash value).

No responsibility taken if you yourself delete the folder with the latest data because it was not shown by the tool.

# Also important note
This is a vibe-coding project that I started to work on to sort out my 25 year old back-ups. It has been developed initially with ChatGPT and later finished with the help of GitHub Copilot in Visual Studio 2022 Community Edition. No manual code editing, just me reviewing the code and verifying functionality.
Refer to the Important note section if you have any concerns.


## Build and integration tests

Development requires Windows and the .NET 10 SDK. `global.json` selects the latest
installed stable .NET 10 feature band. Runtime packages are resolved by that SDK.

```powershell
dotnet build ClutterFlock.sln -c Release
pwsh -NoProfile -File scripts/test-integration.ps1 -NoBuild
dotnet run --project ClutterFlock.csproj
```

The VS Code **test with coverage** task runs the same script, including a build.
Tests exercise real files, the public service interfaces, the application workflow,
and the WPF window on an STA thread. There are no isolated unit tests or mock service
implementations. Each run produces TRX and Cobertura reports in
`ClutterFlock.Tests/TestResults`; CI retains the 75% line / 60% branch coverage gate.

## Projects and multiple roots

Add any number of roots, including overlapping roots. Each directory is compared
using its immediate files. Matches require the same filename (ignoring case), size,
and SHA-256 content hash. Similarity counts files, and the size filter uses the larger
folder in a pair. Directory junctions and symbolic links to directories are skipped
during recursive discovery to avoid traversal cycles.

Saving writes a compressed version 3 `.cfp` snapshot containing all roots, folder
and file metadata, hashes, analysis matches, filters, the unique-files setting, and
the selected folder pair. Files are grouped under root-relative folders, with each
filename, metadata record, and hash stored once. Matches reference integer file IDs
instead of repeating full paths. The ZIP container holds `project.json`,
`folders.json`, and `matches.bin`; entries are streamed during saving and loading.
The destination is replaced only after the complete temporary file is flushed.
A failed save retains the existing project.

Loading restores results and file details without rescanning the source drives.
Unavailable roots remain in the project, so saved results can be inspected offline.
**Run Comparison** explicitly rescans and rehashes all roots. This costs more than
reusing old hashes but detects content changes even when size and timestamps were
preserved. Reconnect unavailable roots before rerunning comparison. Cancellation
or failure retains the previous analysis. Skipped inaccessible items are reported
in the status message.

Legacy version 1 and version 2 `.cfp` and `.dfp` files can be loaded directly;
saving upgrades them to version 3. Version 1 matches are reconstructed from available
saved hashes. Metadata that was never saved appears as `N/A` until a fresh comparison.
Malformed or unsupported projects leave the current session intact.

The storage integration test compares the old JSON snapshot with the compact format
using 1,000 files across 40 backup folders and 19,500 matches. It also verifies offline
restoration, legacy migration, and rejection of damaged containers. Actual size
reduction depends on the project's paths and duplicate matches.
