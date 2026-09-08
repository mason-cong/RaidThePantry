using System.Net;
using System.Net.Sockets;

namespace RecipeApi.Infrastructure.Scraping;

/// <summary>
/// Guards the one place this API fetches a URL chosen by a caller.
///
/// Without this, POST /api/import/url is a server-side request forgery tool: any
/// account could point it at http://169.254.169.254/ for cloud instance
/// credentials, or at a service reachable only from inside the network, and read
/// the result back through the error message or the imported recipe.
///
/// Known gap: this resolves DNS to check the address, and the HTTP client
/// resolves again when it connects. A name that changes answers between the two
/// (DNS rebinding) defeats it. Closing that needs the connection pinned to the
/// vetted address, which is worth doing before this runs anywhere public.
/// </summary>
public static class FetchableUrl
{
    public static bool TryCreate(string? input, out Uri? uri, out string? error)
    {
        uri = null;
        error = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "A URL is required.";
            return false;
        }

        if (!Uri.TryCreate(input.Trim(), UriKind.Absolute, out var parsed))
        {
            error = "That is not a valid absolute URL.";
            return false;
        }

        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
        {
            error = "Only http and https URLs can be imported.";
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.UserInfo))
        {
            error = "URLs with embedded credentials are not accepted.";
            return false;
        }

        uri = parsed;
        return true;
    }

    public static async Task<bool> IsHostAllowedAsync(Uri uri, bool allowLoopback, CancellationToken ct)
    {
        IPAddress[] addresses;

        if (IPAddress.TryParse(uri.Host, out var literal))
        {
            addresses = [literal];
        }
        else
        {
            try
            {
                addresses = await Dns.GetHostAddressesAsync(uri.Host, ct);
            }
            catch (SocketException)
            {
                return false;
            }
        }

        // Every resolved address must be acceptable. A host that answers with one
        // public and one private address is exactly the attack.
        return addresses.Length > 0 && addresses.All(a => IsAllowed(a, allowLoopback));
    }

    private static bool IsAllowed(IPAddress address, bool allowLoopback)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address))
            return allowLoopback;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();

            return b[0] switch
            {
                0 => false,                                   // 0.0.0.0/8
                10 => false,                                  // private
                127 => allowLoopback,
                169 when b[1] == 254 => false,                // link-local, incl. cloud metadata
                172 when b[1] >= 16 && b[1] <= 31 => false,   // private
                192 when b[1] == 168 => false,                // private
                192 when b[1] == 0 && b[2] == 0 => false,     // IETF protocol assignments
                100 when b[1] >= 64 && b[1] <= 127 => false,  // carrier-grade NAT
                198 when b[1] == 18 || b[1] == 19 => false,   // benchmarking
                >= 224 => false,                              // multicast and reserved
                _ => true
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
                return false;

            // fc00::/7 unique local
            return (address.GetAddressBytes()[0] & 0xFE) != 0xFC;
        }

        return false;
    }
}
