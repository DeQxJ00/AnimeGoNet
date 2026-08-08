using AnimeGoNet.App.Api;
using AnimeGoNet.App.Serialization;
using System.Reflection;

namespace AnimeGoNet.App.Tests.Api;

public sealed class ApiJsonSourceGenerationTests
{
    [Fact]
    public void EveryPublicApiContractHasGeneratedJsonMetadata()
    {
        Type[] contractTypes = typeof(RuntimeStatus).Assembly
            .GetExportedTypes()
            .Where(static type =>
                type.Namespace == typeof(RuntimeStatus).Namespace &&
                !type.IsAbstract &&
                !type.ContainsGenericParameters &&
                (type.IsClass || type.IsValueType))
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(contractTypes);

        string[] missingTypes = contractTypes
            .Where(static type => ApiJsonContext.Default.GetTypeInfo(type) is null)
            .Select(static type => type.FullName!)
            .ToArray();

        Assert.True(
            missingTypes.Length == 0,
            $"API contracts missing from {nameof(ApiJsonContext)}: {string.Join(", ", missingTypes)}");
    }

    [Fact]
    public void EveryClosedApiContractInEndpointSignaturesHasGeneratedJsonMetadata()
    {
        HashSet<Type> signatureTypes = [];

        foreach (MethodInfo method in typeof(ApiEndpoints).GetMethods(
                     BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
        {
            CollectApiContractTypes(method.ReturnType, signatureTypes);
            foreach (ParameterInfo parameter in method.GetParameters())
            {
                CollectApiContractTypes(parameter.ParameterType, signatureTypes);
            }
        }

        Assert.Contains(typeof(LegacyApiResponse<PingData>), signatureTypes);

        string[] missingTypes = signatureTypes
            .Where(static type => ApiJsonContext.Default.GetTypeInfo(type) is null)
            .Select(static type => type.ToString())
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            missingTypes.Length == 0,
            $"Endpoint signature contracts missing from {nameof(ApiJsonContext)}: " +
            string.Join(", ", missingTypes));
    }

    private static void CollectApiContractTypes(Type type, HashSet<Type> contractTypes)
    {
        if (type.IsByRef || type.IsPointer || type.IsArray)
        {
            CollectApiContractTypes(type.GetElementType()!, contractTypes);
            return;
        }

        if (!type.ContainsGenericParameters &&
            type.Namespace == typeof(RuntimeStatus).Namespace &&
            type.DeclaringType is null &&
            type != typeof(ApiEndpoints))
        {
            contractTypes.Add(type);
        }

        if (!type.IsGenericType)
        {
            return;
        }

        foreach (Type argument in type.GetGenericArguments())
        {
            CollectApiContractTypes(argument, contractTypes);
        }
    }
}
