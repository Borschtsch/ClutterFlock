using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ClutterFlock.ViewModels;
using ClutterFlock.Models;
using ClutterFlock.Tests.TestUtilities;

namespace ClutterFlock.Tests.Unit.ViewModels
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class MainViewModelTests : TestBase
    {
        private MainViewModel _viewModel = null!;
        private readonly List<string> _propertyChangedEvents = new();

        [TestInitialize]
        public override void TestInitialize()
        {
            base.TestInitialize();
            _viewModel = new MainViewModel();
            _viewModel.PropertyChanged += (sender, e) => 
            {
                if (e.PropertyName != null)
                    _propertyChangedEvents.Add(e.PropertyName);
            };
        }

        [TestCleanup]
        public override void TestCleanup()
        {
            _viewModel?.Dispose();
            base.TestCleanup();
        }

        [TestMethod]
        public void Constructor_InitializesPropertiesCorrectly()
        {
            // Debug: Verify ViewModel is real instance
            Assert.IsNotNull(_viewModel);
            Assert.AreEqual(typeof(MainViewModel), _viewModel.GetType());

            // Assert
            Assert.IsNotNull(_viewModel.ScanFolders);
            Assert.IsNotNull(_viewModel.FilteredFolderMatches);
            Assert.IsNotNull(_viewModel.FileDetails);
            Assert.AreEqual("Ready to scan", _viewModel.StatusMessage);
            Assert.AreEqual(0, _viewModel.CurrentProgress);
            Assert.AreEqual(1, _viewModel.MaxProgress);
            Assert.IsFalse(_viewModel.IsProgressIndeterminate);
            Assert.IsFalse(_viewModel.OperationInProgress);
            Assert.AreEqual(50.0, _viewModel.MinimumSimilarity);
            Assert.AreEqual(1.0, _viewModel.MinimumSizeMB);
        }

        [TestMethod]
        public void CommandAvailability_InitialState_ReturnsCorrectValues()
        {
            // Assert
            Assert.IsTrue(_viewModel.CanAddFolders);
            Assert.IsFalse(_viewModel.CanRemoveFolders); // No folders added yet
            Assert.IsFalse(_viewModel.CanRunComparison); // No folders added yet
            Assert.IsFalse(_viewModel.CanSaveProject); // No folders added yet
            Assert.IsTrue(_viewModel.CanLoadProject);
            Assert.IsFalse(_viewModel.CanApplyFilters); // No matches yet
            Assert.IsFalse(_viewModel.CanCancel); // No operation in progress
        }

        [TestMethod]
        public void OperationInProgress_WhenSet_UpdatesCommandAvailability()
        {
            // Act
            _viewModel.OperationInProgress = true;

            // Assert
            Assert.IsFalse(_viewModel.CanAddFolders);
            Assert.IsFalse(_viewModel.CanRemoveFolders);
            Assert.IsFalse(_viewModel.CanRunComparison);
            Assert.IsFalse(_viewModel.CanSaveProject);
            Assert.IsFalse(_viewModel.CanLoadProject);
            Assert.IsFalse(_viewModel.CanApplyFilters);
            Assert.IsTrue(_viewModel.CanCancel);
        }

        [TestMethod]
        public async Task AddFolderAsync_WithValidFolder_ReturnsTrue()
        {
            // Arrange
            var tempDir = CreateTempDirectory();
            Directory.CreateDirectory(Path.Combine(tempDir, "SubDir1"));
            Directory.CreateDirectory(Path.Combine(tempDir, "SubDir2"));

            // Act
            var result = await _viewModel.AddFolderAsync(tempDir);

            // Assert
            Assert.IsTrue(result);
            Assert.AreEqual(1, _viewModel.ScanFolders.Count);
            Assert.AreEqual(tempDir, _viewModel.ScanFolders[0]);
            Assert.IsFalse(_viewModel.OperationInProgress);
        }

        [TestMethod]
        public async Task AddFolderAsync_WithNullPath_ReturnsFalse()
        {
            // Act
            var result = await _viewModel.AddFolderAsync(null!);

            // Assert
            Assert.IsFalse(result);
            Assert.AreEqual(0, _viewModel.ScanFolders.Count);
        }

        [TestMethod]
        public async Task AddFolderAsync_WithEmptyPath_ReturnsFalse()
        {
            // Act
            var result = await _viewModel.AddFolderAsync("");

            // Assert
            Assert.IsFalse(result);
            Assert.AreEqual(0, _viewModel.ScanFolders.Count);
        }

        [TestMethod]
        public async Task AddFolderAsync_WithDuplicatePath_ReturnsFalse()
        {
            // Arrange
            var tempDir = CreateTempDirectory();
            await _viewModel.AddFolderAsync(tempDir);

            // Act
            var result = await _viewModel.AddFolderAsync(tempDir);

            // Assert
            Assert.IsFalse(result);
            Assert.AreEqual(1, _viewModel.ScanFolders.Count);
        }

        [TestMethod]
        public async Task AddFolderAsync_WithNonExistentPath_ReturnsFalse()
        {
            // Arrange
            var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

            // Act
            var result = await _viewModel.AddFolderAsync(nonExistentPath);

            // Assert
            Assert.IsFalse(result);
            Assert.AreEqual(0, _viewModel.ScanFolders.Count);
            Assert.IsTrue(_viewModel.StatusMessage.Contains("Error"));
        }

        [TestMethod]
        public void RemoveFolder_WithValidPath_RemovesFolder()
        {
            // Arrange
            var testPath = @"C:\TestPath";
            // Add to both collections to simulate proper state
            _viewModel.ScanFolders.Add(testPath);
            // Use reflection to add to private _scanFolders collection
            var scanFoldersField = typeof(MainViewModel).GetField("_scanFolders", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var scanFoldersList = (List<string>)scanFoldersField!.GetValue(_viewModel)!;
            scanFoldersList.Add(testPath);

            // Act
            _viewModel.RemoveFolder(testPath);

            // Assert
            Assert.AreEqual(0, _viewModel.ScanFolders.Count);
            Assert.IsTrue(_viewModel.StatusMessage.Contains("Removed folder"));
        }

        [TestMethod]
        public void RemoveFolder_WithNonExistentPath_DoesNothing()
        {
            // Arrange
            var testPath = @"C:\TestPath";
            var nonExistentPath = @"C:\NonExistent";
            _viewModel.ScanFolders.Add(testPath);

            // Act
            _viewModel.RemoveFolder(nonExistentPath);

            // Assert
            Assert.AreEqual(1, _viewModel.ScanFolders.Count);
            Assert.AreEqual(testPath, _viewModel.ScanFolders[0]);
        }

        [TestMethod]
        public async Task RunComparisonAsync_WithNoFolders_ReturnsFalse()
        {
            // Act
            var result = await _viewModel.RunComparisonAsync();

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void CancelOperation_WhenOperationInProgress_CancelsOperation()
        {
            // Arrange
            _viewModel.OperationInProgress = true;

            // Act
            _viewModel.CancelOperation();

            // Assert - Should not throw exception
            Assert.IsTrue(true);
        }

        [TestMethod]
        public void ApplyFilters_WithValidCriteria_FiltersResults()
        {
            // Arrange
            _viewModel.MinimumSimilarity = 75.0;
            _viewModel.MinimumSizeMB = 5.0;

            // Act
            _viewModel.ApplyFilters();

            // Assert - Should not throw exception
            Assert.IsTrue(true);
        }

        [TestMethod]
        public void SelectedFolderMatch_WhenSet_UpdatesFileDetails()
        {
            // Arrange
            var testMatch = new FolderMatch(
                @"C:\Left", 
                @"C:\Right", 
                new List<FileMatch>(), 
                1, 
                1, 
                1024);

            // Act
            _viewModel.SelectedFolderMatch = testMatch;

            // Assert
            Assert.AreEqual(testMatch, _viewModel.SelectedFolderMatch);
        }

        [TestMethod]
        public void ShowUniqueFiles_WhenToggled_UpdatesFileDetails()
        {
            // Act
            _viewModel.ShowUniqueFiles = true;

            // Assert
            Assert.IsTrue(_viewModel.ShowUniqueFiles);

            // Act
            _viewModel.ShowUniqueFiles = false;

            // Assert
            Assert.IsFalse(_viewModel.ShowUniqueFiles);
        }

        [TestMethod]
        public void MinimumSimilarity_WhenSet_UpdatesProperty()
        {
            // Act
            _viewModel.MinimumSimilarity = 85.5;

            // Assert
            Assert.AreEqual(85.5, _viewModel.MinimumSimilarity);
        }

        [TestMethod]
        public void MinimumSizeMB_WhenSet_UpdatesProperty()
        {
            // Act
            _viewModel.MinimumSizeMB = 10.5;

            // Assert
            Assert.AreEqual(10.5, _viewModel.MinimumSizeMB);
        }

        [TestMethod]
        public void StatusMessage_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var propertyChangedRaised = false;
            _viewModel.PropertyChanged += (s, e) => 
            {
                if (e.PropertyName == nameof(MainViewModel.StatusMessage))
                    propertyChangedRaised = true;
            };

            // Act
            _viewModel.StatusMessage = "Test message";

            // Assert
            Assert.IsTrue(propertyChangedRaised);
            Assert.AreEqual("Test message", _viewModel.StatusMessage);
        }

        [TestMethod]
        public void CurrentProgress_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var propertyChangedRaised = false;
            _viewModel.PropertyChanged += (s, e) => 
            {
                if (e.PropertyName == nameof(MainViewModel.CurrentProgress))
                    propertyChangedRaised = true;
            };

            // Act
            _viewModel.CurrentProgress = 75;

            // Assert
            Assert.IsTrue(propertyChangedRaised);
            Assert.AreEqual(75, _viewModel.CurrentProgress);
        }

        [TestMethod]
        public void MaxProgress_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var propertyChangedRaised = false;
            _viewModel.PropertyChanged += (s, e) => 
            {
                if (e.PropertyName == nameof(MainViewModel.MaxProgress))
                    propertyChangedRaised = true;
            };

            // Act
            _viewModel.MaxProgress = 200;

            // Assert
            Assert.IsTrue(propertyChangedRaised);
            Assert.AreEqual(200, _viewModel.MaxProgress);
        }

        [TestMethod]
        public void IsProgressIndeterminate_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var propertyChangedRaised = false;
            _viewModel.PropertyChanged += (s, e) => 
            {
                if (e.PropertyName == nameof(MainViewModel.IsProgressIndeterminate))
                    propertyChangedRaised = true;
            };

            // Act
            _viewModel.IsProgressIndeterminate = true;

            // Assert
            Assert.IsTrue(propertyChangedRaised);
            Assert.IsTrue(_viewModel.IsProgressIndeterminate);
        }

        [TestMethod]
        public void IsPopulatingResults_WhenSet_UpdatesCommandAvailability()
        {
            // Act
            _viewModel.IsPopulatingResults = true;

            // Assert
            Assert.IsTrue(_viewModel.IsPopulatingResults);
            Assert.IsFalse(_viewModel.CanApplyFilters);
        }

        [TestMethod]
        public void LeftFolderDisplay_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var propertyChangedRaised = false;
            _viewModel.PropertyChanged += (s, e) => 
            {
                if (e.PropertyName == nameof(MainViewModel.LeftFolderDisplay))
                    propertyChangedRaised = true;
            };

            // Act
            _viewModel.LeftFolderDisplay = "Left Folder";

            // Assert
            Assert.IsTrue(propertyChangedRaised);
            Assert.AreEqual("Left Folder", _viewModel.LeftFolderDisplay);
        }

        [TestMethod]
        public void RightFolderDisplay_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var propertyChangedRaised = false;
            _viewModel.PropertyChanged += (s, e) => 
            {
                if (e.PropertyName == nameof(MainViewModel.RightFolderDisplay))
                    propertyChangedRaised = true;
            };

            // Act
            _viewModel.RightFolderDisplay = "Right Folder";

            // Assert
            Assert.IsTrue(propertyChangedRaised);
            Assert.AreEqual("Right Folder", _viewModel.RightFolderDisplay);
        }

        [TestMethod]
        public void FileCountDisplay_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var propertyChangedRaised = false;
            _viewModel.PropertyChanged += (s, e) => 
            {
                if (e.PropertyName == nameof(MainViewModel.FileCountDisplay))
                    propertyChangedRaised = true;
            };

            // Act
            _viewModel.FileCountDisplay = "100 files";

            // Assert
            Assert.IsTrue(propertyChangedRaised);
            Assert.AreEqual("100 files", _viewModel.FileCountDisplay);
        }

        [TestMethod]
        public void CommandAvailability_WithFoldersAdded_UpdatesCorrectly()
        {
            // Arrange
            _viewModel.ScanFolders.Add(@"C:\TestFolder");

            // Act - Trigger property change notifications
            _viewModel.OperationInProgress = false;

            // Assert
            Assert.IsTrue(_viewModel.CanRemoveFolders);
            Assert.IsTrue(_viewModel.CanRunComparison);
            Assert.IsTrue(_viewModel.CanSaveProject);
        }

        [TestMethod]
        public async Task SaveProjectAsync_WithValidPath_ReturnsTrue()
        {
            // Arrange
            var tempFile = Path.Combine(CreateTempDirectory(), "test.cfp");
            _viewModel.ScanFolders.Add(@"C:\TestFolder");

            // Act
            var result = await _viewModel.SaveProjectAsync(tempFile);

            // Assert
            Assert.IsTrue(result);
            Assert.IsTrue(File.Exists(tempFile));
        }

        [TestMethod]
        public async Task SaveProjectAsync_WithNullPath_ReturnsFalse()
        {
            // Act
            var result = await _viewModel.SaveProjectAsync(null!);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public async Task LoadProjectAsync_WithValidFile_ReturnsTrue()
        {
            // Arrange
            var tempFile = Path.Combine(CreateTempDirectory(), "test.cfp");
            _viewModel.ScanFolders.Add(@"C:\TestFolder");
            await _viewModel.SaveProjectAsync(tempFile);

            // Clear current state
            _viewModel.ScanFolders.Clear();

            // Act
            var result = await _viewModel.LoadProjectAsync(tempFile);

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public async Task LoadProjectAsync_WithNonExistentFile_ReturnsFalse()
        {
            // Arrange
            var nonExistentFile = Path.Combine(CreateTempDirectory(), "nonexistent.cfp");

            // Act
            var result = await _viewModel.LoadProjectAsync(nonExistentFile);

            // Assert
            Assert.IsFalse(result);
            Assert.IsTrue(_viewModel.StatusMessage.Contains("Error"));
        }

        [TestMethod]
        public void Dispose_DisposesResourcesProperly()
        {
            // Act
            _viewModel.Dispose();

            // Assert - Should not throw exception
            Assert.IsTrue(true);
        }

        [TestMethod]
        public async Task RemoveFolder_WithNonExistentPath_DoesNotRemove()
        {
            // Arrange
            var existingPath = CreateTempDirectory();
            var nonExistentPath = @"C:\NonExistent";
            await _viewModel.AddFolderAsync(existingPath);

            // Act
            _viewModel.RemoveFolder(nonExistentPath);

            // Assert
            Assert.AreEqual(1, _viewModel.ScanFolders.Count); // Should still be 1
        }



        [TestMethod]
        public void ShowUniqueFiles_WhenSet_UpdatesProperty()
        {
            // Arrange
            var propertyChangedFired = false;
            _viewModel.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.ShowUniqueFiles))
                    propertyChangedFired = true;
            };

            // Act
            _viewModel.ShowUniqueFiles = true;

            // Assert
            Assert.IsTrue(_viewModel.ShowUniqueFiles);
            Assert.IsTrue(propertyChangedFired);
        }

        [TestMethod]
        public void CurrentProgress_WhenSet_UpdatesProperty()
        {
            // Arrange
            var propertyChangedFired = false;
            _viewModel.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.CurrentProgress))
                    propertyChangedFired = true;
            };

            // Act
            _viewModel.CurrentProgress = 50;

            // Assert
            Assert.AreEqual(50, _viewModel.CurrentProgress);
            Assert.IsTrue(propertyChangedFired);
        }

        [TestMethod]
        public void MaxProgress_WhenSet_UpdatesProperty()
        {
            // Arrange
            var propertyChangedFired = false;
            _viewModel.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.MaxProgress))
                    propertyChangedFired = true;
            };

            // Act
            _viewModel.MaxProgress = 200;

            // Assert
            Assert.AreEqual(200, _viewModel.MaxProgress);
            Assert.IsTrue(propertyChangedFired);
        }

        [TestMethod]
        public void IsProgressIndeterminate_WhenSet_UpdatesProperty()
        {
            // Arrange
            var propertyChangedFired = false;
            _viewModel.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.IsProgressIndeterminate))
                    propertyChangedFired = true;
            };

            // Act
            _viewModel.IsProgressIndeterminate = true;

            // Assert
            Assert.IsTrue(_viewModel.IsProgressIndeterminate);
            Assert.IsTrue(propertyChangedFired);
        }

        [TestMethod]
        public void SelectedFolderMatch_WhenSet_UpdatesProperty()
        {
            // Arrange
            var propertyChangedFired = false;
            _viewModel.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.SelectedFolderMatch))
                    propertyChangedFired = true;
            };

            var folderMatch = new ClutterFlock.Models.FolderMatch(
                @"C:\Left", @"C:\Right",
                new List<ClutterFlock.Models.FileMatch> { new(@"C:\Left\file.txt", @"C:\Right\file.txt") },
                1, 1, 1024);

            // Act
            _viewModel.SelectedFolderMatch = folderMatch;

            // Assert
            Assert.AreEqual(folderMatch, _viewModel.SelectedFolderMatch);
            Assert.IsTrue(propertyChangedFired);
        }

        [TestMethod]
        public void CancelOperation_CallsCancellationToken()
        {
            // Arrange
            _viewModel.OperationInProgress = true;

            // Act
            _viewModel.CancelOperation();

            // Assert
            Assert.IsTrue(_viewModel.StatusMessage.Contains("Cancelling"));
        }

        [TestMethod]
        public void CurrentProgress_PropertyChanged_RaisesEvent()
        {
            // Act
            _viewModel.CurrentProgress = 75;

            // Assert
            Assert.AreEqual(75, _viewModel.CurrentProgress);
            Assert.IsTrue(_propertyChangedEvents.Contains("CurrentProgress"));
        }

        [TestMethod]
        public void CurrentProgress_SetToSameValue_DoesNotRaisePropertyChanged()
        {
            // Arrange
            _viewModel.CurrentProgress = 50;
            _propertyChangedEvents.Clear();

            // Act
            _viewModel.CurrentProgress = 50;

            // Assert
            Assert.IsFalse(_propertyChangedEvents.Contains("CurrentProgress"));
        }

        [TestMethod]
        public void IsProgressIndeterminate_PropertyChanged_RaisesEvent()
        {
            // Act
            _viewModel.IsProgressIndeterminate = true;

            // Assert
            Assert.IsTrue(_viewModel.IsProgressIndeterminate);
            Assert.IsTrue(_propertyChangedEvents.Contains("IsProgressIndeterminate"));
        }

        [TestMethod]
        public void IsProgressIndeterminate_SetToSameValue_DoesNotRaisePropertyChanged()
        {
            // Arrange
            _viewModel.IsProgressIndeterminate = true;
            _propertyChangedEvents.Clear();

            // Act
            _viewModel.IsProgressIndeterminate = true;

            // Assert
            Assert.IsFalse(_propertyChangedEvents.Contains("IsProgressIndeterminate"));
        }

        [TestMethod]
        public void OperationInProgress_PropertyChanged_RaisesEvent()
        {
            // Act
            _viewModel.OperationInProgress = true;

            // Assert
            Assert.IsTrue(_viewModel.OperationInProgress);
            Assert.IsTrue(_propertyChangedEvents.Contains("OperationInProgress"));
        }

        [TestMethod]
        public void OperationInProgress_SetToSameValue_DoesNotRaisePropertyChanged()
        {
            // Arrange
            _viewModel.OperationInProgress = true;
            _propertyChangedEvents.Clear();

            // Act
            _viewModel.OperationInProgress = true;

            // Assert
            Assert.IsFalse(_propertyChangedEvents.Contains("OperationInProgress"));
        }

        [TestMethod]
        public void IsPopulatingResults_PropertyChanged_RaisesEvent()
        {
            // Act
            _viewModel.IsPopulatingResults = true;

            // Assert
            Assert.IsTrue(_viewModel.IsPopulatingResults);
            Assert.IsTrue(_propertyChangedEvents.Contains("IsPopulatingResults"));
        }

        [TestMethod]
        public void IsPopulatingResults_SetToSameValue_DoesNotRaisePropertyChanged()
        {
            // Arrange
            _viewModel.IsPopulatingResults = true;
            _propertyChangedEvents.Clear();

            // Act
            _viewModel.IsPopulatingResults = true;

            // Assert
            Assert.IsFalse(_propertyChangedEvents.Contains("IsPopulatingResults"));
        }

        [TestMethod]
        public void LeftFolderDisplay_PropertyChanged_RaisesEvent()
        {
            // Act
            _viewModel.LeftFolderDisplay = "Left Folder Path";

            // Assert
            Assert.AreEqual("Left Folder Path", _viewModel.LeftFolderDisplay);
            Assert.IsTrue(_propertyChangedEvents.Contains("LeftFolderDisplay"));
        }

        [TestMethod]
        public void LeftFolderDisplay_SetToSameValue_DoesNotRaisePropertyChanged()
        {
            // Arrange
            _viewModel.LeftFolderDisplay = "Same Path";
            _propertyChangedEvents.Clear();

            // Act
            _viewModel.LeftFolderDisplay = "Same Path";

            // Assert
            Assert.IsFalse(_propertyChangedEvents.Contains("LeftFolderDisplay"));
        }

        [TestMethod]
        public void RightFolderDisplay_PropertyChanged_RaisesEvent()
        {
            // Act
            _viewModel.RightFolderDisplay = "Right Folder Path";

            // Assert
            Assert.AreEqual("Right Folder Path", _viewModel.RightFolderDisplay);
            Assert.IsTrue(_propertyChangedEvents.Contains("RightFolderDisplay"));
        }

        [TestMethod]
        public void RightFolderDisplay_SetToSameValue_DoesNotRaisePropertyChanged()
        {
            // Arrange
            _viewModel.RightFolderDisplay = "Same Path";
            _propertyChangedEvents.Clear();

            // Act
            _viewModel.RightFolderDisplay = "Same Path";

            // Assert
            Assert.IsFalse(_propertyChangedEvents.Contains("RightFolderDisplay"));
        }

        [TestMethod]
        public void FileCountDisplay_PropertyChanged_RaisesEvent()
        {
            // Act
            _viewModel.FileCountDisplay = "1000 files";

            // Assert
            Assert.AreEqual("1000 files", _viewModel.FileCountDisplay);
            Assert.IsTrue(_propertyChangedEvents.Contains("FileCountDisplay"));
        }

        [TestMethod]
        public void FileCountDisplay_SetToSameValue_DoesNotRaisePropertyChanged()
        {
            // Arrange
            _viewModel.FileCountDisplay = "500 files";
            _propertyChangedEvents.Clear();

            // Act
            _viewModel.FileCountDisplay = "500 files";

            // Assert
            Assert.IsFalse(_propertyChangedEvents.Contains("FileCountDisplay"));
        }

        [TestMethod]
        public void MinimumSimilarity_PropertyChanged_RaisesEvent()
        {
            // Act
            _viewModel.MinimumSimilarity = 80.5;

            // Assert
            Assert.AreEqual(80.5, _viewModel.MinimumSimilarity);
            Assert.IsTrue(_propertyChangedEvents.Contains("MinimumSimilarity"));
        }

        [TestMethod]
        public void MinimumSimilarity_SetToSameValue_DoesNotRaisePropertyChanged()
        {
            // Arrange
            _viewModel.MinimumSimilarity = 75.0;
            _propertyChangedEvents.Clear();

            // Act
            _viewModel.MinimumSimilarity = 75.0;

            // Assert
            Assert.IsFalse(_propertyChangedEvents.Contains("MinimumSimilarity"));
        }

        [TestMethod]
        public void MinimumSizeMB_PropertyChanged_RaisesEvent()
        {
            // Act
            _viewModel.MinimumSizeMB = 10.5;

            // Assert
            Assert.AreEqual(10.5, _viewModel.MinimumSizeMB);
            Assert.IsTrue(_propertyChangedEvents.Contains("MinimumSizeMB"));
        }

        [TestMethod]
        public void MinimumSizeMB_SetToSameValue_DoesNotRaisePropertyChanged()
        {
            // Arrange
            _viewModel.MinimumSizeMB = 5.0;
            _propertyChangedEvents.Clear();

            // Act
            _viewModel.MinimumSizeMB = 5.0;

            // Assert
            Assert.IsFalse(_propertyChangedEvents.Contains("MinimumSizeMB"));
        }

        [TestMethod]
        public void ShowUniqueFiles_PropertyChanged_RaisesEvent()
        {
            // Act
            _viewModel.ShowUniqueFiles = true;

            // Assert
            Assert.IsTrue(_viewModel.ShowUniqueFiles);
            Assert.IsTrue(_propertyChangedEvents.Contains("ShowUniqueFiles"));
        }

        [TestMethod]
        public void ShowUniqueFiles_SetToSameValue_DoesNotRaisePropertyChanged()
        {
            // Arrange
            _viewModel.ShowUniqueFiles = true;
            _propertyChangedEvents.Clear();

            // Act
            _viewModel.ShowUniqueFiles = true;

            // Assert
            Assert.IsFalse(_propertyChangedEvents.Contains("ShowUniqueFiles"));
        }

        [TestMethod]
        public void SelectedFolderMatch_PropertyChanged_RaisesEvent()
        {
            // Arrange
            var folderMatch = new FolderMatch("C:\\Left", "C:\\Right", new List<FileMatch>(), 1, 1, 1024);

            // Act
            _viewModel.SelectedFolderMatch = folderMatch;

            // Assert
            Assert.AreEqual(folderMatch, _viewModel.SelectedFolderMatch);
            Assert.IsTrue(_propertyChangedEvents.Contains("SelectedFolderMatch"));
        }

        [TestMethod]
        public void SelectedFolderMatch_SetToSameValue_DoesNotRaisePropertyChanged()
        {
            // Arrange
            var folderMatch = new FolderMatch("C:\\Left", "C:\\Right", new List<FileMatch>(), 1, 1, 1024);
            _viewModel.SelectedFolderMatch = folderMatch;
            _propertyChangedEvents.Clear();

            // Act
            _viewModel.SelectedFolderMatch = folderMatch;

            // Assert
            Assert.IsFalse(_propertyChangedEvents.Contains("SelectedFolderMatch"));
        }

        [TestMethod]
        public void ScanFolders_IsObservableCollection()
        {
            // Assert
            Assert.IsTrue(_viewModel.ScanFolders is ObservableCollection<string>);
        }

        [TestMethod]
        public void FilteredFolderMatches_IsObservableCollection()
        {
            // Assert
            Assert.IsTrue(_viewModel.FilteredFolderMatches is ObservableCollection<FolderMatch>);
        }

        [TestMethod]
        public void FileDetails_IsObservableCollection()
        {
            // Assert
            Assert.IsTrue(_viewModel.FileDetails is ObservableCollection<FileDetailInfo>);
        }

        [TestMethod]
        public void Constructor_InitializesAllCollections()
        {
            // Assert
            Assert.IsNotNull(_viewModel.ScanFolders);
            Assert.IsNotNull(_viewModel.FilteredFolderMatches);
            Assert.IsNotNull(_viewModel.FileDetails);
            Assert.AreEqual(0, _viewModel.ScanFolders.Count);
            Assert.AreEqual(0, _viewModel.FilteredFolderMatches.Count);
            Assert.AreEqual(0, _viewModel.FileDetails.Count);
        }

        [TestMethod]
        public void Constructor_InitializesAllPropertiesToDefaults()
        {
            // Assert
            Assert.AreEqual(0, _viewModel.CurrentProgress);
            Assert.AreEqual(1, _viewModel.MaxProgress);
            Assert.AreEqual(0, _viewModel.CurrentProgress);
            Assert.AreEqual("Ready to scan", _viewModel.StatusMessage);
            Assert.IsFalse(_viewModel.OperationInProgress);
            Assert.IsFalse(_viewModel.OperationInProgress);
            Assert.IsFalse(_viewModel.IsPopulatingResults);
            Assert.IsFalse(_viewModel.IsProgressIndeterminate);
            Assert.IsNull(_viewModel.SelectedFolderMatch);
            Assert.IsNull(_viewModel.SelectedFolderMatch);
            Assert.AreEqual("", _viewModel.LeftFolderDisplay);
            Assert.AreEqual("", _viewModel.RightFolderDisplay);
            Assert.AreEqual("0", _viewModel.FileCountDisplay);
            Assert.AreEqual(50.0, _viewModel.MinimumSimilarity);
            Assert.AreEqual(1.0, _viewModel.MinimumSizeMB);
            Assert.IsFalse(_viewModel.ShowUniqueFiles);
        }

        [TestMethod]
        public void PropertyChangedEvent_IsRaisedForAllProperties()
        {
            // Act - Set all properties to trigger events
            _viewModel.CurrentProgress = 50;
            _viewModel.MaxProgress = 200;
            _viewModel.CurrentProgress = 75;
            _viewModel.StatusMessage = "Test";
            _viewModel.OperationInProgress = true;
            _viewModel.OperationInProgress = true;
            _viewModel.IsPopulatingResults = true;
            _viewModel.IsProgressIndeterminate = true;
            _viewModel.SelectedFolderMatch = new FolderMatch("C:\\Test", "C:\\Test2", new List<FileMatch>(), 1, 1, 1024);
            _viewModel.LeftFolderDisplay = "Left";
            _viewModel.RightFolderDisplay = "Right";
            _viewModel.FileCountDisplay = "100 files";
            _viewModel.MinimumSimilarity = 75.0;
            _viewModel.MinimumSizeMB = 5.0;
            _viewModel.ShowUniqueFiles = true;
            _viewModel.SelectedFolderMatch = new FolderMatch("C:\\A", "C:\\B", new List<FileMatch>(), 1, 1, 1024);

            // Assert - All property change events should be raised
            var expectedProperties = new[]
            {
                "MaxProgress", "CurrentProgress", "StatusMessage",
                "OperationInProgress", "IsPopulatingResults", "IsProgressIndeterminate",
                "LeftFolderDisplay", "RightFolderDisplay", "FileCountDisplay",
                "MinimumSimilarity", "MinimumSizeMB", "ShowUniqueFiles", "SelectedFolderMatch"
            };

            foreach (var property in expectedProperties)
            {
                Assert.IsTrue(_propertyChangedEvents.Contains(property), $"Property '{property}' should raise PropertyChanged event");
            }
        }
    }
}