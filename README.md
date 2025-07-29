# Folder duplicates finder

This tool can help you to optimize your decades old back-ups or find latest versions of the folders within your back-ups.
Imagine you took photos and never sorted them, but moved them around, replaced back-up drives and now you have multiples copies of the same photos from the same day in multiple places. How to find all the folders with the same data, not just individual files?

I ran into the problem of huge unsorted back-ups and no tool on the market that I could possibly find could help me with that, it either takes too long to scan the files or too much of my time to crawl through the files.
It supports filtering folders by the minimum size and minimum similarity level. Also tool can show content of the folder duplicates in a directory diff-merge style with only the files present in both directories or including the unique files.

![Alt text](/Screenshot/Main.jpg?raw=true "Example analysis")

# Important note
This tool it not designed to manage your back-ups, but it can help you to loo
This tool is not intended to find all file duplicates in your back-ups, its focus is to find similar folders that could store the same files (using file name, file size and hash value).

No responsibility taken if you yourself delete the folder with the latest data because it was not shown by the tool.

# Also important note
This is a vibe-coding project that I started to work on to sort out my 25 year old back-ups. It has been developed initially with ChatGPT and later finished with the help of GitHub Copilot in Visual Studio 2022 Community Edition. No manual code editing, just me reviewing the code and verifying functionality.
Refer to the Important note section if you have any concerns.
