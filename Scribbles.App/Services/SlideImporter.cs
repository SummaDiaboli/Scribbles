using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using Scribbles.Core;

namespace Scribbles.App.Services;

/// <summary>
/// Slide import chain: PowerPoint Interop (late-bound COM, needs Office) →
/// LibreOffice headless → null (caller falls back to asking for a PDF).
/// </summary>
public static class SlideImporter
{
    public static bool OfficeAvailable()
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey("PowerPoint.Application");
            return key is not null;
        }
        catch
        {
            return false;
        }
    }

    public static bool LibreOfficeAvailable()
    {
        try
        {
            string[] candidates =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LibreOffice", "program", "soffice.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "LibreOffice", "program", "soffice.exe"),
            };
            if (candidates.Any(File.Exists))
                return true;
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "soffice",
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            });
            if (process is null)
                return false;
            process.WaitForExit(10000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <returns>Converted PDF path, or null when no backend is available/failed.</returns>
    public static string? TryImport(string presentationPath, string outputDirectory)
    {
        try
        {
            Directory.CreateDirectory(outputDirectory);
            return SlideImport.ChooseBackend(OfficeAvailable(), LibreOfficeAvailable()) switch
            {
                SlideBackend.PowerPoint => ViaPowerPoint(presentationPath, outputDirectory),
                SlideBackend.LibreOffice => ViaLibreOffice(presentationPath, outputDirectory),
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? ViaPowerPoint(string presentationPath, string outputDirectory)
    {
        // Late-bound COM: no interop package needed, works when Office is installed.
        const int ppFixedFormatTypePDF = 2;
        object? app = null;
        object? presentations = null;
        object? presentation = null;
        try
        {
            var appType = Type.GetTypeFromProgID("PowerPoint.Application")
                ?? throw new InvalidOperationException("PowerPoint not registered.");
            app = Activator.CreateInstance(appType)
                ?? throw new InvalidOperationException("Could not start PowerPoint.");
            presentations = appType.InvokeMember("Presentations",
                System.Reflection.BindingFlags.GetProperty, null, app, null);
            presentation = presentations!.GetType().InvokeMember("Open",
                System.Reflection.BindingFlags.InvokeMethod, null, presentations,
                new object[] { presentationPath, true, true, false });
            string pdfPath = Path.Combine(outputDirectory,
                Path.GetFileNameWithoutExtension(presentationPath) + ".pdf");
            presentation!.GetType().InvokeMember("ExportAsFixedFormat",
                System.Reflection.BindingFlags.InvokeMethod, null, presentation,
                new object[] { pdfPath, ppFixedFormatTypePDF });
            return File.Exists(pdfPath) ? pdfPath : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            try
            {
                presentation?.GetType().InvokeMember("Close",
                    System.Reflection.BindingFlags.InvokeMethod, null, presentation, null);
                app?.GetType().InvokeMember("Quit",
                    System.Reflection.BindingFlags.InvokeMethod, null, app, null);
            }
            catch { /* best effort cleanup */ }
            Release(presentation);
            Release(presentations);
            Release(app);
        }
    }

    private static string? ViaLibreOffice(string presentationPath, string outputDirectory)
    {
        string soffice = FindSoffice() ?? "soffice";
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = soffice,
            Arguments = $"--headless --convert-to pdf --outdir \"{outputDirectory}\" \"{presentationPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        if (process is null)
            return null;
        if (!process.WaitForExit(120000))
        {
            try { process.Kill(); } catch { /* ignore */ }
            return null;
        }
        string pdfPath = Path.Combine(outputDirectory,
            Path.GetFileNameWithoutExtension(presentationPath) + ".pdf");
        return process.ExitCode == 0 && File.Exists(pdfPath) ? pdfPath : null;
    }

    private static string? FindSoffice()
    {
        string[] candidates =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LibreOffice", "program", "soffice.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "LibreOffice", "program", "soffice.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static void Release(object? comObject)
    {
        try
        {
            if (comObject is not null && System.Runtime.InteropServices.Marshal.IsComObject(comObject))
                System.Runtime.InteropServices.Marshal.ReleaseComObject(comObject);
        }
        catch { /* ignore */ }
    }
}
