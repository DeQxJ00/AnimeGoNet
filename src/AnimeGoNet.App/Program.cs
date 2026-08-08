using AnimeGoNet.App;
using AnimeGoNet.App.Configuration;

if (AnimeGoHostCommandLine.TryWriteHelp(args, Console.Out))
{
    return;
}

var app = await AnimeGoApplication.BuildAsync(args);
await app.RunAsync();

public partial class Program;
