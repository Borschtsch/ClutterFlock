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
When the normal executable is running, use `scripts/test-integration.ps1 -Configuration
Streaming` for a separate test build without interrupting it.
Tests exercise real files, the public service interfaces, the application workflow,
and the WPF window on an STA thread. There are no isolated unit tests or mock service
implementations. Each run produces TRX and Cobertura reports in
`ClutterFlock.Tests/TestResults`; CI retains the 75% line / 60% branch coverage gate.

## Workspace workflow

1. **Prepare locations.** Add multiple folders in the picker or drop folders onto
   the window. Set useful location labels under **Location label and access**.
   Adding locations does not scan them; **Compare / refresh** starts discovery and
   content verification. After metadata discovery, verified matches appear incrementally
   while hashing continues. The workspace remains browsable and filterable; live pairs
   say **Verification in progress** until the full comparison finishes. Access checks are explicit so offline projects open promptly.
2. **Explore duplicates.** Folder comparisons and file evidence fill the workspace.
   Expand **Locations** above the comparisons to add, label, or filter locations.
   Search folder paths and inspect matching folder pairs. Both folder identities, their location
   context, verified coverage, and relationship are visible. All sizes and similarity
   levels are included by default; optional filters can narrow the results.
3. **Inspect evidence.** Choose All, Differences, Identical, Only A, Only B,
   Different contents, or Unverified. A missing hash is not proof of different
   contents. Sizes sort numerically, and modification dates are available as columns.
   Open either folder in Explorer or copy its path when investigating outside the app.
4. **Record the investigation.** Expand **Review and notes** to bookmark a pair,
   record notes, or mark it Reviewed, Investigate, or Ignore. Refreshing analysis
   marks existing reviews as needing another check.
5. **Save and resume.** Save retains labels, prepared locations, search/filter state,
   pane widths, review notes, bookmarks, analysis time, and reported analysis issues.
   Ctrl+S saves to the current project; Save as creates another project file.
   Ctrl+O opens and Ctrl+N starts a new workspace. Closing or replacing an unsaved
   workspace offers Save / Discard / Cancel.

Analysis groups candidates by case-insensitive filename and size, with smaller
candidates first. Unique name/size combinations never need content reads. Large
candidates (at least 1 MiB) are sampled at the beginning, middle and end (64 KiB each).
Different samples reject a match; equal samples still require full SHA-256 verification.
Sample evidence is saved separately from full hashes and can prove differences, never
identity. Empty files need metadata validation, without reading or hashing content.
Empty-file matches are joined only into folder pairs already sharing non-empty content,
avoiding the quadratic expansion of empty-only folder combinations.

Scanning and hashing tune concurrency independently for each physical storage queue.
All roots start together; discovery and file metadata are collected in one pass.
Windows volume disk extents identify partitions on the same physical disk, including
mounted volumes. Volumes sharing physical disks share a queue. Network servers and
unidentified local storage use conservative fallback queues, labelled as such.

There is one logical drive worker per independent physical storage queue:
four drives means four drive workers minimum. The UI/coordinator is separate.
Each drive worker manages its own adaptive I/O subworkers, starting at one and never
dropping below one while work remains. The total I/O subworker budget is the greater
of the drive count and available logical processors minus one.

CPU-heavy hash/sample calculations and file-pair expansion share a separate limit of
max(1, available logical processors minus one). Reads do not hold these CPU slots,
so every drive can perform I/O even when the drive count exceeds CPU capacity.
On a single-CPU allocation, CPU work shares that CPU; a separate processor cannot be
reserved. These are concurrency limits, not dedicated OS threads, exclusive processor
affinity or guarantees against load from other programs.
CPU affinity, CPU-set and job limits are read from Windows at the start of every
comparison and held fixed for that run, alongside its storage topology. Every 250 ms,
the controller samples metadata entries/s during scanning or bytes/s during reads.
The first growth probe starts after two samples (half a second). Subsequent trials
use a four-sample moving window (one second). Successful probes double the allocation
while spare capacity exists; a gain of at least 5% keeps the increase. Otherwise the
controller restores the previous allocation and waits three seconds before retrying. It continues probing up to the available I/O budget. A sustained
fall of 15% releases a slot for other devices. Every device retains its first subworker.
When the budget is full, the controller periodically trials a transfer from another
device. A transfer is kept only when the receiver improves and the combined throughput
of both devices improves by at least 5%; otherwise the previous allocation is restored.
Only one trial runs at a time, with fresh measurement windows after old operations drain.
Devices are retried throughout the run so a change in workload or device performance
can change the allocation. In-flight operations finish normally when limits decrease.
These measured heuristics respond to throughput; they cannot distinguish device
saturation from other processes, CPU pressure, or changing file sizes with certainty.

