namespace AnimeGoNet.Core.Rules;

public static class MikanRssRuleDefaults
{
    public static MikanRssRuleSet Create() => new(
        Whitelist: [],
        Blacklist:
        [
            new NamedMatchArray("resolution-720p", "720p", true, ["1280x720", "720p"]),
        ],
        PriorityGroups:
        [
            new PriorityGroup(
                "subtitle-language",
                "字幕语言",
                [
                    new NamedMatchArray("simplified", "简体", true, ["简体", "简繁日", "简日"]),
                    new NamedMatchArray("traditional", "繁体", true, ["繁体", "繁中", "简繁日", "繁日"]),
                ]),
            new PriorityGroup(
                "subtitle-packaging",
                "字幕封装",
                [
                    new NamedMatchArray("external", "外挂", true, ["外挂"]),
                    new NamedMatchArray("soft", "内封", true, ["内挂", "内封"]),
                    new NamedMatchArray("hard", "内嵌", true, ["内嵌"]),
                ]),
            new PriorityGroup(
                "video-codec",
                "视频编码",
                [
                    new NamedMatchArray("h265", "H.265", true, ["h265", "hevc"]),
                    new NamedMatchArray("h264", "H.264", true, ["h264", "x264"]),
                ]),
            new PriorityGroup(
                "resolution",
                "分辨率",
                [
                    new NamedMatchArray("1080p", "1080p", true, ["1920x1080", "1080p"]),
                    new NamedMatchArray("720p", "720p", true, ["1280x720", "720p"]),
                ]),
        ]);
}
