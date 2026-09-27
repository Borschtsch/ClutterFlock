using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClutterFlock.Models;
using ClutterFlock.Services;

namespace ClutterFlock.Core
{
    /// <summary>
    /// Handles file comparison operations for the detail view
    /// </summary>
    public class FileComparer : IFileComparer
    {
        public List<FileDetailInfo> BuildFileComparison(string leftFolder, string rightFolder, 
            List<FileMatch> duplicateFiles, ICacheManager cacheManager)
        {
            var fileDetails = new List<FileDetailInfo>();
            
            // Get all files from both folders
            var leftFiles = cacheManager.GetFolderFiles(leftFolder);
            var rightFiles = cacheManager.GetFolderFiles(rightFolder);
            var leftByName = leftFiles.ToDictionary(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);
            var rightByName = rightFiles.ToDictionary(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);
            
            // Create lookup for duplicate files by filename
            var duplicateFileMap = new Dictionary<string, FileMatch>(StringComparer.OrdinalIgnoreCase);
            foreach (var match in duplicateFiles)
            {
                var fileName = Path.GetFileName(match.PathA);
                duplicateFileMap[fileName] = match;
            }
            
            // Get all unique file names from both folders
            var allFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in leftFiles)
                allFileNames.Add(Path.GetFileName(file));
            foreach (var file in rightFiles)
                allFileNames.Add(Path.GetFileName(file));
            
            // Build file details for each unique file name
            foreach (var fileName in allFileNames.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                leftByName.TryGetValue(fileName, out var leftFile);
                rightByName.TryGetValue(fileName, out var rightFile);
                
                var isDuplicate = duplicateFileMap.ContainsKey(fileName);
                
                var fileDetail = new FileDetailInfo
                {
                    IsDuplicate = isDuplicate
                };
                
                // Populate left folder file info
                if (leftFile != null)
                {
                    PopulateFileInfo(leftFile, fileDetail, cacheManager, isLeft: true);
                }
                
                // Populate right folder file info
                if (rightFile != null)
                {
                    PopulateFileInfo(rightFile, fileDetail, cacheManager, isLeft: false);
                }
                
                // A missing hash is unknown evidence, not proof of different contents.
                var leftMetadata = leftFile == null ? null : cacheManager.GetFileMetadata(leftFile);
                var rightMetadata = rightFile == null ? null : cacheManager.GetFileMetadata(rightFile);
                var leftHash = leftFile == null ? null : cacheManager.GetFileHash(leftFile);
                var rightHash = rightFile == null ? null : cacheManager.GetFileHash(rightFile);
                fileDetail.Status = isDuplicate ? "Identical" : leftFile == null ? "Only B" : rightFile == null ? "Only A"
                    : (leftMetadata != null && rightMetadata != null && leftMetadata.Size != rightMetadata.Size)
                        || (leftHash != null && rightHash != null && !leftHash.Equals(rightHash, StringComparison.OrdinalIgnoreCase))
                        || (leftMetadata?.ContentSample is { } leftSample && rightMetadata?.ContentSample is { } rightSample
                            && !leftSample.Equals(rightSample, StringComparison.OrdinalIgnoreCase))
                        ? "Different contents" : "Unverified";
                fileDetails.Add(fileDetail);
            }
            
            return fileDetails;
        }

        public List<FileDetailInfo> FilterFileDetails(List<FileDetailInfo> allFiles, bool showUniqueFiles)
        {
            return showUniqueFiles ? allFiles : allFiles.Where(f => f.IsDuplicate).ToList();
        }

        private static void PopulateFileInfo(string filePath, FileDetailInfo fileDetail, ICacheManager cacheManager, bool isLeft)
        {
            try
            {
                var fileInfo = cacheManager.GetFileMetadata(filePath) ?? throw new IOException("Metadata not available in saved project");
                var fileName = Path.GetFileName(filePath);
                var sizeDisplay = FormatSize(fileInfo.Size);
                var dateDisplay = FormatDate(fileInfo.LastWriteTime);
                
                if (isLeft)
                {
                    fileDetail.LeftFileName = fileName;
                    fileDetail.LeftSizeDisplay = sizeDisplay;
                    fileDetail.LeftSizeBytes = fileInfo.Size;
                    fileDetail.LeftDateDisplay = dateDisplay;
                    fileDetail.LeftDate = fileInfo.LastWriteTime;
                    fileDetail.LeftFullPath = filePath;
                }
                else
                {
                    fileDetail.RightFileName = fileName;
                    fileDetail.RightSizeDisplay = sizeDisplay;
                    fileDetail.RightSizeBytes = fileInfo.Size;
                    fileDetail.RightDateDisplay = dateDisplay;
                    fileDetail.RightDate = fileInfo.LastWriteTime;
                    fileDetail.RightFullPath = filePath;
                }
            }
            catch
            {
                // Handle file access errors gracefully
                if (isLeft)
                {
                    fileDetail.LeftFileName = Path.GetFileName(filePath);
                    fileDetail.LeftSizeDisplay = "N/A";
                    fileDetail.LeftDateDisplay = "N/A";
                    fileDetail.LeftFullPath = filePath;
                }
                else
                {
                    fileDetail.RightFileName = Path.GetFileName(filePath);
                    fileDetail.RightSizeDisplay = "N/A";
                    fileDetail.RightDateDisplay = "N/A";
                    fileDetail.RightFullPath = filePath;
                }
            }
        }

        private static string FormatSize(long size)
        {
            if (size >= 1L << 30) return $"{size / (double)(1L << 30):N1} GB";
            if (size >= 1L << 20) return $"{size / (double)(1L << 20):N1} MB";
            if (size >= 1L << 10) return $"{size / (double)(1L << 10):N1} KB";
            return $"{size} B";
        }

        private static string FormatDate(DateTime date)
        {
            return date.ToString("yyyy-MM-dd HH:mm");
        }
    }
}
