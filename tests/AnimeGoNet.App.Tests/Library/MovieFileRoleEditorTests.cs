using AnimeGoNet.App.Library;

namespace AnimeGoNet.App.Tests.Library;

public sealed class MovieFileRoleEditorTests
{
    [Fact]
    public async Task ApplyAndRollbackSwapMainAndExtraWithoutChangingContents()
    {
        var root = CreateRoot();
        try
        {
            var movieDirectory = Path.Combine(root, "Movie (2024)");
            var main = Path.Combine(movieDirectory, "Main.mkv");
            var extra = Path.Combine(movieDirectory, "Extras", "Alternative.mkv");
            Directory.CreateDirectory(Path.GetDirectoryName(extra)!);
            await File.WriteAllBytesAsync(main, [1, 2, 3]);
            await File.WriteAllBytesAsync(extra, [4, 5]);
            var editor = new MovieFileRoleEditor();
            var plan = editor.Plan(root, main, extra);

            await editor.ApplyAsync(plan);

            Assert.False(File.Exists(main));
            Assert.False(File.Exists(extra));
            Assert.Equal([4, 5], await File.ReadAllBytesAsync(plan.SelectedMainPath));
            Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(plan.FormerMainPath));

            await editor.RollbackAsync(plan);

            Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(main));
            Assert.Equal([4, 5], await File.ReadAllBytesAsync(extra));
            Assert.False(File.Exists(plan.SelectedMainPath));
            Assert.False(File.Exists(plan.FormerMainPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PlanRejectsSelectedFileOutsideCurrentMovieDirectory()
    {
        var root = CreateRoot();
        try
        {
            var movieDirectory = Path.Combine(root, "Movie A");
            var main = Path.Combine(movieDirectory, "Main.mkv");
            var otherMovie = Path.Combine(root, "Movie B", "Other.mkv");
            Directory.CreateDirectory(movieDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(otherMovie)!);
            File.WriteAllBytes(main, [1]);
            File.WriteAllBytes(otherMovie, [2]);

            var exception = Assert.Throws<MovieFileRoleEditException>(() =>
                new MovieFileRoleEditor().Plan(root, main, otherMovie));

            Assert.Equal("library_movie_file_outside_directory", exception.Code);
            Assert.True(File.Exists(main));
            Assert.True(File.Exists(otherMovie));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "animegonet-movie-role-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
