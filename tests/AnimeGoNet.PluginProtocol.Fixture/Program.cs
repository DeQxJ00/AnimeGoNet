using System.Text.Json;
using System.Text.Json.Nodes;

const string PluginIdVariable = "ANIMEGO_PLUGIN_ID";
const string ApiVersionVariable = "ANIMEGO_PLUGIN_API_VERSION";
const string DataPathVariable = "ANIMEGO_PLUGIN_DATA_PATH";
const string InheritedSecretVariable = "ANIMEGO_PROTOCOL_TEST_SECRET";

while (await Console.In.ReadLineAsync() is { } line)
{
    JsonObject? request;
    try
    {
        request = JsonNode.Parse(line) as JsonObject;
    }
    catch (JsonException)
    {
        return 20;
    }

    if (request is null
        || request["apiVersion"]?.GetValue<int>() is not { } apiVersion
        || request["requestId"]?.GetValue<string>() is not { } requestId
        || request["method"]?.GetValue<string>() is not { } method)
    {
        return 21;
    }

    JsonNode result;
    switch (method)
    {
        case "initialize":
        {
            var payload = request["payload"] as JsonObject;
            if (payload is null)
            {
                return 22;
            }
            result = new JsonObject
            {
                ["pluginId"] = payload["pluginId"]?.DeepClone(),
                ["pluginVersion"] = payload["pluginVersion"]?.DeepClone(),
                ["apiVersion"] = payload["apiVersion"]?.DeepClone(),
                ["type"] = payload["type"]?.DeepClone(),
                ["capabilities"] = payload["capabilities"]?.DeepClone(),
            };
            break;
        }
        case "execute":
            result = new JsonObject
            {
                ["operation"] = request["operation"]?.DeepClone(),
                ["pluginId"] = Environment.GetEnvironmentVariable(PluginIdVariable),
                ["apiVersion"] = Environment.GetEnvironmentVariable(ApiVersionVariable),
                ["dataPath"] = Environment.GetEnvironmentVariable(DataPathVariable),
                ["inheritedSecret"] = Environment.GetEnvironmentVariable(InheritedSecretVariable),
                ["environmentKeys"] = new JsonArray(
                    Environment.GetEnvironmentVariables().Keys
                        .Cast<object>()
                        .Select(key => key.ToString())
                        .Where(key => key is not null)
                        .Order(StringComparer.Ordinal)
                        .Select(key => JsonValue.Create(key))
                        .ToArray()),
            };
            break;
        case "health":
            result = new JsonObject { ["healthy"] = true };
            break;
        case "shutdown":
            result = new JsonObject { ["accepted"] = true };
            break;
        default:
            await WriteAsync(new JsonObject
            {
                ["apiVersion"] = apiVersion,
                ["requestId"] = requestId,
                ["ok"] = false,
                ["error"] = new JsonObject
                {
                    ["code"] = "unknown_method",
                    ["message"] = "The fixture does not recognize the method.",
                },
            });
            continue;
    }

    await WriteAsync(new JsonObject
    {
        ["apiVersion"] = apiVersion,
        ["requestId"] = requestId,
        ["ok"] = true,
        ["result"] = result,
    });
    if (method == "shutdown")
    {
        return 0;
    }
}

return 0;

static async Task WriteAsync(JsonObject response)
{
    await Console.Out.WriteLineAsync(response.ToJsonString());
    await Console.Out.FlushAsync();
}
