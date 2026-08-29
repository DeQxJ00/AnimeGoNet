using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Diagnostics;

namespace AnimeGoNet.App.Library;

public sealed record MovieFileRoleChangePlan(
    string CurrentMainPath,
    string SelectedSourcePath,
    string SelectedMainPath,
    string FormerMainPath);

public sealed class MovieFileRoleEditException(string code, string message, Exception? innerException = null)
    : IOException(message, innerException)
{
    public string Code { get; } = StableErrorCode.Require(code, nameof(code));
}

public sealed class MovieFileRoleEditor
{
    public MovieFileRoleChangePlan Plan(
        string movieRoot,
        string currentMainPath,
        string selectedSourcePath)
    {
        var root = Path.GetFullPath(movieRoot);
        var current = Path.GetFullPath(currentMainPath);
        var selected = Path.GetFullPath(selectedSourcePath);
        if (!PathBoundary.IsWithin(root, current) || !PathBoundary.IsWithin(root, selected))
        {
            throw new MovieFileRoleEditException(
                "library_movie_file_outside_root",
                "Movie file role changes are limited to the configured Movie library root.");
        }

        var movieDirectory = Path.GetDirectoryName(current);
        if (string.IsNullOrWhiteSpace(movieDirectory)
            || !PathBoundary.IsWithin(movieDirectory, selected))
        {
            throw new MovieFileRoleEditException(
                "library_movie_file_outside_directory",
                "The selected main file does not belong to the current Movie directory.");
        }

        if (!EntryExists(current))
        {
            throw new MovieFileRoleEditException(
                "library_movie_main_file_missing",
                "The current Movie main file is missing and cannot be reclassified safely.");
        }

        if (!EntryExists(selected))
        {
            throw new MovieFileRoleEditException(
                "library_movie_selected_file_missing",
                "The selected Movie file is missing.");
        }

        if (SamePath(current, selected))
        {
            return new MovieFileRoleChangePlan(current, selected, current, selected);
        }

        var selectedMain = Path.Combine(movieDirectory, Path.GetFileName(selected));
        if (EntryExists(selectedMain)
            && !SamePath(selectedMain, current)
            && !SamePath(selectedMain, selected))
        {
            throw new MovieFileRoleEditException(
                "library_movie_main_target_conflict",
                "A different file already occupies the selected main filename in the Movie directory.");
        }

        var extrasDirectory = Path.Combine(movieDirectory, "Extras");
        var formerMain = AvailableFormerMainPath(
            extrasDirectory,
            Path.GetFileName(current),
            selected);
        return new MovieFileRoleChangePlan(current, selected, selectedMain, formerMain);
    }

    public Task ApplyAsync(
        MovieFileRoleChangePlan plan,
        CancellationToken cancellationToken = default) =>
        SwapAsync(
            plan.SelectedSourcePath,
            plan.CurrentMainPath,
            plan.SelectedMainPath,
            plan.FormerMainPath,
            cancellationToken);

    public Task RollbackAsync(
        MovieFileRoleChangePlan plan,
        CancellationToken cancellationToken = default) =>
        SwapAsync(
            plan.SelectedMainPath,
            plan.FormerMainPath,
            plan.SelectedSourcePath,
            plan.CurrentMainPath,
            cancellationToken);

    private static Task SwapAsync(
        string selectedSource,
        string formerMainSource,
        string selectedTarget,
        string formerMainTarget,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var movieDirectory = Path.GetDirectoryName(formerMainSource)!;
        Directory.CreateDirectory(Path.GetDirectoryName(formerMainTarget)!);
        var temporary = Path.Combine(
            movieDirectory,
            $".animegonet-movie-role-{Guid.NewGuid():N}.swap");
        var selectedInTemporary = false;
        var formerMainMoved = false;
        var selectedCommitted = false;
        try
        {
            File.Move(selectedSource, temporary, overwrite: false);
            selectedInTemporary = true;
            File.Move(formerMainSource, formerMainTarget, overwrite: false);
            formerMainMoved = true;
            File.Move(temporary, selectedTarget, overwrite: false);
            selectedInTemporary = false;
            selectedCommitted = true;
            return Task.CompletedTask;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            try
            {
                if (selectedCommitted && EntryExists(selectedTarget) && !EntryExists(temporary))
                {
                    File.Move(selectedTarget, temporary, overwrite: false);
                    selectedInTemporary = true;
                }

                if (formerMainMoved && EntryExists(formerMainTarget) && !EntryExists(formerMainSource))
                {
                    File.Move(formerMainTarget, formerMainSource, overwrite: false);
                }

                if (selectedInTemporary && EntryExists(temporary) && !EntryExists(selectedSource))
                {
                    File.Move(temporary, selectedSource, overwrite: false);
                }
            }
            catch (Exception rollbackException) when (rollbackException is IOException or UnauthorizedAccessException)
            {
                throw new MovieFileRoleEditException(
                    "library_movie_file_role_rollback_failed",
                    "Movie file role update failed and the filesystem rollback also failed.",
                    new AggregateException(exception, rollbackException));
            }

            throw new MovieFileRoleEditException(
                exception is UnauthorizedAccessException
                    ? "library_movie_file_access_denied"
                    : "library_movie_file_role_move_failed",
                "Movie file role update could not move the selected files safely.",
                exception);
        }
    }

    private static string AvailableFormerMainPath(
        string extrasDirectory,
        string fileName,
        string selectedSource)
    {
        var first = Path.Combine(extrasDirectory, fileName);
        if (!EntryExists(first) || SamePath(first, selectedSource))
        {
            return first;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var suffix = 2; suffix <= 999; suffix++)
        {
            var candidate = Path.Combine(extrasDirectory, $"{stem} (former main {suffix}){extension}");
            if (!EntryExists(candidate) || SamePath(candidate, selectedSource))
            {
                return candidate;
            }
        }

        throw new MovieFileRoleEditException(
            "library_movie_extras_target_exhausted",
            "No available Extras filename could be reserved for the former Movie main file.");
    }

    private static bool EntryExists(string path)
    {
        var info = new FileInfo(path);
        try
        {
            return info.Exists || info.LinkTarget is not null;
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or NotSupportedException)
        {
            return false;
        }
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
}
