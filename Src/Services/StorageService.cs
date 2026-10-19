using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PlaylistEditorX.Services;

public class StorageService : IStorageService
{
    private readonly Window _mainWindow;

    // Pass your main window reference during app startup
    public StorageService(Window mainWindow)
    {
        _mainWindow = mainWindow;
    }

    public async Task<IReadOnlyList<string>> SelectFilesAsync(string title, string[] extensions, bool allowMultiple)
    {
        var topLevel = TopLevel.GetTopLevel(_mainWindow);
        if (topLevel == null) return System.Array.Empty<string>();

        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = allowMultiple, // Enabled multi-select as requested
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Allowed Files") { Patterns = extensions }
            }
        };

        // Open picker dialog
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(options);

        // Extract the absolute file paths using the TryGetLocalPath extension method
        return files
            .Select(file => file.TryGetLocalPath())
            .Where(path => path != null) // Filters out empty/unsupported sandboxed paths
            .ToList()!;
    }

    public async Task<(Stream Stream, string Name)?> OpenFileAsync(string title, string[] extensions)
    {
        var topLevel = TopLevel.GetTopLevel(_mainWindow);
        if (topLevel == null) return null;

        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Allowed Files") { Patterns = extensions }
            }
        };

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(options);

        if (files.Any())
        {
            var file = files[0];
            var stream = await file.OpenReadAsync();
            return (stream, file.Name);
        }

        return null;
    }

    public async Task CopyTextToClipboardAsync(string text)
    {
        var topLevel = TopLevel.GetTopLevel(_mainWindow);
        if (topLevel?.Clipboard is not null)
        {
            await topLevel.Clipboard.SetTextAsync(text);
        }
    }
}
