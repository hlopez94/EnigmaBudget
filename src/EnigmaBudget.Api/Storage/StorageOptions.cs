namespace EnigmaBudget.Api.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage:S3";

    public string? Endpoint { get; init; }
    public string Bucket { get; init; } = "enigmabudget-private";
    public string Region { get; init; } = "auto";
    public bool ForcePathStyle { get; init; } = true;

    public static bool IsValid(StorageOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Bucket) || string.IsNullOrWhiteSpace(options.Region)) return false;
        return string.IsNullOrWhiteSpace(options.Endpoint)
            || Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint)
                && endpoint.Scheme is "http" or "https"
                && string.IsNullOrEmpty(endpoint.UserInfo)
                && string.IsNullOrEmpty(endpoint.Query)
                && string.IsNullOrEmpty(endpoint.Fragment);
    }
}
