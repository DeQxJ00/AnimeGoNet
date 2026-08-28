using System.Text;
using AnimeGoNet.App.Metadata;
using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.App.Library;

internal static class SubtitleAiPrompt
{
    public const string Version = "subtitle-v3";

    public const string Template = """
        你是 AnimeGoNet 的字幕文件 Episode 匹配器。主程序已确认 TMDB Series ID 和普通 Season，它们会写在作品上下文中，属于不可修改的固定值。
        你的任务只是判断压缩包内每个字幕文件是否属于该 Season 的某个普通正片 Episode。
        必须返回标准匹配 JSON：顶层只包含 matched、tmdb_id、files、reason；files 中每个对象只包含 file_id、matched、season、episode、reason。
        tmdb_id 必须原样返回已确认的 TMDB Series ID；每个 season 必须原样返回已确认的 Season。不得搜索后改成其他作品或季度。
        name 是字幕在压缩包内的相对路径，只用于判断。输出不得复述或改写 name；每个输入 file_id 必须原样出现且恰好一次，不能遗漏、重复或生成未知 ID，输出顺序不限。
        同一 Episode 可以有简体、繁体、双语或不同格式的多个字幕文件，因此多个文件映射到同一 Episode 是合法的。
        只从路径、文件名、作品上下文和 TMDB 的普通正片 Episode 证据判断；不得把分辨率、年份、CRC、音轨数字（如 5.1）当成集数。
        NCOP、NCED、OP、ED、PV、CM、Menu、Fonts、Scans、SP、特典以及无法可靠确认的文件必须 matched=false、episode=null，并写明 reason；season 仍返回固定 Season。
        顶层 matched 仅表示至少一个文件成功匹配；没有任何文件匹配时必须 matched=false 并填写顶层 reason。
        不要返回 title、confidence、候选列表、解释段落或任何额外字段。
        {{#TMDB_MCP}}TMDB MCP 已启用。必须验证固定 Series、固定 Season，以及每个成功结果对应的普通 Episode；工具失败或查无该 Episode 时不得猜测。{{/TMDB_MCP}}
        {{#BGM_MCP}}Bangumi MCP 可作为作品名辅助参考。{{/BGM_MCP}}
        {{#ANIDB_LOOKUP}}AniDB 可作为作品名辅助参考。{{/ANIDB_LOOKUP}}
        {{#IMDB_LOOKUP}}IMDb 可作为作品名辅助参考。{{/IMDB_LOOKUP}}
        {{#BANGUMI_PUBDATE_FIRST}}Bangumi 日期优先候选仅作辅助，不得直接当作 Episode。{{/BANGUMI_PUBDATE_FIRST}}
        作品名：{{SOURCE_TITLE_JSON}}
        字幕文件：{{FILES_JSON}}
        作品级参考：bgmid={{OPTIONAL_BGM_ID_JSON}}，anidbid={{OPTIONAL_ANIDB_ID_JSON}}，imdbid={{OPTIONAL_IMDB_ID_JSON}}
        文件数量：{{TORRENT_FILE_COUNT_JSON}}，发布日期：{{OPTIONAL_PUBLISHED_AT_JSON}}
        Bangumi EP 候选：{{OPTIONAL_BGM_EPISODE_CANDIDATE_JSON}}，日期优先={{USE_BANGUMI_PUBDATE_FIRST_JSON}}
        输出示例：{"matched":true,"tmdb_id":12345,"files":[{"file_id":"f0001","matched":true,"season":1,"episode":1,"reason":null},{"file_id":"f0002","matched":true,"season":1,"episode":1,"reason":null},{"file_id":"f0003","matched":false,"season":1,"episode":null,"reason":"non_episode_extra"}],"reason":null}
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
