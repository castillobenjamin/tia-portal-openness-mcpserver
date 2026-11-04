using System;
using System.IO;
using System.Linq;

namespace TiaPortalMcpServer.Services
{
    /// <summary>
    /// Checks file system targets of project operations before TIA Portal is called.
    /// </summary>
    public static class ProjectPaths
    {
        /// <summary>
        /// Checks a "Save as" target: an absolute path whose parent exists, that is not a file, and that is missing or empty.
        /// </summary>
        public static bool TryValidateSaveAsTarget(string? targetDirectory, out string fullPath, out string errorMessage)
        {
            fullPath = string.Empty;
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(targetDirectory))
            {
                errorMessage = "targetDirectory is required";
                return false;
            }

            try
            {
                if (!Path.IsPathRooted(targetDirectory))
                {
                    errorMessage = $"targetDirectory '{targetDirectory}' must be an absolute path";
                    return false;
                }

                fullPath = Path.GetFullPath(targetDirectory!.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                errorMessage = $"Invalid targetDirectory '{targetDirectory}': {ex.Message}";
                return false;
            }

            if (File.Exists(fullPath))
            {
                errorMessage = $"'{fullPath}' is a file, not a directory";
                return false;
            }

            if (Directory.Exists(fullPath))
            {
                if (Directory.EnumerateFileSystemEntries(fullPath).Any())
                {
                    errorMessage = $"Directory '{fullPath}' is not empty. Choose a new or empty directory";
                    return false;
                }

                return true;
            }

            var parent = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
            {
                errorMessage = $"Parent directory of '{fullPath}' does not exist";
                return false;
            }

            return true;
        }
    }
}
