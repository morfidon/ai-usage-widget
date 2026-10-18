using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AIUsageWidget;

public static class CodexNavigation
{
    public static string? ThreadUri(string id) => Guid.TryParseExact(id, "D", out var thread) && thread != Guid.Empty
        ? "codex://threads/" + thread.ToString("D") : null;

    // Use the package identity, which survives updates, before the protocol association.
    // Older Codex installers can leave that association pointing at a removed executable.
    public static string? Open(string id)
    {
        var uri = ThreadUri(id);
        if (uri is null) return "Brak poprawnego ID rozmowy. Możesz skopiować identyfikator sesji.";
        IApplicationActivationManager? manager = null;
        try
        {
            manager = (IApplicationActivationManager)new ApplicationActivationManager();
            int result = manager.ActivateApplication("OpenAI.Codex_2p2nqsd0c76g0!App", "--single-argument " + uri, 2, out _);
            if (result >= 0) return null;
        }
        catch (COMException) { }
        finally { if (manager is not null) Marshal.ReleaseComObject(manager); }
        try
        {
            using var process = Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            return null;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            return "Nie udało się otworzyć Codex. Uruchom aplikację i spróbuj ponownie lub skopiuj ID sesji.";
        }
    }

    // Windows SDK: shobjidl_core.h, IApplicationActivationManager::ActivateApplication.
    [ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
    class ApplicationActivationManager { }

    [ComImport, Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string arguments, uint options, out uint processId);
    }
}
