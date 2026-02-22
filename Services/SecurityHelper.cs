using System;
using System.IO;
using System.Text.RegularExpressions;

namespace WallArt.Services;

/// <summary>
/// Centralised security utility methods used across WallArt services.
/// </summary>
internal static class SecurityHelper
{
    /// <summary>
    /// Only alphanumeric characters, underscores, and hyphens are permitted in API-sourced IDs.
    /// </summary>
    private static readonly Regex _unsafeIdChars = new(@"[^a-zA-Z0-9_\-]", RegexOptions.Compiled);
    private const int MaxIdLength = 200;

    /// <summary>
    /// Strips unsafe characters from an API-provided identifier so it is safe
    /// for use in file names and URL path segments.
    /// </summary>
    public static string SanitizeId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "_";
        var cleaned = _unsafeIdChars.Replace(id, string.Empty);
        if (cleaned.Length == 0) return "_";
        return cleaned.Length > MaxIdLength ? cleaned[..MaxIdLength] : cleaned;
    }

    /// <summary>
    /// Throws if <paramref name="filePath"/> resolves outside <paramref name="expectedDirectory"/>.
    /// Prevents path-traversal attacks when constructing paths with API-supplied data.
    /// </summary>
    public static string EnsurePathIsWithin(string filePath, string expectedDirectory)
    {
        var resolvedFile = Path.GetFullPath(filePath);
        var resolvedDir  = Path.GetFullPath(expectedDirectory)
                           + Path.DirectorySeparatorChar;

        if (!resolvedFile.StartsWith(resolvedDir, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Security: path traversal attempt. '{filePath}' is outside '{expectedDirectory}'.");

        return resolvedFile;
    }

    /// <summary>
    /// Throws if <paramref name="url"/> does not use the HTTPS scheme.
    /// </summary>
    public static void RequireHttps(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Security: only HTTPS URLs are permitted. Rejected: {url}");
        }
    }
}
