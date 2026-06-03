using DocumentProcessing.Contracts.Models;

namespace DocumentProcessing.Tests.Contracts;

public class DocumentStatusTests
{
    [Fact]
    public void Uploaded_HasValueOne()
    {
        Assert.Equal(1, (int)DocumentStatus.Uploaded);
    }

    [Fact]
    public void Values_AreInSequentialOrder()
    {
        var values = Enum.GetValues<DocumentStatus>()
            .OrderBy(v => (int)v)
            .ToArray();

        Assert.Equal(6, values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            Assert.Equal(i + 1, (int)values[i]);
        }
    }

    [Fact]
    public void StatusNames_AreCorrect()
    {
        var names = Enum.GetNames<DocumentStatus>();
        Assert.Contains("Uploaded", names);
        Assert.Contains("Classified", names);
        Assert.Contains("ContentExtracted", names);
        Assert.Contains("MetadataEnriched", names);
        Assert.Contains("Approved", names);
        Assert.Contains("Rejected", names);
    }

    [Fact]
    public void Count_IsExactlySix()
    {
        Assert.Equal(6, Enum.GetValues<DocumentStatus>().Length);
    }
}
