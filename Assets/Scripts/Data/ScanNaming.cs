using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// Names for the mock KIRI scans that land in *Your Models*.
///
/// They used to be `New 3D Scan &lt;random 10-99&gt;`, which told you nothing about a scan and could
/// repeat itself. The agreed form is <c>Scan_DD/MM/YYYY</c>.
///
/// Scanning twice in one day would otherwise produce two cards with identical names and no way to
/// tell which is which, so a second scan on the same date becomes `Scan_07/10/2026 (2)`.
/// </summary>
public static class ScanNaming
{
    /// <summary>`Scan_DD/MM/YYYY`, with real slashes whatever the device's locale uses.</summary>
    public static string ForDate(DateTime when)
    {
        // The quotes matter: a bare "/" in a .NET format string means "the culture's date
        // separator", which on some phones is "." or "-".
        return "Scan_" + when.ToString("dd'/'MM'/'yyyy", CultureInfo.InvariantCulture);
    }

    /// <summary>Appends ` (2)`, ` (3)` … while the name is already taken.</summary>
    public static string MakeUnique(string baseName, IEnumerable<ModelData> existing)
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (existing != null)
        {
            foreach (ModelData m in existing)
            {
                if (m != null && !string.IsNullOrEmpty(m.name)) taken.Add(m.name);
            }
        }

        if (!taken.Contains(baseName)) return baseName;

        for (int n = 2; n < 1000; n++)
        {
            string candidate = $"{baseName} ({n})";
            if (!taken.Contains(candidate)) return candidate;
        }

        return baseName; // a thousand scans in one day: just reuse the name
    }

    /// <summary>
    /// Builds today's scan name, checked against what is already stored.
    /// If the lookup is unavailable or fails, falls back to the plain date stamp rather than
    /// dropping the capture.
    /// </summary>
    public static void BuildTodaysName(Action<string> onReady)
    {
        string baseName = ForDate(DateTime.Now);

        if (onReady == null) return;
        if (ModelCatalog.Instance == null) { onReady(baseName); return; }

        ModelCatalog.Instance.GetAllModels(models => onReady(MakeUnique(baseName, models)));
    }
}
