using System;
using System.Collections.Generic;
using System.Net;

namespace Kapla
{
    internal enum KoboEndpointKind
    {
        Api,
        Activation,
        Resource
    }

    internal enum KoboCredentialType
    {
        None,
        AccessToken,
        UserKey
    }

    internal static class KoboEndpointPolicy
    {
        public const string StoreApiHost = "storeapi.kobo.com";
        public const string AuthApiHost = "auth.kobobooks.com";

        public static string Validate(Uri uri, KoboEndpointKind kind)
        {
            if (uri == null)
            {
                return "The Kobo destination is missing.";
            }
            if (!String.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return "Kobo requests require HTTPS.";
            }
            if (String.IsNullOrWhiteSpace(uri.Host) || IsLocalOrPrivateHost(uri.Host))
            {
                return "The Kobo destination is local or private and was rejected.";
            }

            if (kind == KoboEndpointKind.Api && !IsExactHost(uri, StoreApiHost))
            {
                return "The Kobo API destination is not trusted.";
            }
            if (kind == KoboEndpointKind.Activation && !IsExactHost(uri, AuthApiHost))
            {
                return "The Kobo activation destination is not trusted.";
            }
            return null;
        }

        public static bool AllowsAccessToken(Uri uri)
        {
            return uri != null && IsExactHost(uri, StoreApiHost);
        }

        public static bool AllowsUserKey(Uri uri)
        {
            return uri != null && IsExactHost(uri, StoreApiHost);
        }

        public static IDictionary<string, string> BuildCredentialHeaders(Uri uri, string accessToken, string userKey)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!String.IsNullOrWhiteSpace(accessToken) && AllowsAccessToken(uri))
            {
                headers["Authorization"] = "Bearer " + accessToken;
            }
            if (!String.IsNullOrWhiteSpace(userKey) && AllowsUserKey(uri))
            {
                headers["x-kobo-userkey"] = userKey;
            }
            return headers;
        }

        public static bool IsExactHost(Uri uri, string expectedHost)
        {
            return uri != null
                && String.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && String.Equals(uri.Host, expectedHost, StringComparison.OrdinalIgnoreCase)
                && !IsLocalOrPrivateHost(uri.Host);
        }

        public static bool IsLocalOrPrivateHost(string host)
        {
            if (String.IsNullOrWhiteSpace(host))
            {
                return true;
            }

            var normalized = host.Trim().TrimEnd('.');
            if (String.Equals(normalized, "localhost", StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            IPAddress address;
            if (!IPAddress.TryParse(normalized, out address))
            {
                return false;
            }

            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }
            if (IPAddress.IsLoopback(address) || IPAddress.None.Equals(address) || IPAddress.Any.Equals(address))
            {
                return true;
            }
            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                var bytes = address.GetAddressBytes();
                return bytes[0] == 0
                    || bytes[0] == 10
                    || bytes[0] == 127
                    || (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
                    || (bytes[0] == 169 && bytes[1] == 254)
                    || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                    || (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0)
                    || (bytes[0] == 192 && bytes[1] == 168)
                    || (bytes[0] == 198 && (bytes[1] == 18 || bytes[1] == 19))
                    || bytes[0] >= 224;
            }

            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                var bytes = address.GetAddressBytes();
                return address.IsIPv6LinkLocal
                    || address.IsIPv6SiteLocal
                    || (bytes[0] & 0xfe) == 0xfc
                    || (bytes[0] & 0xff) == 0xff;
            }
            return true;
        }

        public static Uri CreateUri(string value)
        {
            Uri uri;
            return Uri.TryCreate(value, UriKind.Absolute, out uri) ? uri : null;
        }
    }
}
