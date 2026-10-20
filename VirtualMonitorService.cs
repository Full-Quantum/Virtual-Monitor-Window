using System.Diagnostics;
using System.IO.Pipes;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using System.Windows.Forms;

namespace VirtualMonitorWindow;

internal sealed record DisplayTarget(
    string DeviceName,
    string AdapterName,
    string MonitorName,
    Rectangle Bounds,
    bool IsPrimary,
    bool IsVirtual)
{
    public override string ToString()
    {
        var kind = IsVirtual ? "VIRTUAL" : "DISPLAY";
        var primary = IsPrimary ? " · Primary" : "";
        return $"{kind}  {Bounds.Width} × {Bounds.Height}{primary}  —  {DeviceName}";
    }
}

internal static class VirtualMonitorService
{
    private const string PipeName = "MTTVirtualDisplayPipe";
    private const string DriverHardwareId = "Root\\MttVDD";
    private const string DriverClassGuid = "4D36E968-E325-11CE-BFC1-08002BE10318";
    private const string DriverZipName = "VirtualDisplayDriver-x86.Driver.Only.zip";
    private const string NefconZipName = "nefcon_v1.17.40.zip";
    private const string DriverZipSha256 = "e24210692b442b39af763536330ce78b423f19342b7a7792c26de3944e418b3a";
    private const string NefconZipSha256 = "812bae7ed7dfb7d6d2284bc7de2f8ccebc92ed2a0b1ae893c53b337096e50c1a";
    private const string DriverInstallDirectory = @"C:\VirtualDisplayDriver";
    private const string DriverRegistryPath = @"SOFTWARE\MikeTheTech\VirtualDisplayDriver";

