
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;


namespace PlaylistEditorX.Services;

public interface IStorageService
{
    // Returns a stream and the file name, or null if cancelled
    Task<(Stream Stream, string Name)?> OpenFileAsync(string title, string[] extensions);

    // Returns a list of absolute file paths, or an empty list if cancelled
    Task<IReadOnlyList<string>> SelectFilesAsync(string title, string[] extensions, bool allowMultiple);

    Task CopyTextToClipboardAsync(string text);
}
