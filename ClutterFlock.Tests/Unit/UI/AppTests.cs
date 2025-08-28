using System.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ClutterFlock.Tests.TestUtilities;

namespace ClutterFlock.Tests.Unit.UI
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class AppTests : TestBase
    {
        [STATestMethod]
        public void App_Type_InheritsFromApplication()
        {
            // Act - Test the type hierarchy without creating an instance
            var appType = typeof(App);

            // Assert
            Assert.IsTrue(appType.IsSubclassOf(typeof(Application)));
            Assert.IsTrue(typeof(Application).IsAssignableFrom(appType));
        }

        [STATestMethod]
        public void App_Type_HasCorrectNamespace()
        {
            // Act
            var appType = typeof(App);

            // Assert
            Assert.AreEqual("ClutterFlock", appType.Namespace);
            Assert.AreEqual("App", appType.Name);
        }
    }
}