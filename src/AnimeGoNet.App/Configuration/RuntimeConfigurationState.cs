using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.App.Configuration;

public sealed record RuntimeConfigurationState(
    bool RunningInContainer,
    bool BackgroundWorkersEnabled,
    bool InnerPluginMikanAccessKeyConfigured,
    bool WebUiAccessKeyConfigured = false);

public sealed record DeploymentConfigurationOptions(AnimeGoOptions Value);

public sealed class DataUpdateRuntimeState(DataUpdateOptions value)
{
    private readonly Lock _sync = new();
    private DataUpdateOptions _value = value;

    public DataUpdateOptions Value
    {
        get
        {
            lock (_sync)
            {
                return _value;
            }
        }
    }

    public void Update(DataUpdateOptions value)
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (_sync)
        {
            _value = value;
        }
    }
}
