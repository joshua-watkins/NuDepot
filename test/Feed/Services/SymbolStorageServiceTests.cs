using Moq;
using NuDepot.Feed.Storage;
using Xunit;

namespace NuDepot.Feed.Tests.Services;

public class SymbolStorageServiceTests
{
    private const string Key = "0123456789ABCDEF0123456789ABCDEF12345678";
    private static readonly string ExpectedPath = Path.Combine("symbols", "test.pdb", "0123456789abcdef0123456789abcdefffffffff");

    public static TheoryData<string, string> InvalidNamesAndKeys => new()
    {
        { "", Key },
        { ".", Key },
        { "..", Key },
        { "../test.pdb", Key },
        { "..\\test.pdb", Key },
        { "folder/test.pdb", Key },
        { "test\0.pdb", Key },
        { "test.pdb", "" },
        { "test.pdb", "0123456789abcdef" },
        { "test.pdb", "0123456789abcdef0123456789abcde/ffffffff" },
        { "test.pdb", "0123456789abcdef0123456789abcdéfffffffff" },
    };

    private Mock<IStorageService> Storage { get; } = new();
    private SymbolStorageService Target { get; }

    public SymbolStorageServiceTests()
    {
        Target = new SymbolStorageService(Storage.Object);
    }

    [Theory]
    [MemberData(nameof(InvalidNamesAndKeys))]
    public async Task SaveRejectsInvalidNamesAndKeys(string filename, string key)
    {
        using MemoryStream pdb = new([1, 2, 3]);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Target.SavePortablePdbContentAsync(filename, key, pdb, CancellationToken.None));

        Storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SavesPdbUnderItsKeyWithoutAge()
    {
        using MemoryStream pdb = new([1, 2, 3]);
        Storage
            .Setup(s => s.PutAsync(ExpectedPath, pdb, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StoragePutResult.Success);

        Assert.True(await Target.SavePortablePdbContentAsync("Test.pdb", Key, pdb, CancellationToken.None));

        Storage.Verify(s => s.PutAsync(ExpectedPath, pdb, It.IsAny<string>(), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task SaveFailsIfADifferentPdbExists()
    {
        using MemoryStream pdb = new([1, 2, 3]);
        Storage
            .Setup(s => s.PutAsync(ExpectedPath, pdb, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StoragePutResult.Conflict);

        Assert.False(await Target.SavePortablePdbContentAsync("test.pdb", Key, pdb, CancellationToken.None));
    }

    [Fact]
    public async Task SaveSucceedsIfTheSamePdbExists()
    {
        using MemoryStream pdb = new([1, 2, 3]);
        Storage
            .Setup(s => s.PutAsync(ExpectedPath, pdb, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StoragePutResult.AlreadyExists);

        Assert.True(await Target.SavePortablePdbContentAsync("test.pdb", Key, pdb, CancellationToken.None));
    }

    [Theory]
    [MemberData(nameof(InvalidNamesAndKeys))]
    public async Task InvalidNamesAndKeysAreNotFound(string filename, string key)
    {
        Assert.Null(await Target.GetPortablePdbContentStreamOrNullAsync(filename, key));

        Storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MissingPdbIsNotFound()
    {
        Storage
            .Setup(s => s.GetAsync(ExpectedPath, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FileNotFoundException());

        Assert.Null(await Target.GetPortablePdbContentStreamOrNullAsync("test.pdb", Key));
    }

    [Fact]
    public async Task StorageFailuresAreNotHidden()
    {
        Storage
            .Setup(s => s.GetAsync(ExpectedPath, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("The disk is gone."));

        await Assert.ThrowsAsync<IOException>(() =>
            Target.GetPortablePdbContentStreamOrNullAsync("test.pdb", Key));
    }

    [Fact]
    public async Task ReturnsPdb()
    {
        using MemoryStream pdb = new([1, 2, 3]);
        Storage
            .Setup(s => s.GetAsync(ExpectedPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pdb);

        Assert.Same(pdb, await Target.GetPortablePdbContentStreamOrNullAsync("test.pdb", "0123456789abcdef0123456789abcdef"));
    }
}
