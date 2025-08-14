using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ClutterFlock.Core;
using ClutterFlock.Models;
using ClutterFlock.Tests.TestUtilities;

namespace ClutterFlock.Tests.Unit.Core
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class CacheManagerTests : TestBase
    {
        private CacheManager _cacheManager = null!;

        [TestInitialize]
        public override void TestInitialize()
        {
            base.TestInitialize();
            _cacheManager = new CacheManager();
        }

        [TestMethod]
        public void IsFolderCached_WithUncachedFolder_ReturnsFalse()
        {
            // Arrange
            var folderPath = @"C:\TestFolder";

            // Act
            var result = _cacheManager.IsFolderCached(folderPath);

            // Assert
            Assert.IsFalse(result, "Uncached folder should return false");
        }

        [TestMethod]
        public void IsFolderCached_WithCachedFolder_ReturnsTrue()
        {
            // Arrange
            var folderPath = @"C:\TestFolder";
            var folderInfo = new FolderInfo
            {
                Files = new List<string> { @"C:\TestFolder\file1.txt" },
                TotalSize = 1024
            };

            // Act
            _cacheManager.CacheFolderInfo(folderPath, folderInfo);
            var result = _cacheManager.IsFolderCached(folderPath);

            // Assert
            Assert.IsTrue(result, "Cached folder should return true");
        }

        [TestMethod]
        public void IsFolderCached_CaseInsensitive_ReturnsTrue()
        {
            // Arrange
            var folderPath = @"C:\TestFolder";
            var folderPathDifferentCase = @"c:\testfolder";
            var folderInfo = new FolderInfo
            {
                Files = new List<string> { @"C:\TestFolder\file1.txt" },
                TotalSize = 1024
            };

            // Act
            _cacheManager.CacheFolderInfo(folderPath, folderInfo);
            var result = _cacheManager.IsFolderCached(folderPathDifferentCase);

            // Assert
            Assert.IsTrue(result, "Cache should be case-insensitive");
        }

        [TestMethod]
        public void CacheFolderInfo_ValidData_StoresCorrectly()
        {
            // Arrange
            var folderPath = @"C:\TestFolder";
            var files = new List<string> { @"C:\TestFolder\file1.txt", @"C:\TestFolder\file2.txt" };
            var folderInfo = new FolderInfo
            {
                Files = files,
                TotalSize = 2048,
                LatestModificationDate = DateTime.Now
            };

            // Act
            _cacheManager.CacheFolderInfo(folderPath, folderInfo);

            // Assert
            Assert.IsTrue(_cacheManager.IsFolderCached(folderPath));
            var retrievedInfo = _cacheManager.GetFolderInfo(folderPath);
            Assert.IsNotNull(retrievedInfo);
            Assert.AreEqual(folderInfo.TotalSize, retrievedInfo.TotalSize);
            Assert.AreEqual(folderInfo.Files.Count, retrievedInfo.Files.Count);
            CollectionAssert.AreEqual(folderInfo.Files, retrievedInfo.Files);
        }

        [TestMethod]
        public void GetFolderInfo_WithUncachedFolder_ReturnsNull()
        {
            // Arrange
            var folderPath = @"C:\NonExistentFolder";

            // Act
            var result = _cacheManager.GetFolderInfo(folderPath);

            // Assert
            Assert.IsNull(result, "Uncached folder should return null");
        }

        [TestMethod]
        public void CacheFileHash_ValidData_StoresCorrectly()
        {
            // Arrange
            var filePath = @"C:\TestFolder\file1.txt";
            var hash = "ABC123DEF456";

            // Act
            _cacheManager.CacheFileHash(filePath, hash);

            // Assert
            var retrievedHash = _cacheManager.GetFileHash(filePath);
            Assert.AreEqual(hash, retrievedHash);
        }

        [TestMethod]
        public void GetFileHash_WithUncachedFile_ReturnsNull()
        {
            // Arrange
            var filePath = @"C:\NonExistentFile.txt";

            // Act
            var result = _cacheManager.GetFileHash(filePath);

            // Assert
            Assert.IsNull(result, "Uncached file hash should return null");
        }

        [TestMethod]
        public void GetFileHash_CaseInsensitive_ReturnsCorrectHash()
        {
            // Arrange
            var filePath = @"C:\TestFolder\File1.txt";
            var filePathDifferentCase = @"c:\testfolder\file1.txt";
            var hash = "ABC123DEF456";

            // Act
            _cacheManager.CacheFileHash(filePath, hash);
            var result = _cacheManager.GetFileHash(filePathDifferentCase);

            // Assert
            Assert.AreEqual(hash, result, "File hash cache should be case-insensitive");
        }

        [TestMethod]
        public void ClearCache_RemovesAllCachedData()
        {
            // Arrange
            var folderPath = @"C:\TestFolder";
            var filePath = @"C:\TestFolder\file1.txt";
            var folderInfo = new FolderInfo { Files = new List<string> { filePath }, TotalSize = 1024 };
            var hash = "ABC123";
            var metadata = new FileMetadata { FileName = "file1.txt", Size = 1024 };

            _cacheManager.CacheFolderInfo(folderPath, folderInfo);
            _cacheManager.CacheFileHash(filePath, hash);
            _cacheManager.CacheFileMetadata(filePath, metadata);

            // Act
            _cacheManager.ClearCache();

            // Assert
            Assert.IsFalse(_cacheManager.IsFolderCached(folderPath));
            Assert.IsNull(_cacheManager.GetFileHash(filePath));
            Assert.IsNull(_cacheManager.GetFileMetadata(filePath));
            Assert.AreEqual(0, _cacheManager.GetCachedFolderCount());
            Assert.AreEqual(0, _cacheManager.GetCachedFileHashCount());
        }

        [TestMethod]
        public void RemoveFolderFromCache_RemovesSpecificFolder()
        {
            // Arrange
            var folder1 = @"C:\TestFolder1";
            var folder2 = @"C:\TestFolder2";
            var file1 = @"C:\TestFolder1\file1.txt";
            var file2 = @"C:\TestFolder2\file2.txt";
            
            var folderInfo1 = new FolderInfo { Files = new List<string> { file1 }, TotalSize = 1024 };
            var folderInfo2 = new FolderInfo { Files = new List<string> { file2 }, TotalSize = 2048 };

            _cacheManager.CacheFolderInfo(folder1, folderInfo1);
            _cacheManager.CacheFolderInfo(folder2, folderInfo2);
            _cacheManager.CacheFileHash(file1, "hash1");
            _cacheManager.CacheFileHash(file2, "hash2");

            // Act
            _cacheManager.RemoveFolderFromCache(folder1);

            // Assert
            Assert.IsFalse(_cacheManager.IsFolderCached(folder1));
            Assert.IsTrue(_cacheManager.IsFolderCached(folder2));
            Assert.IsNull(_cacheManager.GetFileHash(file1));
            Assert.IsNotNull(_cacheManager.GetFileHash(file2));
        }

        [TestMethod]
        public void RemoveFolderFromCache_RemovesSubfolders()
        {
            // Arrange
            var parentFolder = @"C:\Parent";
            var childFolder = @"C:\Parent\Child";
            var grandchildFolder = @"C:\Parent\Child\Grandchild";
            
            var parentInfo = new FolderInfo { Files = new List<string>(), TotalSize = 0 };
            var childInfo = new FolderInfo { Files = new List<string>(), TotalSize = 1024 };
            var grandchildInfo = new FolderInfo { Files = new List<string>(), TotalSize = 512 };

            _cacheManager.CacheFolderInfo(parentFolder, parentInfo);
            _cacheManager.CacheFolderInfo(childFolder, childInfo);
            _cacheManager.CacheFolderInfo(grandchildFolder, grandchildInfo);

            // Act
            _cacheManager.RemoveFolderFromCache(parentFolder);

            // Assert
            Assert.IsFalse(_cacheManager.IsFolderCached(parentFolder));
            Assert.IsFalse(_cacheManager.IsFolderCached(childFolder));
            Assert.IsFalse(_cacheManager.IsFolderCached(grandchildFolder));
        }

        [TestMethod]
        public void GetAllFolderInfo_ReturnsAllCachedFolders()
        {
            // Arrange
            var folder1 = @"C:\TestFolder1";
            var folder2 = @"C:\TestFolder2";
            var folderInfo1 = new FolderInfo { Files = new List<string>(), TotalSize = 1024 };
            var folderInfo2 = new FolderInfo { Files = new List<string>(), TotalSize = 2048 };

            _cacheManager.CacheFolderInfo(folder1, folderInfo1);
            _cacheManager.CacheFolderInfo(folder2, folderInfo2);

            // Act
            var allFolders = _cacheManager.GetAllFolderInfo();

            // Assert
            Assert.AreEqual(2, allFolders.Count);
            Assert.IsTrue(allFolders.ContainsKey(folder1));
            Assert.IsTrue(allFolders.ContainsKey(folder2));
            Assert.AreEqual(folderInfo1.TotalSize, allFolders[folder1].TotalSize);
            Assert.AreEqual(folderInfo2.TotalSize, allFolders[folder2].TotalSize);
        }

        [TestMethod]
        public void GetAllFileHashes_ReturnsAllCachedHashes()
        {
            // Arrange
            var file1 = @"C:\TestFolder\file1.txt";
            var file2 = @"C:\TestFolder\file2.txt";
            var hash1 = "ABC123";
            var hash2 = "DEF456";

            _cacheManager.CacheFileHash(file1, hash1);
            _cacheManager.CacheFileHash(file2, hash2);

            // Act
            var allHashes = _cacheManager.GetAllFileHashes();

            // Assert
            Assert.AreEqual(2, allHashes.Count);
            Assert.IsTrue(allHashes.ContainsKey(file1));
            Assert.IsTrue(allHashes.ContainsKey(file2));
            Assert.AreEqual(hash1, allHashes[file1]);
            Assert.AreEqual(hash2, allHashes[file2]);
        }

        [TestMethod]
        public void GetFolderFiles_WithCachedFolder_ReturnsFiles()
        {
            // Arrange
            var folderPath = @"C:\TestFolder";
            var files = new List<string> { @"C:\TestFolder\file1.txt", @"C:\TestFolder\file2.txt" };
            var folderInfo = new FolderInfo { Files = files, TotalSize = 2048 };

            _cacheManager.CacheFolderInfo(folderPath, folderInfo);

            // Act
            var retrievedFiles = _cacheManager.GetFolderFiles(folderPath);

            // Assert
            Assert.AreEqual(files.Count, retrievedFiles.Count);
            CollectionAssert.AreEqual(files, retrievedFiles);
        }

        [TestMethod]
        public void GetFolderFiles_WithUncachedFolder_ReturnsEmptyList()
        {
            // Arrange
            var folderPath = @"C:\NonExistentFolder";

            // Act
            var result = _cacheManager.GetFolderFiles(folderPath);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void GetFolderSize_WithCachedFolder_ReturnsCorrectSize()
        {
            // Arrange
            var folderPath = @"C:\TestFolder";
            var expectedSize = 5120L;
            var folderInfo = new FolderInfo { Files = new List<string>(), TotalSize = expectedSize };

            _cacheManager.CacheFolderInfo(folderPath, folderInfo);

            // Act
            var actualSize = _cacheManager.GetFolderSize(folderPath);

            // Assert
            Assert.AreEqual(expectedSize, actualSize);
        }

        [TestMethod]
        public void GetFolderSize_WithUncachedFolder_ReturnsZero()
        {
            // Arrange
            var folderPath = @"C:\NonExistentFolder";

            // Act
            var result = _cacheManager.GetFolderSize(folderPath);

            // Assert
            Assert.AreEqual(0L, result);
        }

        [TestMethod]
        public void GetCachedFolderCount_ReturnsCorrectCount()
        {
            // Arrange
            var folder1 = @"C:\TestFolder1";
            var folder2 = @"C:\TestFolder2";
            var folderInfo = new FolderInfo { Files = new List<string>(), TotalSize = 1024 };

            // Act & Assert - Initially empty
            Assert.AreEqual(0, _cacheManager.GetCachedFolderCount());

            // Add folders and verify count
            _cacheManager.CacheFolderInfo(folder1, folderInfo);
            Assert.AreEqual(1, _cacheManager.GetCachedFolderCount());

            _cacheManager.CacheFolderInfo(folder2, folderInfo);
            Assert.AreEqual(2, _cacheManager.GetCachedFolderCount());
        }

        [TestMethod]
        public void GetCachedFileHashCount_ReturnsCorrectCount()
        {
            // Arrange
            var file1 = @"C:\TestFolder\file1.txt";
            var file2 = @"C:\TestFolder\file2.txt";

            // Act & Assert - Initially empty
            Assert.AreEqual(0, _cacheManager.GetCachedFileHashCount());

            // Add hashes and verify count
            _cacheManager.CacheFileHash(file1, "hash1");
            Assert.AreEqual(1, _cacheManager.GetCachedFileHashCount());

            _cacheManager.CacheFileHash(file2, "hash2");
            Assert.AreEqual(2, _cacheManager.GetCachedFileHashCount());
        }

        [TestMethod]
        public void CacheFileMetadata_ValidData_StoresCorrectly()
        {
            // Arrange
            var filePath = @"C:\TestFolder\file1.txt";
            var metadata = new FileMetadata
            {
                FileName = "file1.txt",
                Size = 1024,
                LastWriteTime = DateTime.Now
            };

            // Act
            _cacheManager.CacheFileMetadata(filePath, metadata);

            // Assert
            var retrievedMetadata = _cacheManager.GetFileMetadata(filePath);
            Assert.IsNotNull(retrievedMetadata);
            Assert.AreEqual(metadata.FileName, retrievedMetadata.FileName);
            Assert.AreEqual(metadata.Size, retrievedMetadata.Size);
            Assert.AreEqual(metadata.LastWriteTime, retrievedMetadata.LastWriteTime);
        }

        [TestMethod]
        public void GetFileMetadata_WithUncachedFile_ReturnsNull()
        {
            // Arrange
            var filePath = @"C:\NonExistentFile.txt";

            // Act
            var result = _cacheManager.GetFileMetadata(filePath);

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void LoadFromProjectData_ValidData_LoadsCorrectly()
        {
            // Arrange
            var projectData = TestDataGenerator.CreateSampleProjectData(new List<string> { @"C:\TestFolder1", @"C:\TestFolder2" });

            // Act
            _cacheManager.LoadFromProjectData(projectData);

            // Assert
            Assert.AreEqual(projectData.FolderInfoCache.Count, _cacheManager.GetCachedFolderCount());
            Assert.AreEqual(projectData.FileHashCache.Count, _cacheManager.GetCachedFileHashCount());

            foreach (var kvp in projectData.FolderInfoCache)
            {
                Assert.IsTrue(_cacheManager.IsFolderCached(kvp.Key));
                var cachedInfo = _cacheManager.GetFolderInfo(kvp.Key);
                Assert.IsNotNull(cachedInfo);
                Assert.AreEqual(kvp.Value.TotalSize, cachedInfo.TotalSize);
            }

            foreach (var kvp in projectData.FileHashCache)
            {
                var cachedHash = _cacheManager.GetFileHash(kvp.Key);
                Assert.AreEqual(kvp.Value, cachedHash);
            }
        }

        [TestMethod]
        public void LoadFromProjectData_ClearsExistingCache()
        {
            // Arrange
            var existingFolder = @"C:\ExistingFolder";
            var existingInfo = new FolderInfo { Files = new List<string>(), TotalSize = 1024 };
            _cacheManager.CacheFolderInfo(existingFolder, existingInfo);

            var projectData = TestDataGenerator.CreateSampleProjectData(new List<string> { @"C:\NewFolder" });

            // Act
            _cacheManager.LoadFromProjectData(projectData);

            // Assert
            Assert.IsFalse(_cacheManager.IsFolderCached(existingFolder));
            Assert.IsTrue(_cacheManager.IsFolderCached(@"C:\NewFolder"));
        }

        [TestMethod]
        public void ExportToProjectData_ReturnsCorrectData()
        {
            // Arrange
            var scanFolders = new List<string> { @"C:\TestFolder1", @"C:\TestFolder2" };
            var folder1 = @"C:\TestFolder1";
            var folder2 = @"C:\TestFolder2";
            var file1 = @"C:\TestFolder1\file1.txt";
            var file2 = @"C:\TestFolder2\file2.txt";

            var folderInfo1 = new FolderInfo { Files = new List<string> { file1 }, TotalSize = 1024 };
            var folderInfo2 = new FolderInfo { Files = new List<string> { file2 }, TotalSize = 2048 };

            _cacheManager.CacheFolderInfo(folder1, folderInfo1);
            _cacheManager.CacheFolderInfo(folder2, folderInfo2);
            _cacheManager.CacheFileHash(file1, "hash1");
            _cacheManager.CacheFileHash(file2, "hash2");

            // Act
            var projectData = _cacheManager.ExportToProjectData(scanFolders);

            // Assert
            Assert.IsNotNull(projectData);
            CollectionAssert.AreEqual(scanFolders, projectData.ScanFolders);
            Assert.AreEqual(2, projectData.FolderInfoCache.Count);
            Assert.AreEqual(2, projectData.FileHashCache.Count);
            Assert.IsTrue(projectData.FolderInfoCache.ContainsKey(folder1));
            Assert.IsTrue(projectData.FolderInfoCache.ContainsKey(folder2));
            Assert.IsTrue(projectData.FileHashCache.ContainsKey(file1));
            Assert.IsTrue(projectData.FileHashCache.ContainsKey(file2));
        }

        [TestMethod]
        public void ThreadSafety_ConcurrentOperations_NoExceptions()
        {
            // Arrange
            const int threadCount = 10;
            const int operationsPerThread = 100;
            var tasks = new List<Task>();

            // Act
            for (int t = 0; t < threadCount; t++)
            {
                int threadId = t;
                tasks.Add(Task.Run(() =>
                {
                    for (int i = 0; i < operationsPerThread; i++)
                    {
                        var folderPath = $@"C:\Thread{threadId}\Folder{i}";
                        var filePath = $@"C:\Thread{threadId}\Folder{i}\file{i}.txt";
                        var folderInfo = new FolderInfo { Files = new List<string> { filePath }, TotalSize = i * 1024 };

                        _cacheManager.CacheFolderInfo(folderPath, folderInfo);
                        _cacheManager.CacheFileHash(filePath, $"hash{threadId}_{i}");
                        
                        var retrievedInfo = _cacheManager.GetFolderInfo(folderPath);
                        var retrievedHash = _cacheManager.GetFileHash(filePath);
                        
                        Assert.IsNotNull(retrievedInfo);
                        Assert.IsNotNull(retrievedHash);
                    }
                }));
            }

            // Assert - No exceptions should be thrown
            AssertCompletesWithinTime(() => Task.WaitAll(tasks.ToArray()), TimeSpan.FromSeconds(30));
            
            // Verify final state
            Assert.AreEqual(threadCount * operationsPerThread, _cacheManager.GetCachedFolderCount());
            Assert.AreEqual(threadCount * operationsPerThread, _cacheManager.GetCachedFileHashCount());
        }

        [TestMethod]
        public void LoadFromProjectData_WithExistingFiles_RebuildsFileMetadataCache()
        {
            // Arrange
            var tempDir = CreateTempDirectory();
            var testFile1 = Path.Combine(tempDir, "test1.txt");
            var testFile2 = Path.Combine(tempDir, "test2.txt");
            
            // Create real files
            File.WriteAllText(testFile1, "Test content 1");
            File.WriteAllText(testFile2, "Test content 2");
            
            var projectData = new ProjectData
            {
                ScanFolders = new List<string> { tempDir },
                FolderInfoCache = new Dictionary<string, FolderInfo>
                {
                    [tempDir] = new FolderInfo
                    {
                        Files = new List<string> { testFile1, testFile2 },
                        TotalSize = 1024,
                        LatestModificationDate = DateTime.Now
                    }
                },
                FileHashCache = new Dictionary<string, string>
                {
                    [testFile1] = "hash1",
                    [testFile2] = "hash2"
                },
                FolderFileCache = new Dictionary<string, List<string>>
                {
                    [tempDir] = new List<string> { testFile1, testFile2 }
                }
            };

            // Act
            _cacheManager.LoadFromProjectData(projectData);

            // Assert
            var metadata1 = _cacheManager.GetFileMetadata(testFile1);
            var metadata2 = _cacheManager.GetFileMetadata(testFile2);
            
            Assert.IsNotNull(metadata1);
            Assert.IsNotNull(metadata2);
            Assert.AreEqual("test1.txt", metadata1.FileName);
            Assert.AreEqual("test2.txt", metadata2.FileName);
            Assert.IsTrue(metadata1.Size > 0);
            Assert.IsTrue(metadata2.Size > 0);
        }

        [TestMethod]
        public void LoadFromProjectData_WithNonExistentFiles_SkipsFileMetadataCreation()
        {
            // Arrange
            var nonExistentFile1 = @"C:\NonExistent\file1.txt";
            var nonExistentFile2 = @"C:\NonExistent\file2.txt";
            
            var projectData = new ProjectData
            {
                ScanFolders = new List<string> { @"C:\NonExistent" },
                FolderInfoCache = new Dictionary<string, FolderInfo>
                {
                    [@"C:\NonExistent"] = new FolderInfo
                    {
                        Files = new List<string> { nonExistentFile1, nonExistentFile2 },
                        TotalSize = 1024,
                        LatestModificationDate = DateTime.Now
                    }
                },
                FileHashCache = new Dictionary<string, string>
                {
                    [nonExistentFile1] = "hash1",
                    [nonExistentFile2] = "hash2"
                },
                FolderFileCache = new Dictionary<string, List<string>>
                {
                    [@"C:\NonExistent"] = new List<string> { nonExistentFile1, nonExistentFile2 }
                }
            };

            // Act
            _cacheManager.LoadFromProjectData(projectData);

            // Assert - Should not throw exceptions and should load other data correctly
            Assert.AreEqual(1, _cacheManager.GetCachedFolderCount());
            Assert.AreEqual(2, _cacheManager.GetCachedFileHashCount());
            
            // File metadata should not be created for non-existent files
            var metadata1 = _cacheManager.GetFileMetadata(nonExistentFile1);
            var metadata2 = _cacheManager.GetFileMetadata(nonExistentFile2);
            Assert.IsNull(metadata1);
            Assert.IsNull(metadata2);
        }

        [TestMethod]
        public void LoadFromProjectData_WithInaccessibleFiles_HandlesExceptionsGracefully()
        {
            // Arrange
            var tempDir = CreateTempDirectory();
            var testFile = Path.Combine(tempDir, "test.txt");
            
            // Create a file and then make it inaccessible by creating a directory with the same name
            File.WriteAllText(testFile, "Test content");
            File.Delete(testFile);
            Directory.CreateDirectory(testFile); // This will cause FileInfo to throw when accessing properties
            
            var projectData = new ProjectData
            {
                ScanFolders = new List<string> { tempDir },
                FolderInfoCache = new Dictionary<string, FolderInfo>
                {
                    [tempDir] = new FolderInfo
                    {
                        Files = new List<string> { testFile },
                        TotalSize = 1024,
                        LatestModificationDate = DateTime.Now
                    }
                },
                FileHashCache = new Dictionary<string, string>
                {
                    [testFile] = "hash1"
                },
                FolderFileCache = new Dictionary<string, List<string>>
                {
                    [tempDir] = new List<string> { testFile }
                }
            };

            // Act & Assert - Should not throw exceptions
            _cacheManager.LoadFromProjectData(projectData);
            
            // Should load other data correctly despite file access issues
            Assert.AreEqual(1, _cacheManager.GetCachedFolderCount());
            Assert.AreEqual(1, _cacheManager.GetCachedFileHashCount());
            
            // File metadata should not be created due to access issues
            var metadata = _cacheManager.GetFileMetadata(testFile);
            Assert.IsNull(metadata);
        }
    }
}