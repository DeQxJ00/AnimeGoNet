using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.App.Configuration;

public sealed record RuntimeConfigurationState(
    bool RunningInContainer,
    bool BackgroundWorkersEnabled,
    bool AccessKeyConfigured);

public sealed record DeploymentConfigurationOptions(AnimeGoOptions Value);
