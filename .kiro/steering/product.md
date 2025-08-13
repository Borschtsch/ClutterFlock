# Product Overview

ClutterFlock is a Windows desktop application designed to find and analyze duplicate folders across file systems. The primary use case is optimizing decades-old backup archives by identifying folders containing the same files (photos, documents, etc.) that have been moved around or duplicated over time.

## Core Purpose
Find and analyze duplicate folders across file systems to help optimize backup storage and organize decades-old backup archives. The system identifies duplicate files based on filename, size, and SHA-256 hash, then computes folder similarity percentages based on shared duplicate files.

## Key Features
- **Intelligent Folder Scanning:** Recursive discovery with smart caching and parallel processing
- **Advanced Duplicate Detection:** Multi-factor matching (filename, size, SHA-256 hash) with cross-hierarchy analysis
- **Flexible Results Filtering:** Filter by similarity percentage (0-100%) and minimum folder size
- **WinMerge-Style File Comparison:** Dual-pane view with color-coded file status (duplicates, unique, missing)
- **Project State Management:** Save/resume analysis sessions with complete state preservation (.cfp format with legacy .dfp support)
- **Real-Time Progress Control:** Multi-phase progress tracking with cancellation support
- **Performance Optimization:** Handles large datasets (100,000+ subfolders, millions of files) efficiently

## Target Users & Use Cases
- **Legacy Backup Organization:** Users with decades of unorganized backup drives
- **Photo Collection Management:** Photo enthusiasts with scattered image collections across devices
- **Document Archive Cleanup:** Businesses/individuals with redundant document storage
- **Storage Optimization:** Users needing to identify largest duplicate folders for space recovery

## Success Criteria
- **Performance:** Process 10,000 files per minute, maintain <2GB memory usage, <100ms UI response
- **Accuracy:** 100% accurate duplicate detection based on hash comparison
- **Scalability:** Handle folder hierarchies with 100,000+ subfolders efficiently

## Important Notes
- This is NOT a backup management tool - it's for analysis only
- Focus is on folder-level duplicates, not individual file duplicates
- Users are responsible for verifying results before deleting data
- Read-only operation - never modifies user files
- Originally developed using AI assistance (ChatGPT/GitHub Copilot)