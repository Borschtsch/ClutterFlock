using Microsoft.VisualStudio.TestTools.UnitTesting;
using ClutterFlock.Core;
using ClutterFlock.ViewModels;

namespace ClutterFlock.Tests
{
    [TestClass]
    public class BasicTests
    {
        [TestMethod]
        public void CacheManager_CanBeInstantiated()
        {
            // Act
            var cacheManager = new CacheManager();
            
            // Assert
            Assert.IsNotNull(cacheManager);
        }

        [TestMethod]
        public void MainViewModel_CanBeInstantiated()
        {
            // Act
            var viewModel = new MainViewModel();
            
            // Assert
            Assert.IsNotNull(viewModel);
            
            // Cleanup
            viewModel.Dispose();
        }
    }
}