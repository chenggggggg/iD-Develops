namespace iD_Develops.Services
{
    public sealed class StoragePublicUrlService : IStoragePublicUrlService
    {
        private readonly string _prefix;
        private readonly string? _publicBaseUrl;

        public StoragePublicUrlService(IConfiguration configuration)
        {
            _prefix = (configuration["Storage:Prefix"] ?? string.Empty).Trim().Trim('/');
            _publicBaseUrl = ResolvePublicBaseUrl(configuration);
        }

        public string? ResolvePublicUrl(string? objectKey)
        {
            if (string.IsNullOrWhiteSpace(objectKey))
            {
                return null;
            }

            var key = objectKey.Trim();
            if (IsAbsoluteHttpUrl(key))
            {
                return key;
            }

            if (string.IsNullOrWhiteSpace(_publicBaseUrl))
            {
                return null;
            }

            key = key.Replace('\\', '/').TrimStart('/');
            if (!string.IsNullOrWhiteSpace(_prefix) &&
                !key.StartsWith($"{_prefix}/", StringComparison.OrdinalIgnoreCase))
            {
                key = $"{_prefix}/{key}";
            }

            return $"{_publicBaseUrl.TrimEnd('/')}/{key}";
        }

        private static string? ResolvePublicBaseUrl(IConfiguration configuration)
        {
            var configured = FirstConfiguredValue(
                configuration["Storage:PublicBaseUrl"],
                configuration["Storage:CdnBaseUrl"]);

            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured.Trim().TrimEnd('/');
            }

            var endpoint = configuration["Storage:Endpoint"]?.Trim();
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri))
            {
                return null;
            }

            return endpointUri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        }

        private static string? FirstConfiguredValue(params string?[] values)
            => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        private static bool IsAbsoluteHttpUrl(string value)
            => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
