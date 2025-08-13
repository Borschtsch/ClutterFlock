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
                var leftFile = leftFiles.FirstOrDefault(f => 
                    Path.GetFileName(f).Equals(fileName, StringComparison.OrdinalIgnoreCase));
                var rightFile = rightFiles.FirstOrDefault(f => 
                    Path.GetFileName(f).Equals(fileName, StringComparison.OrdinalIgnoreCase));
                
                var isDuplicate = duplicateFileMap.ContainsKey(fileName);
                
                var fileDetail = new FileDetailInfo
                {
                    IsDuplicate = isDuplicate
                };
                
                // Populate left folder file info
                if (leftFile != null)
                {
                    PopulateFileInfo(leftFile, fileDetail, isLeft: true);
                }
                
                // Populate right folder file info
                if (rightFile != null)
                {
                    PopulateFileInfo(rightFile, fileDetail, isLeft: false);
                }
                
                fileDetails.Add(fileDetail);
            }
            
            return fileDetails;
        }

        public List<FileDetailInfo> FilterFileDetails(List<FileDetailInfo> allFiles, bool showUniqueFiles)
        {
            return showUniqueFiles ? allFiles : allFiles.Where(f => f.IsDuplicate).ToList();
        }

        private static void PopulateFileInfo(string filePath, FileDetailInfo fileDetail, bool isLeft)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                var fileName = Path.GetFileName(filePath);
                var sizeDisplay = FormatSize(fileInfo.Length);
                var dateDisplay = FormatDate(fileInfo.LastWriteTime);
                
                if (isLeft)
                {
                    fileDetail.LeftFileName = fileName;
                    fileDetail.LeftSizeDisplay = sizeDisplay;
                    fileDetail.LeftSizeBytes = fileInfo.Length;
                    fileDetail.LeftDateDisplay = dateDisplay;
                    fileDetail.LeftDate = fileInfo.LastWriteTime;
                    fileDetail.LeftFullPath = filePath;
                }
                else
                {
                    fileDetail.RightFileName = fileName;
                    fileDetail.RightSizeDisplay = sizeDisplay;
                    fileDetail.RightSizeBytes = fileInfo.Length;
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
            if (size >= 1L << 30) return $"{size / (1L << 30):N1} GB";
            if (size >= 1L << 20) return $"{size / (1L << 20):N1} MB";
            if (size >= 1L << 10) return $"{size / (1L << 10):N1} KB";
            return $"{size} B";
        }

        private static string FormatDate(DateTime date)
        {
            return date.ToString("yyyy-MM-dd HH:mm");
        }
    }
}
