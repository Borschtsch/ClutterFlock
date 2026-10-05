using ClutterFlock.Core;

namespace ClutterFlock.Tests.Integration;

[TestClass]
[TestCategory("Integration")]
public sealed class FileMergeTests
{
    private string _root = null!;
    private string A => Path.Combine(_root, "a");
    private string B => Path.Combine(_root, "b");
    private readonly FolderOperations _operations = new();
    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "ClutterFlockFileMerge", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(A); Directory.CreateDirectory(B);
        File.WriteAllText(Path.Combine(A, "untouched"), "A"); File.WriteAllText(Path.Combine(B, "untouched"), "B");
    }
    [TestCleanup]
    public void Cleanup()
    {
        Assert.StartsWith(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ClutterFlockFileMerge")) + Path.DirectorySeparatorChar, Path.GetFullPath(_root));
        Directory.Delete(_root, true);
    }

    [TestMethod]
    [DataRow(true, false)] [DataRow(false, false)] [DataRow(true, true)] [DataRow(false, true)]
    public async Task Merge_ChangesOnlySelectedFile_PreservesOtherFilesAndBothFolders(bool toA, bool destinationExists)
    {
        var source = Path.Combine(toA ? B : A, "image.png"); var target = Path.Combine(toA ? A : B, "image.png");
        File.WriteAllText(source, "content"); if (destinationExists) File.WriteAllText(target, "content");
        var plan = await _operations.PrepareFileMergeAsync(source, target);
        Assert.IsTrue(File.Exists(source)); Assert.AreEqual(destinationExists, plan.DestinationExists);
        await _operations.ExecuteFileMergeAsync(plan);
        Assert.IsFalse(File.Exists(source)); Assert.AreEqual("content", File.ReadAllText(target));
        Assert.AreEqual("A", File.ReadAllText(Path.Combine(A, "untouched"))); Assert.AreEqual("B", File.ReadAllText(Path.Combine(B, "untouched")));
        Assert.IsTrue(Directory.Exists(A)); Assert.IsTrue(Directory.Exists(B));
    }

    [TestMethod]
    public async Task DifferentContents_BlockWithoutOverwriting()
    {
        var source = Path.Combine(A, "image.png"); var target = Path.Combine(B, "image.png");
        File.WriteAllText(source, "AAAA"); File.WriteAllText(target, "BBBB");
        await Assert.ThrowsAsync<IOException>(() => _operations.PrepareFileMergeAsync(source, target));
        Assert.AreEqual("AAAA", File.ReadAllText(source)); Assert.AreEqual("BBBB", File.ReadAllText(target));
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task NewDestinationOrChangedContent_AfterConfirmation_IsRejected(bool existing)
    {
        var source = Path.Combine(A, "image.png"); var target = Path.Combine(B, "image.png");
        File.WriteAllText(source, "AAAA"); if (existing) File.WriteAllText(target, "AAAA");
        var plan = await _operations.PrepareFileMergeAsync(source, target);
        var modified = existing ? File.GetLastWriteTimeUtc(target) : DateTime.UtcNow;
        File.WriteAllText(target, "BBBB"); File.SetLastWriteTimeUtc(target, modified);
        await Assert.ThrowsAsync<IOException>(() => _operations.ExecuteFileMergeAsync(plan));
        Assert.AreEqual("AAAA", File.ReadAllText(source)); Assert.AreEqual("BBBB", File.ReadAllText(target));
    }

    [TestMethod]
    [DataRow(true)] [DataRow(false)]
    public async Task Subfolders_BlockSingleFileMergeAndDelete(bool sourceChild)
    {
        var source = Path.Combine(A, "image.png"); var target = Path.Combine(B, "image.png");
        File.WriteAllText(source, "content");
        var plan = await _operations.PrepareFileMergeAsync(source, target);
        Directory.CreateDirectory(Path.Combine(sourceChild ? A : B, "child"));
        await Assert.ThrowsAsync<IOException>(() => _operations.PrepareFileMergeAsync(source, target));
        await Assert.ThrowsAsync<IOException>(() => _operations.ExecuteFileMergeAsync(plan));
        if (sourceChild) await Assert.ThrowsAsync<IOException>(() => _operations.DeleteFileAsync(source));
        Assert.AreEqual("content", File.ReadAllText(source)); Assert.IsFalse(File.Exists(target));
    }
}
