using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        [TestInitialize]
        public override void TestInitialize()
        {
            base.TestInitialize();
            _viewModel = new MainViewModel();
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
            Assert.AreEqual(1, _viewModel.ScanFolders.Count); // Should still be 1
        }

        [TestMethod]
        public async Task AddFolderAsync_WithNonExistentFolder_HandlesGracefully()
        {
            // Arrange
            var nonExistentPath = Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid());

            // Act
            var result = await _viewModel.AddFolderAsync(nonExistentPath);

            // Assert
            // The important thing is that it doesn't crash and completes the operation
            // The FolderScanner handles non-existent paths gracefully
            Assert.IsNotNull(_viewModel.StatusMessage);
            Assert.IsFalse(_viewModel.OperationInProgress); // Should complete the operation
        }

        [TestMethod]
        public async Task RemoveFolder_WithExistingFolder_RemovesFolder()
        {
            // Arrange
            var tempDir = CreateTempDirectory();
            await _viewModel.AddFolderAsync(tempDir); // Use proper method to add folder

            // Act
            _viewModel.RemoveFolder(tempDir);

            // Assert
            Assert.AreEqual(0, _viewModel.ScanFolders.Count);
            Assert.IsTrue(_viewModel.StatusMessage.Contains("Removed folder"));
        }

        [TestMethod]
        public async Task RemoveFolder_WithNonExistentFolder_DoesNothing()
        {
            // Arrange
            var tempDir = CreateTempDirectory();
            var nonExistentPath = Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid());
            await _viewModel.AddFolderAsync(tempDir); // Use proper method to add folder

            // Act
            _viewModel.RemoveFolder(nonExistentPath);

            // Assert
            Assert.AreEqual(1, _viewModel.ScanFolders.Count); // Should still be 1
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
        public void PropertyChanged_WhenPropertySet_FiresEvent()
        {
            // Arrange
            var propertyChangedFired = false;
            string? changedPropertyName = null;
            _viewModel.PropertyChanged += (sender, e) =>
            {
                propertyChangedFired = true;
                changedPropertyName = e.PropertyName;
            };

            // Act
            _viewModel.StatusMessage = "Test Message";

            // Assert
            Assert.IsTrue(propertyChangedFired);
            Assert.AreEqual(nameof(_viewModel.StatusMessage), changedPropertyName);
        }

        [TestMethod]
        public void MinimumSimilarity_WhenSet_UpdatesProperty()
        {
            // Arrange
            var propertyChangedFired = false;
            _viewModel.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.MinimumSimilarity))
                    propertyChangedFired = true;
            };

            // Act
            _viewModel.MinimumSimilarity = 75.0;

            // Assert
            Assert.AreEqual(75.0, _viewModel.MinimumSimilarity);
            Assert.IsTrue(propertyChangedFired);
        }

        [TestMethod]
        public void MinimumSizeMB_WhenSet_UpdatesProperty()
        {
            // Arrange
            var propertyChangedFired = false;
            _viewModel.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.MinimumSizeMB))
                    propertyChangedFired = true;
            };

            // Act
            _viewModel.MinimumSizeMB = 5.0;

            // Assert
            Assert.AreEqual(5.0, _viewModel.MinimumSizeMB);
            Assert.IsTrue(propertyChangedFired);
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
        public async Task SaveProjectAsync_WithValidPath_ReturnsTrue()
        {
            // Arrange
            var tempFile = Path.Combine(CreateTempDirectory(), "test.cfp");
            var tempDir = CreateTempDirectory();
            await _viewModel.AddFolderAsync(tempDir);

            // Act
            var result = await _viewModel.SaveProjectAsync(tempFile);

            // Assert
            Assert.IsTrue(result);
            Assert.IsTrue(File.Exists(tempFile));
            Assert.IsTrue(_viewModel.StatusMessage.Contains("saved successfully"));
        }

        [TestMethod]
        public async Task SaveProjectAsync_WithInvalidPath_ReturnsFalse()
        {
            // Arrange
            var invalidPath = @"Z:\NonExistent\Path\test.cfp";
            var tempDir = CreateTempDirectory();
            await _viewModel.AddFolderAsync(tempDir);

            // Act
            var result = await _viewModel.SaveProjectAsync(invalidPath);

            // Assert
            Assert.IsFalse(result);
            Assert.IsTrue(_viewModel.StatusMessage.Contains("Error saving"));
        }

        [TestMethod]
        public async Task LoadProjectAsync_WithValidFile_ReturnsTrue()
        {
            // Arrange
            var tempFile = Path.Combine(CreateTempDirectory(), "test.cfp");
            var tempDir = CreateTempDirectory();
            
            // First save a project
            await _viewModel.AddFolderAsync(tempDir);
            await _viewModel.SaveProjectAsync(tempFile);
            
            // Clear the current state
            _viewModel.RemoveFolder(tempDir);

            // Act
            var result = await _viewModel.LoadProjectAsync(tempFile);

            // Assert
            Assert.IsTrue(result);
            Assert.AreEqual(1, _viewModel.ScanFolders.Count);
            Assert.IsTrue(_viewModel.StatusMessage.Contains("loaded successfully"));
        }

        [TestMethod]
        public async Task LoadProjectAsync_WithNonExistentFile_ReturnsFalse()
        {
            // Arrange
            var nonExistentFile = Path.Combine(Path.GetTempPath(), "nonexistent_" + Guid.NewGuid() + ".cfp");

            // Act
            var result = await _viewModel.LoadProjectAsync(nonExistentFile);

            // Assert
            Assert.IsFalse(result);
            Assert.IsTrue(_viewModel.StatusMessage.Contains("Error loading"));
        }

        [TestMethod]
        public void Dispose_CleansUpResources()
        {
            // Arrange
            var viewModel = new MainViewModel();

            // Act
            viewModel.Dispose();

            // Assert - Should not throw exceptions
            // The main thing we're testing is that Dispose doesn't crash
            Assert.IsTrue(true); // If we get here, Dispose worked
        }

        [TestMethod]
        public async Task CommandAvailability_AfterAddingFolder_UpdatesCorrectly()
        {
            // Arrange
            var tempDir = CreateTempDirectory();

            // Act
            await _viewModel.AddFolderAsync(tempDir); // Use proper method

            // Assert
            Assert.IsTrue(_viewModel.CanRemoveFolders);
            Assert.IsTrue(_viewModel.CanRunComparison);
            Assert.IsTrue(_viewModel.CanSaveProject);
        }

        [TestMethod]
        public async Task IsPopulatingResults_WhenSet_UpdatesCommandAvailability()
        {
            // Arrange
            await _viewModel.AddFolderAsync(CreateTempDirectory()); // Use proper method

            // Act
            _viewModel.IsPopulatingResults = true;

            // Assert
            Assert.IsFalse(_viewModel.CanApplyFilters);

            // Act
            _viewModel.IsPopulatingResults = false;

            // Assert - Still false because no matches exist
            Assert.IsFalse(_viewModel.CanApplyFilters);
        }

        [TestMethod]
        public void SetProperty_WithSameValue_DoesNotFirePropertyChanged()
        {
            // Arrange
            var propertyChangedCount = 0;
            _viewModel.PropertyChanged += (sender, e) => propertyChangedCount++;
            
            var initialMessage = _viewModel.StatusMessage;

            // Act
            _viewModel.StatusMessage = initialMessage; // Set to same value

            // Assert
            Assert.AreEqual(0, propertyChangedCount);
        }

        [TestMethod]
        public void SetProperty_WithDifferentValue_FiresPropertyChanged()
        {
            // Arrange
            var propertyChangedCount = 0;
            _viewModel.PropertyChanged += (sender, e) => propertyChangedCount++;

            // Act
            _viewModel.StatusMessage = "New Message";

            // Assert
            Assert.AreEqual(1, propertyChangedCount);
        }
    }
}