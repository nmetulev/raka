using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace Raka.Cli.Connection;

/// <summary>
/// Injects the Raka TAP DLL into a target process using InitializeXamlDiagnosticsEx.
/// This enables inspecting WinUI 3 apps that don't have the Raka.DevTools NuGet package.
/// The TAP DLL walks the visual tree via IVisualTreeService and sends JSON over a named pipe.
/// </summary>
internal static class TapInjector
{
    // Must match CLSID_RakaTap in raka_tap.cpp
    private static readonly Guid CLSID_RakaTap = new("7A3F1E8D-4B2C-4D6A-9E5F-1C8A2B3D4E5F");

    // InitializeXamlDiagnosticsEx signature — note: CLSID is passed BY VALUE (not REFCLSID)
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int InitializeXamlDiagnosticsExDelegate(
        [MarshalAs(UnmanagedType.LPWStr)] string endPointName,
        uint processId,
        [MarshalAs(UnmanagedType.LPWStr)] string scriptEngineAbsolutePath,
        [MarshalAs(UnmanagedType.LPWStr)] string tapDllAbsolutePath,
        Guid tapClsid,
        [MarshalAs(UnmanagedType.LPWStr)] string initializationData);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadLibraryW([MarshalAs(UnmanagedType.LPWStr)] string lpLibFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, [MarshalAs(UnmanagedType.LPStr)] string lpProcName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(IntPtr hModule);

    /// <summary>
    /// Injects the TAP DLL into the target process and receives the visual tree JSON.
    /// Returns the JSON string or throws on failure.
    /// </summary>
    public static async Task<string> InjectAndReadTreeAsync(int processId, int timeoutMs = 15000)
    {
        // 1. Find FrameworkUdk.dll path in the target process
        var frameworkUdkPath = FindModuleInProcess(processId, "Microsoft.Internal.FrameworkUdk.dll");
        if (string.IsNullOrEmpty(frameworkUdkPath))
            throw new InvalidOperationException(
                "Could not find Microsoft.Internal.FrameworkUdk.dll in the target process. " +
                "Is this a WinUI 3 (Windows App SDK) application?");

        // 2. Locate our TAP DLL
        var tapDllPath = FindTapDll();
        if (string.IsNullOrEmpty(tapDllPath))
            throw new FileNotFoundException(
                "Could not find raka_tap.dll. Build the TAP DLL with CMake or download from releases.");

        Console.Error.WriteLine($"  FrameworkUdk: {frameworkUdkPath}");
        Console.Error.WriteLine($"  TAP DLL: {tapDllPath}");

        // 3. Create a named pipe to receive tree data from the injected DLL
        var pipeName = $"raka-tap-{processId}-{Environment.TickCount64}";
        var fullPipeName = $@"\\.\pipe\{pipeName}";

        using var pipeServer = new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        // 4. Load FrameworkUdk.dll to get InitializeXamlDiagnosticsEx
        var hXaml = LoadLibraryW(frameworkUdkPath);
        if (hXaml == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                $"Failed to load {frameworkUdkPath}");

        try
        {
            var pInit = GetProcAddress(hXaml, "InitializeXamlDiagnosticsEx");
            if (pInit == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "InitializeXamlDiagnosticsEx not found in FrameworkUdk.dll");

            var initFunc = Marshal.GetDelegateForFunctionPointer<InitializeXamlDiagnosticsExDelegate>(pInit);

            // 5. Try multiple XAML diagnostic endpoints (WinUI uses numbered endpoints)
            var clsid = CLSID_RakaTap;
            int hr = unchecked((int)0x80070490); // ERROR_NOT_FOUND

            for (int i = 0; i < 10; i++)
            {
                var endpoint = $"WinUIVisualDiagConnection{i + 1}";
                hr = initFunc(endpoint, (uint)processId, frameworkUdkPath, tapDllPath, clsid, fullPipeName);

                if (hr != unchecked((int)0x80070490)) // not ERROR_NOT_FOUND
                    break;
            }

            if (hr < 0)
                throw new COMException(
                    $"InitializeXamlDiagnosticsEx failed (0x{hr:X8}). " +
                    "The app may not have a XAML visual tree yet, or XAML diagnostics may be disabled.", hr);
        }
        finally
        {
            FreeLibrary(hXaml);
        }

        // 6. Wait for the TAP DLL to connect and send tree data
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            await pipeServer.WaitForConnectionAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException(
                "TAP DLL did not connect within timeout. Check %TEMP%\\raka_tap.log for details.");
        }

        // 7. Read all data
        var buffer = new byte[1024 * 1024]; // 1MB max
        var totalRead = 0;
        while (true)
        {
            var bytesRead = await pipeServer.ReadAsync(buffer.AsMemory(totalRead), cts.Token);
            if (bytesRead == 0) break;
            totalRead += bytesRead;
            if (totalRead >= buffer.Length) break;
        }

        if (totalRead == 0)
            throw new InvalidOperationException(
                "No tree data received from TAP DLL. Check %TEMP%\\raka_tap.log for details.");

        return Encoding.UTF8.GetString(buffer, 0, totalRead);
    }

    /// <summary>
    /// Finds a DLL loaded in the target process by base name.
    /// Uses System.Diagnostics.Process.Modules for reliable cross-architecture enumeration.
    /// </summary>
    private static string? FindModuleInProcess(int processId, string moduleName)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            foreach (ProcessModule module in process.Modules)
            {
                if (string.Equals(module.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase))
                    return module.FileName;
            }
        }
        catch (Exception)
        {
            // Access denied or process exited
        }
        return null;
    }

    /// <summary>
    /// Finds the raka_tap.dll next to the CLI executable or in known locations.
    /// </summary>
    private static string? FindTapDll()
    {
        var exeDir = AppContext.BaseDirectory;

        // Search in multiple locations, picking the newest file
        var candidates = new List<string>();

        // Check subdirectories named tap* (tap, tap2, etc.)
        foreach (var dir in Directory.GetDirectories(exeDir, "tap*"))
        {
            var p = Path.Combine(dir, "raka_tap.dll");
            if (File.Exists(p)) candidates.Add(p);
        }

        // Next to raka.exe
        var path = Path.Combine(exeDir, "raka_tap.dll");
        if (File.Exists(path)) candidates.Add(path);

        // In the repo build output (for development)
        var repoRoot = FindRepoRoot(exeDir);
        if (repoRoot != null)
        {
            foreach (var buildDir in Directory.GetDirectories(Path.Combine(repoRoot, "src", "Raka.Tap"), "build*"))
            {
                var p = Path.Combine(buildDir, "bin", "raka_tap.dll");
                if (File.Exists(p)) candidates.Add(p);
            }
        }

        // Return the newest file (most recently written)
        return candidates
            .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
            .FirstOrDefault();
    }

    private static string? FindRepoRoot(string startDir)
    {
        var dir = startDir;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "Raka.slnx")) ||
                File.Exists(Path.Combine(dir, "README.md")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
