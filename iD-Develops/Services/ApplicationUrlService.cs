using iD_Develops.Configuration;
using Microsoft.Extensions.Options;

namespace iD_Develops.Services
{
    public interface IApplicationUrlService
    {
        string PublicBaseUrl { get; }
        string PortalBaseUrl { get; }
        string PublicUrl(string pathAndQuery = "/");
        string PortalUrl(string pathAndQuery = "/");
        bool IsPortalRequest(HttpRequest request);
    }

    public sealed class ApplicationUrlService : IApplicationUrlService
    {
        private readonly Uri _publicBaseUri;
        private readonly Uri _portalBaseUri;

        public ApplicationUrlService(IOptions<ApplicationUrlOptions> options)
        {
            ArgumentNullException.ThrowIfNull(options);

            _publicBaseUri = ParseAbsoluteBaseUrl(options.Value.PublicBaseUrl, nameof(options.Value.PublicBaseUrl));
            _portalBaseUri = ParseAbsoluteBaseUrl(options.Value.PortalBaseUrl, nameof(options.Value.PortalBaseUrl));
        }

        public string PublicBaseUrl => _publicBaseUri.AbsoluteUri.TrimEnd('/');
        public string PortalBaseUrl => _portalBaseUri.AbsoluteUri.TrimEnd('/');

        public string PublicUrl(string pathAndQuery = "/") => BuildUrl(_publicBaseUri, pathAndQuery);

        public string PortalUrl(string pathAndQuery = "/") => BuildUrl(_portalBaseUri, pathAndQuery);

        public bool IsPortalRequest(HttpRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            return string.Equals(request.Host.Host, _portalBaseUri.Host, StringComparison.OrdinalIgnoreCase);
        }

        private static Uri ParseAbsoluteBaseUrl(string value, string optionName)
        {
            if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                string.IsNullOrWhiteSpace(uri.Host))
            {
                throw new InvalidOperationException(
                    $"ApplicationUrls:{optionName} must be an absolute HTTP or HTTPS URL.");
            }

            return uri;
        }

        private static string BuildUrl(Uri baseUri, string pathAndQuery)
        {
            var relative = string.IsNullOrWhiteSpace(pathAndQuery) ? "/" : pathAndQuery.Trim();
            if (!relative.StartsWith('/'))
            {
                relative = "/" + relative;
            }

            return new Uri(baseUri, relative).AbsoluteUri;
        }
    }
}
