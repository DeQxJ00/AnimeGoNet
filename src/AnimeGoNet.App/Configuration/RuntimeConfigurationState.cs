namespace AnimeGoNet.App.Configuration;

public sealed record RuntimeConfigurationState(
    bool RunningInContainer,
    bool BackgroundWorkersEnabled,
    bool AccessKeyConfigured);
