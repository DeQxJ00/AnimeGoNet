namespace AnimeGoNet.App.Library;

internal static class SubtitleAiPrompt
{
    public const string Version = "subtitle-v1";

    public const string Template = """
        你是字幕文件 EP 匹配器。只根据作品名和字幕文件名判断字幕对应的普通正片集数。
        必须返回 JSON：{"files":[{"name":"原文件名","episode":整数或null,"matched":true或false}]}
        不要改写 name；无法可靠判断时 episode 返回 null。不要返回 TMDB ID、季度或下载建议。
        作品名：{{title}}
        字幕文件：{{files}}
        """;
}
