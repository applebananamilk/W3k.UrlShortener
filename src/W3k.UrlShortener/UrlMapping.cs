namespace W3k.UrlShortener;

public class UrlMapping
{
    public string Key { get; protected set; }

    public string OriginalUrl { get; protected set; }

    public DateTime Created { get; protected set; } = DateTime.Now;

    public UrlMapping(string key, string originalUrl)
    {
        ArgumentException.ThrowIfNullOrEmpty(key, nameof(key));
        ArgumentException.ThrowIfNullOrEmpty(originalUrl, nameof(originalUrl));

        Key = key;
        OriginalUrl = originalUrl;
    }
}