    public static IReadOnlyList<DisplayTarget> EnumerateDisplays()
    {
        var adapters = new Dictionary<string, NativeMethods.DisplayDevice>(
            StringComparer.OrdinalIgnoreCase);

        for (uint index = 0; ; index++)
        {
            var adapter = NewDisplayDevice();
            if (!NativeMethods.EnumDisplayDevices(null, index, ref adapter, 0))
                break;
            adapters[adapter.DeviceName] = adapter;
        }

        return Screen.AllScreens
            .Select(screen =>
            {
                adapters.TryGetValue(screen.DeviceName, out var adapter);
                var monitor = NewDisplayDevice();
                NativeMethods.EnumDisplayDevices(screen.DeviceName, 0, ref monitor, 0);
                var adapterName = adapter.DeviceString ?? "";
                var monitorName = monitor.DeviceString ?? "";
                var deviceId = adapter.DeviceId ?? "";
                var isVirtual =
                    adapterName.Contains("MttVDD", StringComparison.OrdinalIgnoreCase) ||
                    adapterName.Contains("Virtual Display Driver", StringComparison.OrdinalIgnoreCase) ||
                    deviceId.Contains("MttVDD", StringComparison.OrdinalIgnoreCase);
                return new DisplayTarget(
                    screen.DeviceName,
                    adapterName,
                    monitorName,
                    screen.Bounds,
                    screen.Primary,
                    isVirtual);
            })
            .OrderByDescending(display => display.IsVirtual)
            .ThenBy(display => display.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static Bitmap CaptureDisplay(DisplayTarget display)
    {
        var bitmap = new Bitmap(display.Bounds.Width, display.Bounds.Height);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(
                display.Bounds.Location,
                Point.Empty,
                display.Bounds.Size,
                CopyPixelOperation.SourceCopy);
            DrawCursor(graphics, display.Bounds);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static void DrawCursor(Graphics graphics, Rectangle displayBounds)
    {
        var cursorInfo = new NativeMethods.CursorInfo
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.CursorInfo>()
        };
        if (!NativeMethods.GetCursorInfo(ref cursorInfo))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        if ((cursorInfo.Flags & NativeMethods.CursorShowing) == 0 ||
            cursorInfo.Cursor == 0 ||
            !displayBounds.Contains(cursorInfo.ScreenPosition.X, cursorInfo.ScreenPosition.Y))
        {
            return;
        }

        if (!NativeMethods.GetIconInfo(cursorInfo.Cursor, out var iconInfo))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var deviceContext = graphics.GetHdc();
            try
            {
                if (!NativeMethods.DrawIconEx(
                    deviceContext,
                    cursorInfo.ScreenPosition.X - displayBounds.Left - (int)iconInfo.HotspotX,
                    cursorInfo.ScreenPosition.Y - displayBounds.Top - (int)iconInfo.HotspotY,
                    cursorInfo.Cursor,
                    0,
                    0,
                    0,
                    0,
                    NativeMethods.DiNormal))
                {
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            finally
            {
                graphics.ReleaseHdc(deviceContext);
            }
        }
        finally
        {
            if (iconInfo.MaskBitmap != 0)
                NativeMethods.DeleteObject(iconInfo.MaskBitmap);
            if (iconInfo.ColorBitmap != 0)
                NativeMethods.DeleteObject(iconInfo.ColorBitmap);
        }
    }

    public static async Task<bool> IsDriverAvailableAsync()
    {
        try
        {
            await SendPipeCommandAsync("PING", TimeSpan.FromSeconds(3));
            return true;
        }
        catch (Exception error) when (
            error is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static async Task ApplyDisplayCountAsync(int count)
    {
        if (count is < 0 or > 16)
            throw new ArgumentOutOfRangeException(nameof(count), "Display count must be between 0 and 16.");
        await SendPipeCommandAsync($"SETDISPLAYCOUNT {count}", TimeSpan.FromSeconds(8));
    }

    public static async Task<(bool Success, string Message)> InstallWithUacAsync()
    {
        var executable = Environment.ProcessPath;
        var expectedExecutable = $"{typeof(MainForm).Assembly.GetName().Name}.exe";
        if (string.IsNullOrWhiteSpace(executable) ||
            !Path.GetFileName(executable).Equals(expectedExecutable, StringComparison.OrdinalIgnoreCase))
        {
            return (false, $"Run the published {expectedExecutable} before installing the driver.");
        }

        var operationId = Guid.NewGuid().ToString("N");
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = AppContext.BaseDirectory,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("--elevated-install");
        startInfo.ArgumentList.Add(operationId);

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
                return (false, "Windows did not start the elevated driver installer.");

            var completed = await Task.Run(() => process.WaitForExit(300_000));
            if (!completed)
                return (false, "The elevated driver installer is still running; check Windows driver setup before retrying.");

            var logPath = GetInstallLogPath(operationId);
            var details = File.Exists(logPath)
                ? await File.ReadAllTextAsync(logPath)
                : "The elevated installer did not write a result log.";
            if (process.ExitCode == 0)
                return (true, details.Trim());
            return (false, $"Driver installation failed (exit code {process.ExitCode}).{Environment.NewLine}{details}".Trim());
        }
        catch (System.ComponentModel.Win32Exception error) when (error.NativeErrorCode == 1223)
        {
            return (false, "Driver installation was cancelled at the Windows elevation prompt.");
        }
        catch (System.ComponentModel.Win32Exception error)
        {
            return (false, $"Could not start the elevated installer: {error.Message}");
        }
    }

    public static async Task<int> RunSelfTestAsync()
    {
        try
        {
            var driverZip = AssetPath(DriverZipName);
            var nefconZip = AssetPath(NefconZipName);
            VerifySha256(driverZip, DriverZipSha256);
            VerifySha256(nefconZip, NefconZipSha256);

            var displays = EnumerateDisplays();
            Console.WriteLine($"Bundled VDD driver package: verified");
            Console.WriteLine($"Bundled Nefcon package: verified");
            Console.WriteLine($"Windows displays found: {displays.Count}");
            foreach (var display in displays)
                Console.WriteLine(
                    $"{(display.IsVirtual ? "VIRTUAL" : "DISPLAY")} {display.Bounds.Width}x{display.Bounds.Height} {display.DeviceName}");

            if (displays.Count > 0)
            {
                using var capture = CaptureDisplay(displays[0]);
                Console.WriteLine($"Display capture: {capture.Width} x {capture.Height}");
            }

            Console.WriteLine(
                $"VDD control pipe: {(await IsDriverAvailableAsync() ? "available" : "not installed or not running")}");
            Console.WriteLine("Driver installation: not requested by self-test");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Self-test failed: {error.Message}");
            return 1;
        }
    }

    public static Task<int> RunElevatedInstallAsync() =>
        RunElevatedInstallAsync(Environment.GetCommandLineArgs().Skip(1).ToArray());

    private static async Task<int> RunElevatedInstallAsync(string[] args)
    {
        if (args.Length != 2 ||
            args[0] != "--elevated-install" ||
            !Guid.TryParseExact(args[1], "N", out _))
        {
            return 2;
        }

        var logPath = GetInstallLogPath(args[1]);
        try
        {
            var result = await InstallDriverAsync();
            await File.WriteAllTextAsync(logPath, result.Message);
            return result.Success ? 0 : 1;
        }
        catch (Exception error)
        {
            try
            {
                await File.WriteAllTextAsync(logPath, error.ToString());
            }
            catch (IOException)
            {
            }
            return 1;
        }
    }

    private static async Task<(bool Success, string Message)> InstallDriverAsync()
    {
        if (!Environment.Is64BitOperatingSystem || !Environment.Is64BitProcess)
            return (false, "This portable package contains the signed x64 driver only.");

        var driverZip = AssetPath(DriverZipName);
        var nefconZip = AssetPath(NefconZipName);
        VerifySha256(driverZip, DriverZipSha256);
        VerifySha256(nefconZip, NefconZipSha256);

        var workDirectory = Path.Combine(Path.GetTempPath(), "VirtualMonitorWindow", Guid.NewGuid().ToString("N"));
        var driverExtract = Path.Combine(workDirectory, "driver");
        var nefconExtract = Path.Combine(workDirectory, "nefcon");
        Directory.CreateDirectory(driverExtract);
        Directory.CreateDirectory(nefconExtract);
        var progress = new StringBuilder();

        try
        {
            ZipFile.ExtractToDirectory(driverZip, driverExtract);
            ZipFile.ExtractToDirectory(nefconZip, nefconExtract);

            var packageDirectory = Path.Combine(driverExtract, "VirtualDisplayDriver");
            var infPath = Path.Combine(packageDirectory, "MttVDD.inf");
            var catalogPath = Path.Combine(packageDirectory, "mttvdd.cat");
            var libraryPath = Path.Combine(packageDirectory, "MttVDD.dll");
            var settingsPath = Path.Combine(packageDirectory, "vdd_settings.xml");
            var nefconPath = Path.Combine(nefconExtract, "x64", "nefconc.exe");
            foreach (var path in new[] { infPath, catalogPath, libraryPath, settingsPath, nefconPath })
            {
                if (!File.Exists(path))
                    throw new FileNotFoundException("A required signed driver package file is missing.", path);
            }

            var nefconSignature = new System.Security.Cryptography.X509Certificates.X509Certificate2(
                System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(nefconPath));
            if (!nefconSignature.Subject.Contains("Nefarius", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The Nefcon helper signature does not match its expected publisher.");

            Directory.CreateDirectory(DriverInstallDirectory);
            foreach (var path in new[] { infPath, catalogPath, libraryPath })
                File.Copy(path, Path.Combine(DriverInstallDirectory, Path.GetFileName(path)), true);
            var installedSettingsPath = Path.Combine(DriverInstallDirectory, "vdd_settings.xml");
            if (!File.Exists(installedSettingsPath))
                File.Copy(settingsPath, installedSettingsPath);

            using (var key = Registry.LocalMachine.CreateSubKey(DriverRegistryPath, writable: true)
                   ?? throw new InvalidOperationException("Could not create the VDD settings registry key."))
            {
                key.SetValue("VDDPATH", DriverInstallDirectory, RegistryValueKind.String);
            }

            progress.AppendLine("Official Virtual Display Driver package SHA-256 verified.");
            progress.AppendLine("Official Nefcon helper SHA-256 and publisher verified.");
            progress.AppendLine($"Driver files staged at {DriverInstallDirectory}.");
            progress.AppendLine("Existing vdd_settings.xml was preserved.");
            progress.AppendLine("Windows will now install the signed virtual display driver.");

            var startInfo = new ProcessStartInfo(nefconPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = packageDirectory
            };
            foreach (var argument in new[]
                     {
                         "install",
                         infPath,
                         DriverHardwareId,
                         "--no-duplicates",
                         "--remove-duplicates"
                     })
                startInfo.ArgumentList.Add(argument);

            using var installer = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Windows could not start Nefcon.");
            var stdoutTask = installer.StandardOutput.ReadToEndAsync();
            var stderrTask = installer.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await installer.WaitForExitAsync(timeout.Token);
            progress.Append(await stdoutTask);
            progress.Append(await stderrTask);
            if (installer.ExitCode is not 0 and not 3010)
            {
                progress.AppendLine($"Nefcon exited with code {installer.ExitCode}.");
                return (false, progress.ToString());
            }

            progress.AppendLine(installer.ExitCode == 3010
                ? "Windows reported that a restart is required to finish installation."
                : "Driver installation completed.");
            return (true, progress.ToString());
        }
        finally
        {
            try
            {
                Directory.Delete(workDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static async Task SendPipeCommandAsync(string command, TimeSpan timeout)
    {
        using var pipe = new NamedPipeClientStream(
            ".",
            PipeName,
            PipeDirection.Out,
            PipeOptions.Asynchronous);
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            await pipe.ConnectAsync(cancellation.Token);
        }
        catch (OperationCanceledException error)
        {
            throw new TimeoutException("The VDD control pipe is not responding.", error);
        }

        var payload = Encoding.Unicode.GetBytes(command);
        await pipe.WriteAsync(payload, cancellation.Token);
        await pipe.FlushAsync(cancellation.Token);
    }

    private static NativeMethods.DisplayDevice NewDisplayDevice() => new()
    {
        Size = Marshal.SizeOf<NativeMethods.DisplayDevice>(),
        DeviceName = "",
        DeviceString = "",
        DeviceId = "",
        DeviceKey = ""
    };

    private static string AssetPath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Assets", name);

    private static string GetInstallLogPath(string operationId) =>
        Path.Combine(Path.GetTempPath(), $"VirtualMonitorWindow-install-{operationId}.txt");

    private static void VerifySha256(string path, string expectedHash)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("A required portable driver package is missing.", path);
        var actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Package checksum mismatch for {Path.GetFileName(path)}.");
    }
}
