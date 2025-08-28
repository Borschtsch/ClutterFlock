using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ClutterFlock.ViewModels;
using ClutterFlock.Models;
using ClutterFlock.Tests.TestUtilities;
using System.Collections.ObjectModel;
using System.Linq;
using System.Collections.Generic;

namespace ClutterFlock.Tests.Unit.UI
{
    [TestClass]
    [TestCategory("Unit")]
    public class MainWindowTests : TestBase
    {
        private MainWindow? _mainWindow;
        private MainViewModel? _testViewModel;

        [TestInitialize]
        public override void TestInitialize()
        {
            base.TestInitialize();
        }

        [TestCleanup]
        public override void TestCleanup()
        {
            _testViewModel?.Dispose();
            _testViewModel = null;
            
            if (_mainWindow != null)
            {
                try
                {
                    if (_mainWindow.IsLoaded)
                    {
                        _mainWindow.Close();
                    }
                }
                catch
                {
                    // Ignore cleanup errors in test environment
                }
                
                _mainWindow = null;
            }
            
            base.TestCleanup();
        }

        [STATestMethod]
        public void MainWindow_Type_InheritsFromWindow()
        {
            // Act - Test the type hierarchy without creating an instance
            var windowType = typeof(MainWindow);

            // Assert
            Assert.IsTrue(windowType.IsSubclassOf(typeof(Window)));
            Assert.IsTrue(typeof(Window).IsAssignableFrom(windowType));
        }

        [STATestMethod]
        public void MainWindow_Type_HasCorrectNamespace()
        {
            // Act
            var windowType = typeof(MainWindow);

            // Assert
            Assert.AreEqual("ClutterFlock", windowType.Namespace);
            Assert.AreEqual("MainWindow", windowType.Name);
        }

        [STATestMethod]
        public void ViewModel_PropertyChanges_WorkCorrectly()
        {
            // Arrange - Create ViewModel directly without UI
            _testViewModel = new MainViewModel();

            // Act
            _testViewModel.StatusMessage = "Test Status";
            _testViewModel.CurrentProgress = 50;
            _testViewModel.MaxProgress = 100;
            _testViewModel.IsProgressIndeterminate = true;

            // Assert
            Assert.AreEqual("Test Status", _testViewModel.StatusMessage);
            Assert.AreEqual(50, _testViewModel.CurrentProgress);
            Assert.AreEqual(100, _testViewModel.MaxProgress);
            Assert.IsTrue(_testViewModel.IsProgressIndeterminate);
        }

        [STATestMethod]
        public void ViewModel_FolderDisplayProperties_WorkCorrectly()
        {
            // Arrange - Create ViewModel directly without UI
            _testViewModel = new MainViewModel();

            // Act
            _testViewModel.LeftFolderDisplay = "Left Folder";
            _testViewModel.RightFolderDisplay = "Right Folder";
            _testViewModel.FileCountDisplay = "100 files";

            // Assert
            Assert.AreEqual("Left Folder", _testViewModel.LeftFolderDisplay);
            Assert.AreEqual("Right Folder", _testViewModel.RightFolderDisplay);
            Assert.AreEqual("100 files", _testViewModel.FileCountDisplay);
        }

        [STATestMethod]
        public async Task ViewModel_FolderOperations_WorkCorrectly()
        {
            // Arrange - Create ViewModel directly without UI
            _testViewModel = new MainViewModel();
            
            // Create a temporary directory for testing
            var tempDir = CreateTempDirectory();

            // Act - Add a folder to enable comparison using the proper async method
            await _testViewModel.AddFolderAsync(tempDir);

            // Assert - Test ViewModel state directly
            Assert.IsTrue(_testViewModel.CanRunComparison);
            Assert.AreEqual(1, _testViewModel.ScanFolders.Count);
            Assert.IsTrue(_testViewModel.ScanFolders.Contains(tempDir));
        }

        [STATestMethod]
        public async Task ViewModel_FolderRemoval_WorksCorrectly()
        {
            // Arrange - Create ViewModel directly without UI
            _testViewModel = new MainViewModel();
            
            // Create a temporary directory for testing
            var tempDir = CreateTempDirectory();
            
            // Add folder properly using the async method
            await _testViewModel.AddFolderAsync(tempDir);

            // Act
            _testViewModel.RemoveFolder(tempDir);

            // Assert
            Assert.IsFalse(_testViewModel.ScanFolders.Contains(tempDir));
            Assert.AreEqual(0, _testViewModel.ScanFolders.Count);
        }

        [STATestMethod]
        public void ViewModel_CancelOperation_WorksCorrectly()
        {
            // Arrange - Create ViewModel directly without UI
            _testViewModel = new MainViewModel();

            // Act - Test the cancel operation
            _testViewModel.CancelOperation();

            // Assert - Should not throw exception and operation should be cancelled
            Assert.IsFalse(_testViewModel.OperationInProgress);
        }

        [STATestMethod]
        public void ViewModel_SelectedFolderMatch_WorksCorrectly()
        {
            // Arrange - Create ViewModel directly without UI
            _testViewModel = new MainViewModel();
            var testMatch = new FolderMatch("C:\\Test1", "C:\\Test2", new List<FileMatch>(), 1, 1, 1024);

            // Act
            _testViewModel.SelectedFolderMatch = testMatch;

            // Assert
            Assert.AreEqual(testMatch, _testViewModel.SelectedFolderMatch);
        }

        [STATestMethod]
        public void ViewModel_FilterProperties_WorkCorrectly()
        {
            // Arrange - Create ViewModel directly without UI
            _testViewModel = new MainViewModel();

            // Act
            _testViewModel.MinimumSimilarity = 75.5;
            _testViewModel.MinimumSizeMB = 10.5;

            // Assert
            Assert.AreEqual(75.5, _testViewModel.MinimumSimilarity, 0.1);
            Assert.AreEqual(10.5, _testViewModel.MinimumSizeMB, 0.1);
        }

        [STATestMethod]
        public void ViewModel_ShowUniqueFiles_WorksCorrectly()
        {
            // Arrange - Create ViewModel directly without UI
            _testViewModel = new MainViewModel();

            // Act
            _testViewModel.ShowUniqueFiles = true;

            // Assert
            Assert.IsTrue(_testViewModel.ShowUniqueFiles);
        }

        [STATestMethod]
        public void ViewModel_Collections_InitializeCorrectly()
        {
            // Arrange - Create ViewModel directly without UI
            _testViewModel = new MainViewModel();

            // Assert - Test that collections are properly initialized
            Assert.IsNotNull(_testViewModel.ScanFolders);
            Assert.IsNotNull(_testViewModel.FilteredFolderMatches);
            Assert.IsNotNull(_testViewModel.FileDetails);
            Assert.AreEqual(0, _testViewModel.ScanFolders.Count);
            Assert.AreEqual(0, _testViewModel.FilteredFolderMatches.Count);
            Assert.AreEqual(0, _testViewModel.FileDetails.Count);
        }

        [STATestMethod]
        public void ViewModel_InitialState_IsCorrect()
        {
            // Arrange - Create ViewModel directly without UI
            _testViewModel = new MainViewModel();

            // Assert - Test initial state
            Assert.AreEqual("Ready to scan", _testViewModel.StatusMessage);
            Assert.AreEqual(0, _testViewModel.CurrentProgress);
            Assert.AreEqual(1, _testViewModel.MaxProgress);
            Assert.IsFalse(_testViewModel.OperationInProgress);
            Assert.IsFalse(_testViewModel.IsProgressIndeterminate);
            Assert.IsFalse(_testViewModel.ShowUniqueFiles);
        }
    }
}