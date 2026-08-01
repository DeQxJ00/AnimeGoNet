using System.Text;
using AnimeGoNet.App.Plugins;

namespace AnimeGo.PluginTool.Tests;

public sealed class PluginToolApplicationTests
{
    private static readonly System.Text.Json.JsonElement ValidFilterResult =
        PluginToolTestDriver.Json("""
            {
              "decisions": [{
                "index": 7,
                "outcome": "accepted",
                "accepted": true,
                "reason": "fixture",
                "priority": 0,
                "metadata": {}
              }],
              "errors": [],
              "metadata": { "verified": "true" }
            }
            """);

    [Theory]
    [InlineData("source", "source.normalize")]
    [InlineData("feed", "feed.fetch")]
    [InlineData("parser", "parser.parse")]
    [InlineData("filter", "filter.all")]
    [InlineData("rename", "rename.plan")]
    [InlineData("schedule", "schedule.execute")]
    public void OperationsExposeTheExactTypedAdapterContract(string type, string operation)
    {
        Assert.Equal(operation, ExternalPluginOperations.ForType(type));
    }

    [Fact]
    public async Task HelpAndUsageErrorsHaveStableChannelsAndExitCodes()
    {
        var help = await PluginToolTestDriver.InvokeAsync(["--help"]);
        var invalid = await PluginToolTestDriver.InvokeAsync(["validate"]);
        var duplicate = await PluginToolTestDriver.InvokeAsync(
            ["validate", "package", "--rid", "win-x64", "--rid", "win-x64"]);

        Assert.Equal(0, help.ExitCode);
        Assert.Contains("animego-plugin run", help.Output, StringComparison.Ordinal);
        Assert.Empty(help.Error);
        Assert.Equal(2, invalid.ExitCode);
        Assert.Empty(invalid.Output);
        Assert.Equal("plugin_tool_package_required", invalid.ErrorJson.GetProperty("code").GetString());
        Assert.Equal(2, duplicate.ExitCode);
        Assert.Equal("plugin_tool_option_duplicate", duplicate.ErrorJson.GetProperty("code").GetString());
        Assert.DoesNotContain("stack", invalid.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateReturnsManifestAndCanonicalTreeAudit()
    {
        using var package = new PluginToolTestPackage();

        var result = await PluginToolTestDriver.InvokeAsync(
            ["validate", package.PackagePath, "--rid", package.Rid]);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        var outputPackage = result.OutputJson.GetProperty("package");
        Assert.Equal("com.example.filter", outputPackage.GetProperty("id").GetString());
        Assert.Equal(3, outputPackage.GetProperty("fileCount").GetInt32());
        Assert.True(outputPackage.GetProperty("totalBytes").GetInt64() > 0);
        Assert.Equal(64, outputPackage.GetProperty("contentSha256").GetString()!.Length);
    }

    [Fact]
    public async Task ValidateReturnsManifestFailureWithoutLeakingExceptionDetails()
    {
        using var package = new PluginToolTestPackage();
        package.WriteManifest("01.0.0");

        var result = await PluginToolTestDriver.InvokeAsync(
            ["validate", package.PackagePath]);

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("plugin_version_invalid", result.ErrorJson.GetProperty("code").GetString());
        Assert.DoesNotContain("System.", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateMapsMissingPackageAndCancellationToStableExitCodes()
    {
        using var package = new PluginToolTestPackage();
        var missing = Path.Combine(package.RootPath, "missing");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var missingResult = await PluginToolTestDriver.InvokeAsync(["validate", missing]);
        var canceledResult = await PluginToolTestDriver.InvokeAsync(
            ["validate", package.PackagePath],
            cancellationToken: cancellation.Token);

        Assert.Equal(3, missingResult.ExitCode);
        Assert.Equal(
            "plugin_manifest_missing",
            missingResult.ErrorJson.GetProperty("code").GetString());
        Assert.Equal(130, canceledResult.ExitCode);
        Assert.Equal(
            "plugin_tool_canceled",
            canceledResult.ErrorJson.GetProperty("code").GetString());
    }

    [Fact]
    public async Task RunValidatesConfigThenExecutesTypedLifecycleAndCleansOwnedData()
    {
        using var package = new PluginToolTestPackage(
            schema: """
                {
                  "type": "object",
                  "additionalProperties": false,
                  "properties": { "mode": { "type": "string", "enum": ["safe"] } },
                  "required": ["mode"]
                }
                """);
        var fixture = package.WriteFilterFixture("{\"mode\":\"safe\"}");
        var factory = new RecordingSessionFactory(ValidFilterResult);
        var application = new PluginToolApplication(sessionFactory: factory);

        var result = await PluginToolTestDriver.InvokeAsync(
            [
                "run", package.PackagePath,
                "--fixture", fixture,
                "--timeout-seconds", "37",
            ],
            application);

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.OutputJson.GetProperty("healthy").GetBoolean());
        Assert.Equal(1, factory.CreateCount);
        Assert.True(factory.Session.DataPathExistedAtCreation);
        Assert.True(factory.Session.Started);
        Assert.True(factory.Session.Executed);
        Assert.True(factory.Session.HealthChecked);
        Assert.True(factory.Session.Shutdown);
        Assert.True(factory.Session.Disposed);
        Assert.Equal("filter.all", factory.Session.Operation);
        Assert.Equal("safe", factory.Session.Config.GetProperty("mode").GetString());
        Assert.Equal(TimeSpan.FromSeconds(37), factory.Session.ExecuteTimeout);
        Assert.False(Directory.Exists(factory.DataPath));
    }

    [Fact]
    public async Task RunRejectsInvalidConfigAndOperationBeforeStartingProcess()
    {
        using var package = new PluginToolTestPackage(
            schema: """
                {
                  "type": "object",
                  "additionalProperties": false,
                  "properties": { "requiredValue": { "type": "string" } },
                  "required": ["requiredValue"]
                }
                """);
        var invalidConfig = package.WriteFilterFixture("{}", "bad-config.json");
        var invalidOperation = package.WriteFixture(
            "{\"operation\":\"filter.custom\",\"payload\":{},\"config\":{}}",
            "bad-operation.json");
        var factory = new RecordingSessionFactory(ValidFilterResult);
        var application = new PluginToolApplication(sessionFactory: factory);

        var configResult = await PluginToolTestDriver.InvokeAsync(
            ["run", package.PackagePath, "--fixture", invalidConfig],
            application);
        var operationResult = await PluginToolTestDriver.InvokeAsync(
            ["run", package.PackagePath, "--fixture", invalidOperation],
            application);

        Assert.Equal(4, configResult.ExitCode);
        Assert.Equal("plugin_config_invalid", configResult.ErrorJson.GetProperty("code").GetString());
        Assert.Equal(4, operationResult.ExitCode);
        Assert.Equal(
            "plugin_fixture_operation_mismatch",
            operationResult.ErrorJson.GetProperty("code").GetString());
        Assert.Equal(0, factory.CreateCount);
    }

    [Fact]
    public async Task RunRejectsTypedPayloadAndPackageContainedDataBeforeStartingProcess()
    {
        using var package = new PluginToolTestPackage();
        var invalidPayload = package.WriteFixture(
            """
                {
                  "operation": "filter.all",
                  "payload": {
                    "sourceProfileId": "fixture",
                    "items": [],
                    "arguments": {},
                    "sourceProfileSnapshot": null,
                    "unknown": true
                  },
                  "config": {}
                }
                """,
            "bad-payload.json");
        var validFixture = package.WriteFilterFixture(name: "valid.json");
        var factory = new RecordingSessionFactory(ValidFilterResult);
        var application = new PluginToolApplication(sessionFactory: factory);

        var payloadResult = await PluginToolTestDriver.InvokeAsync(
            ["run", package.PackagePath, "--fixture", invalidPayload],
            application);
        var dataResult = await PluginToolTestDriver.InvokeAsync(
            [
                "run", package.PackagePath,
                "--fixture", validFixture,
                "--data-path", Path.Combine(package.PackagePath, "data"),
            ],
            application);

        Assert.Equal(4, payloadResult.ExitCode);
        Assert.Equal(
            "plugin_fixture_payload_invalid",
            payloadResult.ErrorJson.GetProperty("code").GetString());
        Assert.Equal(5, dataResult.ExitCode);
        Assert.Equal(
            "plugin_data_path_inside_package",
            dataResult.ErrorJson.GetProperty("code").GetString());
        Assert.Equal(0, factory.CreateCount);
        Assert.False(Directory.Exists(Path.Combine(package.PackagePath, "data")));
    }

    [Fact]
    public async Task RunRejectsInvalidUtf8DuplicateAndUnknownFixtureFields()
    {
        using var package = new PluginToolTestPackage();
        var invalidUtf8 = Path.Combine(package.RootPath, "invalid-utf8.json");
        await File.WriteAllBytesAsync(invalidUtf8, [0xc3, 0x28]);
        var duplicate = package.WriteFixture(
            "{\"operation\":\"filter.all\",\"operation\":\"filter.all\",\"payload\":{},\"config\":{}}",
            "duplicate.json");
        var unknown = package.WriteFixture(
            "{\"operation\":\"filter.all\",\"payload\":{},\"config\":{},\"unknown\":true}",
            "unknown.json");

        var utf8Result = await PluginToolTestDriver.InvokeAsync(
            ["run", package.PackagePath, "--fixture", invalidUtf8]);
        var duplicateResult = await PluginToolTestDriver.InvokeAsync(
            ["run", package.PackagePath, "--fixture", duplicate]);
        var unknownResult = await PluginToolTestDriver.InvokeAsync(
            ["run", package.PackagePath, "--fixture", unknown]);

        Assert.Equal("plugin_fixture_utf8_invalid", utf8Result.ErrorJson.GetProperty("code").GetString());
        Assert.Equal("plugin_fixture_duplicate_field", duplicateResult.ErrorJson.GetProperty("code").GetString());
        Assert.Equal("plugin_fixture_invalid", unknownResult.ErrorJson.GetProperty("code").GetString());
        Assert.All([utf8Result, duplicateResult, unknownResult], item => Assert.Equal(4, item.ExitCode));
    }

    [Fact]
    public async Task RunRejectsIncompleteTypedFilterResult()
    {
        using var package = new PluginToolTestPackage();
        var fixture = package.WriteFilterFixture();
        var incomplete = PluginToolTestDriver.Json(
            "{\"decisions\":[],\"errors\":[],\"metadata\":{}}");
        var factory = new RecordingSessionFactory(incomplete);

        var result = await PluginToolTestDriver.InvokeAsync(
            ["run", package.PackagePath, "--fixture", fixture],
            new PluginToolApplication(sessionFactory: factory));

        Assert.Equal(5, result.ExitCode);
        Assert.Equal("filter_result_invalid", result.ErrorJson.GetProperty("code").GetString());
        Assert.True(factory.Session.Disposed);
        Assert.False(factory.Session.HealthChecked);
    }

    [Fact]
    public async Task RunRedactsProtocolExceptionMessageAndPreservesExplicitDataPath()
    {
        using var package = new PluginToolTestPackage();
        var fixture = package.WriteFilterFixture();
        var dataPath = Path.Combine(package.RootPath, "persistent-data");
        const string secret = "fixture-password-should-never-be-printed";
        var factory = new RecordingSessionFactory(ValidFilterResult);
        factory.Session.ExecuteException = new ExternalPluginProtocolException(
            "fixture_protocol_failure",
            secret);

        var result = await PluginToolTestDriver.InvokeAsync(
            [
                "run", package.PackagePath,
                "--fixture", fixture,
                "--data-path", dataPath,
            ],
            new PluginToolApplication(sessionFactory: factory));

        Assert.Equal(5, result.ExitCode);
        Assert.Equal("fixture_protocol_failure", result.ErrorJson.GetProperty("code").GetString());
        Assert.DoesNotContain(secret, result.Error, StringComparison.Ordinal);
        Assert.True(Directory.Exists(dataPath));
        Assert.True(factory.Session.Disposed);
    }

    [Fact]
    public async Task RunReturnsStableFailureWhenHealthCheckIsUnhealthy()
    {
        using var package = new PluginToolTestPackage();
        var fixture = package.WriteFilterFixture();
        var factory = new RecordingSessionFactory(ValidFilterResult);
        factory.Session.Healthy = false;

        var result = await PluginToolTestDriver.InvokeAsync(
            ["run", package.PackagePath, "--fixture", fixture],
            new PluginToolApplication(sessionFactory: factory));

        Assert.Equal(5, result.ExitCode);
        Assert.Equal("plugin_health_unhealthy", result.ErrorJson.GetProperty("code").GetString());
        Assert.False(factory.Session.Shutdown);
        Assert.True(factory.Session.Disposed);
    }
}
