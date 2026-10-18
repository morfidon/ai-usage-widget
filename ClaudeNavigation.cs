using System;
using System.ComponentModel;
using System.Diagnostics;

namespace AIUsageWidget;

public static class ClaudeNavigation
{
    // Claude Desktop's resume handler adopts the local CLI conversation by UUID.
    // An already imported conversation is reopened; Desktop handles active-session conflicts.
    public static string? ThreadUri(string id) => Guid.TryParseExact(id, "D", out var thread) && thread != Guid.Empty
        ? "claude://resume?session=" + thread.ToString("D") : null;

    public static string? Open(string id)
    {
        var uri = ThreadUri(id);
        if (uri is null) return "Brak poprawnego ID rozmowy Claude. Możesz skopiować identyfikator sesji.";
        try
        {
            using var process = Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            return null;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            return "Nie udało się otworzyć Claude Desktop. Uruchom aplikację i spróbuj ponownie lub skopiuj ID sesji.";
        }
    }
}
