namespace AnimeGoNet.App.Tests.Delivery;

public sealed class ImplementationCompletionAuditContractTests
{
    [Fact]
    public void FirstReleaseHasNoOpenImplementationStatusAndPublishesTheAudit()
    {
        var root = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var todo = File.ReadAllText(Path.Combine(root, "TODO.md"));
        var checklist = File.ReadAllText(Path.Combine(root, "docs", "PORTING_CHECKLIST.md"));
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        var auditPath = Path.Combine(root, "docs", "IMPLEMENTATION_COMPLETION_AUDIT.md");

        var openTodoLines = todo.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("- [ ]", StringComparison.Ordinal)
                || line.StartsWith("- [>]", StringComparison.Ordinal)
                || line.StartsWith("- [!]", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(openTodoLines);
        Assert.DoesNotContain("| 待实现 |", checklist, StringComparison.Ordinal);
        Assert.DoesNotContain("| 进行中 |", checklist, StringComparison.Ordinal);
        Assert.Contains("(docs/IMPLEMENTATION_COMPLETION_AUDIT.md)", readme, StringComparison.Ordinal);
        Assert.True(File.Exists(auditPath));

        var audit = File.ReadAllText(auditPath);
        Assert.Contains("1613/1613", audit, StringComparison.Ordinal);
        Assert.Contains("U2 首版暂缓", audit, StringComparison.Ordinal);
        Assert.Contains("Ubuntu 24.04 x86_64 CT", audit, StringComparison.Ordinal);
        Assert.Contains("固定上游 Go Linux amd64 基线已于 2026-08-11 通过", audit, StringComparison.Ordinal);
        Assert.DoesNotContain("arm64/macOS 平台、上游 Go 基线", audit, StringComparison.Ordinal);
        Assert.Contains("linux-arm64", audit, StringComparison.Ordinal);
        Assert.Contains("Mikan 真实数据完整链", audit, StringComparison.Ordinal);
    }
}