Scanning reuses metadata returned by directory enumeration and batches short jobs
to avoid dispatching a new task for every folder. New allocations are dispatched
immediately after each tuning decision.

Progress distinguishes allocated I/O subworkers from currently running operations,
and shows allocated/running/queued counts per device. Four detected devices always
retain at least four allocated subworkers; running operations can drop below four
as devices finish their work or have no pending jobs.
Progress reports the current device allocation, bytes read, throughput, full hashes,
sample exclusions, cache hits and verified pairs. Existing hashes in the current
analysis cache are reused without reading the files. The former saved manual read
limit remains readable for project compatibility; automatic tuning now controls runs.
Topology follows [Windows volume disk extents](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ns-winioctl-volume_disk_extents);
the CPU budget refreshes [Windows process affinity](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getprocessaffinitymask) and applicable CPU limits for each comparison.

Workers accumulate evidence in a background buffer, coalesced by folder pair. They do
not wait for the UI to render. The UI drains up to 128 changed pairs per update, about
every 100 ms during verification, and drains faster after verification completes. This
stores pending result data, not one dispatcher callback per file; the application also
avoids retaining a second, unused complete file-match list. Large genuine duplicate
sets still require space for their resulting evidence and folder pairs.
Saving and review edits become available after completion. Cancelling a live comparison
discards its preview and restores the previous completed analysis.

Changing prepared locations preserves the last analysis until a fresh comparison
finishes. A persistent banner explains when those locations differ from the saved
analysis. Saving during this state retains both the old evidence and the locations
prepared for the next scan. Saving and loading retain the compact version 3 container.

Relationships describe **recorded files directly inside each folder**, not entire
directory trees. Review the persistent completeness summary before drawing conclusions.
The engine matches same-name files by size and content hash; renamed copies are outside
its matching scope. A folder pair must share at least one verified non-empty file.
Shared empty files still count toward similarity and remain visible in that pair's
evidence, but empty files alone do not make folders a match. This also applies when
opening saved projects. No automatic choice of a preferred copy is made.

## Confirmed file and folder actions

Select an evidence row to open its A or B file with the associated application, or
permanently delete either file after a confirmation showing its exact path.

**Delete A/B** permanently deletes only the selected leaf folder and its direct
files. It is blocked if that folder has any subfolders. **Merge to A/B** moves files
into the chosen destination and removes the empty source folder; both folders must
be leaves. Neither operation descends into subfolders. Blocked parent operations
offer a manual route to child-folder comparisons or Explorer.

Merge checks current contents before offering confirmation and again before moving.
Conflict checks compare up to four file pairs concurrently and stop reading a pair at
its first differing block. These are exact byte comparisons, without calculating hashes.
The final check under file locks before discarding an identical source remains in place.
Conflicting files must be inspected and resolved explicitly: open either version,
then confirm deletion of the unwanted version. There is no automatic conflict winner,
renaming, or overwrite. Identical files at the same path are retained once.

Operations reject equal/nested paths, drive roots, symbolic links and junctions.
Cancellation/errors retain completed changes and may leave a partial operation.
Folder actions remove affected analysis evidence, including after partial failure;
**Compare / refresh** verifies current results. Saving the project does not undo a
filesystem operation. All deletion bypasses the Recycle Bin.

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
