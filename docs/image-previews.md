# Image previews

Select an image row in the comparison evidence to see small A/B previews. Double-click
the row, press Enter, or use **Enlarge…** for a resizable side-by-side window. Escape
closes the window. Each side shows its filename and image dimensions; hover over
the filename for its full path. Images preserve their aspect ratio and EXIF orientation.

JPEG, PNG, BMP, GIF, TIFF and ICO use Windows/WPF image support. Additional formats
such as WebP and HEIC depend on an installed compatible image codec. Animated or
multi-page files show their first frame. Text and document previews are not included.
Unavailable, unsupported or damaged files show a message on the affected side.

Previews read the current source files; they are not saved in project snapshots and
do not change duplicate evidence. Inline previews decode to a maximum edge of 480
pixels; the larger window uses 2,560 pixels. Loading runs off the UI thread with at
most two decoders at once and a brief selection debounce. Superseded loads cannot
replace a newer selection. Completed previews release file handles and never write
to source files. Native decoding already in progress finishes before cancellation
can discard its result.

## Reviewing images in the large window

**Previous / Next** and the left/right arrow keys navigate image rows in the current
folder pair. **Differences only** is checked initially: navigation skips recorded
identical files and includes one-sided and unverified images. Uncheck it to include
identical images. The window uses all image rows in that folder pair, independently
of the main list's search and filters. It does not wrap around at either end.
An explicitly opened identical image stays visible until you navigate.

The controls clearly state **ONLY the displayed file pair**:

- **Open file A / B** opens that file in its associated application.
- **Delete file A / B** requires confirmation for permanent deletion of that one file.
- **Merge file to A / B** requires a fresh check and confirmation of the exact source
  and destination paths. It moves the source file to the chosen side when absent.
  If both files exist and their contents match, it keeps the destination and deletes
  the source copy. Different contents block merging: inspect both versions and
  explicitly delete the unwanted one first. Nothing is overwritten.

Other files and both folders are kept, including an emptied source folder. File
deletion requires a leaf source folder; merging requires both folders to be leaves.
Links and junctions are rejected. Navigation and other file actions are disabled
while a confirmation or action is in progress. The surviving file is shown after
deletion; affected evidence is refreshed after changes. If the folder pair no longer
has recorded matches, the viewer clears its candidates; compare / refresh to rebuild
the evidence. Selecting another folder pair closes the old viewer.
