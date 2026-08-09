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
        Assert.Contains("1525/1525", audit, StringComparison.Ordinal);
        Assert.Contains("U2/TTG 首版暂缓", audit, StringComparison.Ordinal);
        Assert.Contains("Docker 实跑 `[~]`", audit, StringComparison.Ordinal);
        Assert.Contains("Mikan 真实数据完整链", audit, StringComparison.Ordinal);
    }
}
