using EnigmaBudget.Api.Storage;

namespace EnigmaBudget.Api.Tests;

public sealed class StorageOptionsTests
{
    [Fact]
    public void Default_options_are_valid_without_configuring_an_endpoint()
    {
        Assert.True(StorageOptions.IsValid(new StorageOptions()));
    }

    [Theory]
    [InlineData("ftp://objects.internal")]
    [InlineData("https://sample-user@objects.internal")]
    [InlineData("https://objects.internal?sample=value")]
    public void S3_endpoint_rejects_non_http_or_credential_bearing_urls(string endpoint)
    {
        Assert.False(StorageOptions.IsValid(new StorageOptions { Endpoint = endpoint }));
    }

    [Fact]
    public void S3_endpoint_accepts_a_private_http_or_https_origin()
    {
        Assert.True(StorageOptions.IsValid(new StorageOptions { Endpoint = "http://garage:3900" }));
        Assert.True(StorageOptions.IsValid(new StorageOptions { Endpoint = "https://account.r2.cloudflarestorage.com" }));
    }
}
