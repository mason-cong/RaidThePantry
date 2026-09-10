using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;

namespace RecipeApi.Infrastructure.Scraping;

/// <summary>
/// Where the SSRF rule is actually enforced.
///
/// <see cref="FetchableUrl.IsHostAllowedAsync"/> resolves a name to vet it, and
/// then the HTTP client resolves the same name again to open the socket. Those
/// are two separate lookups, and a hostile DNS server can answer differently the
/// second time — returning a public address to pass the check and 169.254.169.254
/// to receive the connection. That is DNS rebinding, and no amount of checking
/// beforehand closes it, because the check and the connection are looking at
/// different answers.
///
/// The fix is to stop having two lookups. This callback resolves the host and
/// opens the socket itself, so the address that was vetted is by construction the
/// address that gets dialled. There is no window in between for the answer to
/// change.
///
/// TLS is still negotiated normally: SocketsHttpHandler layers it on top of
/// whatever transport stream this returns.
/// </summary>
public static class GuardedConnect
{
    public static Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>> Handler(
        bool allowLoopback) =>
        async (context, ct) =>
        {
            var host = context.DnsEndPoint.Host;
            var port = context.DnsEndPoint.Port;

            IPAddress[] addresses;

            if (IPAddress.TryParse(host, out var literal))
            {
                addresses = [literal];
            }
            else
            {
                try
                {
                    addresses = await Dns.GetHostAddressesAsync(host, ct);
                }
                catch (SocketException ex)
                {
                    throw new HttpRequestException($"Could not resolve {host}.", ex);
                }
            }

            // Every answer must pass, not merely one of them. A name that resolves
            // to one public and one private address is the attack, not an
            // inconvenience to route around by picking the public one — the
            // connection could still land on the other.
            if (addresses.Length == 0 || !addresses.All(a => FetchableUrl.IsAddressAllowed(a, allowLoopback)))
                throw new HttpRequestException($"Refusing to connect to {host}: address is not permitted.");

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

            try
            {
                await socket.ConnectAsync(addresses, port, ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        };
}
