using System.Diagnostics;

namespace AnimeGoNet.App.Plugins;

internal interface IExternalPluginProcess : IAsyncDisposable
{
    Stream StandardInput { get; }

    Stream StandardOutput { get; }

    Stream StandardError { get; }

    bool HasExited { get; }

    int? ExitCode { get; }

    Task WaitForExitAsync(CancellationToken cancellationToken);

    void Kill();
}

internal interface IExternalPluginProcessFactory
{
    IExternalPluginProcess Start(
        ExternalPluginPackage package,
        string pluginDataPath);
}

internal sealed class SystemExternalPluginProcessFactory : IExternalPluginProcessFactory
{
    public IExternalPluginProcess Start(
        ExternalPluginPackage package,
        string pluginDataPath)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginDataPath);
        Directory.CreateDirectory(pluginDataPath);
        var startInfo = new ProcessStartInfo
        {
            FileName = package.EntryPointPath,
            WorkingDirectory = package.DirectoryPath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.Environment.Clear();
        startInfo.Environment["ANIMEGO_PLUGIN_ID"] = package.Manifest.Id;
        startInfo.Environment["ANIMEGO_PLUGIN_API_VERSION"] =
            ExternalPluginProtocol.CurrentApiVersion.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        startInfo.Environment["ANIMEGO_PLUGIN_DATA_PATH"] = Path.GetFullPath(pluginDataPath);

        var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new ExternalPluginProtocolException(
                    "plugin_process_start_failed",
                    "The external plugin process did not start.");
            }
            return new SystemExternalPluginProcess(process);
        }
        catch (ExternalPluginProtocolException)
        {
            process.Dispose();
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or System.ComponentModel.Win32Exception
                or IOException
                or UnauthorizedAccessException)
        {
            process.Dispose();
            throw new ExternalPluginProtocolException(
                "plugin_process_start_failed",
                "The external plugin process could not be started.",
                exception);
        }
    }

    private sealed class SystemExternalPluginProcess(Process process) : IExternalPluginProcess
    {
        public Stream StandardInput => process.StandardInput.BaseStream;

        public Stream StandardOutput => process.StandardOutput.BaseStream;

        public Stream StandardError => process.StandardError.BaseStream;

        public bool HasExited
        {
            get
            {
                try
                {
                    return process.HasExited;
                }
                catch (InvalidOperationException)
                {
                    return true;
                }
            }
        }

        public int? ExitCode
        {
            get
            {
                try
                {
                    return process.HasExited ? process.ExitCode : null;
                }
                catch (InvalidOperationException)
                {
                    return null;
                }
            }
        }

        public Task WaitForExitAsync(CancellationToken cancellationToken) =>
            process.WaitForExitAsync(cancellationToken);

        public void Kill()
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception exception) when (
                exception is InvalidOperationException
                    or System.ComponentModel.Win32Exception
                    or NotSupportedException)
            {
                // The process already exited or cannot be killed again.
            }
        }

        public ValueTask DisposeAsync()
        {
            Kill();
            process.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
