using System.Text;
using AnimeGoNet.App.Metadata;
using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.App.Library;

internal static class SubtitleAiPrompt
{
    public const string Version = "subtitle-v1";

    public const string Template = """
        你是字幕文件 EP 匹配器。只根据作品名和字幕文件名判断字幕对应的普通正片集数。
        必须返回标准匹配 JSON：顶层包含 matched、tmdb_id、files、reason；files 中每个对象包含 name、matched、season、episode、reason。
        name 必须与输入文件名原样一致且每个只出现一次；无法可靠判断时 matched=false、episode=null，但仍填写已确认的普通 season。
        必须返回 tmdb_id 和 season，以便主程序验证；不要返回 title、confidence 或其他额外字段。
        必须使用 TMDB MCP 验证作品和集数；工具不可用时说明无法确认。
        {{#TMDB_MCP}}TMDB MCP 已启用，请查询并验证对应的 Series、Season 和 Episode。{{/TMDB_MCP}}
        {{#BGM_MCP}}Bangumi MCP 可作为作品名辅助参考。{{/BGM_MCP}}
        {{#ANIDB_LOOKUP}}AniDB 可作为作品名辅助参考。{{/ANIDB_LOOKUP}}
        {{#IMDB_LOOKUP}}IMDb 可作为作品名辅助参考。{{/IMDB_LOOKUP}}
        {{#BANGUMI_PUBDATE_FIRST}}Bangumi 日期优先候选仅作辅助，不得直接当作 Episode。{{/BANGUMI_PUBDATE_FIRST}}
        作品名：{{SOURCE_TITLE_JSON}}
        字幕文件：{{FILES_JSON}}
        作品级参考：bgmid={{OPTIONAL_BGM_ID_JSON}}，anidbid={{OPTIONAL_ANIDB_ID_JSON}}，imdbid={{OPTIONAL_IMDB_ID_JSON}}
        文件数量：{{TORRENT_FILE_COUNT_JSON}}，发布日期：{{OPTIONAL_PUBLISHED_AT_JSON}}
        Bangumi EP 候选：{{OPTIONAL_BGM_EPISODE_CANDIDATE_JSON}}，日期优先={{USE_BANGUMI_PUBDATE_FIRST_JSON}}
        输出示例：{"matched":true,"tmdb_id":12345,"files":[{"name":"01.zh.ass","matched":true,"season":1,"episode":1,"reason":null}],"reason":null}
        """;
}

public sealed record SubtitleAiPromptSettings(
    string PromptVersion,
    string Template,
    string DefaultTemplate,
    int MaximumLength,
    bool Customized);

public sealed record SubtitleAiPromptUpdate(string Template);

public sealed class SubtitleAiPromptStore : IDisposable
{
    private const string FileName = "subtitle-ai-prompt.txt";
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SubtitleAiPromptStore(DirectoryLayout layout)
    {
        Directory.CreateDirectory(layout.ConfigurationPath);
        _path = Path.Combine(layout.ConfigurationPath, FileName);
    }

    public void Dispose() => _gate.Dispose();

    public async Task<string> GetTemplateAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path))
            {
                return SubtitleAiPrompt.Template;
            }

            var value = await File.ReadAllTextAsync(_path, Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(value) ? SubtitleAiPrompt.Template : value;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SubtitleAiPromptSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var template = await GetTemplateAsync(cancellationToken).ConfigureAwait(false);
        return new(
            SubtitleAiPrompt.Version,
            template,
            SubtitleAiPrompt.Template,
            AiMetadataPromptRenderer.MaximumTemplateLength,
            !string.Equals(template, SubtitleAiPrompt.Template, StringComparison.Ordinal));
    }

    public async Task<SubtitleAiPromptSettings> SaveAsync(
        string template,
        CancellationToken cancellationToken = default)
    {
        template = template.Replace("\r\n", "\n", StringComparison.Ordinal);
        AiMetadataPromptRenderer.ValidateTemplate(template);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await File.WriteAllTextAsync(_path, template, new UTF8Encoding(false), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        return new(
            SubtitleAiPrompt.Version,
            template,
            SubtitleAiPrompt.Template,
            AiMetadataPromptRenderer.MaximumTemplateLength,
            !string.Equals(template, SubtitleAiPrompt.Template, StringComparison.Ordinal));
    }

    public async Task<SubtitleAiPromptSettings> ResetAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
        finally
        {
            _gate.Release();
        }

        return new(
            SubtitleAiPrompt.Version,
            SubtitleAiPrompt.Template,
            SubtitleAiPrompt.Template,
            AiMetadataPromptRenderer.MaximumTemplateLength,
            false);
    }
}
