using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlaylistEditorX.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace PlaylistEditorX.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly DispatcherTimer _hideStatusTimer;
    private readonly Services.IStorageService _storageService;
    private readonly Services.IDialogService _dialogService;
    private static string ConfigFilePath => "app.config";
    private XDocument? _xspfDoc;
    public static readonly HashSet<string> PlaylistExtensions = new(StringComparer.OrdinalIgnoreCase) { ".txt", ".xspf", ".m3u", ".m3u8" };

    internal MainWindowViewModel(Services.IStorageService storageService, Services.IDialogService dialogService)
    {
        _storageService = storageService;
        _dialogService = dialogService;

        // Status bar initially hidden
        IsStatusBarVisible = false;

        // Timer equivalent of TimerHideStatusbar
        _hideStatusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };

        _hideStatusTimer.Tick += (_, _) =>
        {
            IsStatusBarVisible = false;
            _hideStatusTimer.Stop();
        };

        // Disable commands initially (matching Pascal actXXX.Enabled := false)
        DisableAllCommands();

        // Load config file
        LoadConfig();

        // Handle command-line file open
        TryOpenStartupFile();
    }

    #region == Properties ==

    // These boolean properties control command availability
    [ObservableProperty]
    public partial bool CanSaveITunesTxt { get; set; }

    [ObservableProperty]
    public partial bool CanSaveM3u { get; set; }

    [ObservableProperty]
    public partial bool CanSaveM3uAnsi { get; set; }

    [ObservableProperty]
    public partial bool CanSaveM3uBom { get; set; }

    [ObservableProperty]
    public partial bool CanSaveM3uUtf8 { get; set; }

    [ObservableProperty]
    public partial bool CanSaveXspf { get; set; }

    [ObservableProperty]
    public partial bool CanConvertToM3u { get; set; }

    [ObservableProperty]
    public partial bool CanConvertToXspf { get; set; }

    [ObservableProperty]
    public partial bool CanCopySelectedPath { get; set; }

    [ObservableProperty]
    public partial bool CanOpenContainingFolder { get; set; }

    [ObservableProperty]
    public partial bool CanRemoveSelected { get; set; }

    [ObservableProperty]
    public partial bool CanAddFiles { get; set; }

    public ObservableCollection<PlaylistItem> Items { get; } = [];

    public ObservableCollection<string> RawLines { get; } = [];

    [ObservableProperty]
    public partial PlaylistItem? SelectedItem { get; set; }

    [ObservableProperty]
    public partial PlaylistType CurrentFormat { get; set; } = PlaylistType.New;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsRearrangedOrder { get; set; }

    [ObservableProperty]
    public partial bool IsLocationEdited { get; set; }

    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    [ObservableProperty]
    public partial bool IsCloseRequested { get; set; }

    [ObservableProperty]
    public partial string AppVersion { get; set; } = "1.0.0";

    [ObservableProperty]
    public partial string StatusTextLeft { get; set; } = "New";

    [ObservableProperty]
    public partial string StatusTextRight { get; set; } = "";

    [ObservableProperty]
    public partial bool IsStatusBarVisible { get; set; }

    [ObservableProperty]
    public partial string FindText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ReplaceText { get; set; } = string.Empty;

    #endregion

    #region == Public Methods ==

    #region == Window Lifecycle Methods ==

    public void OnWindowOpened()
    {
        // Restore window state if config exists
        if (ConfigExists())
        {
            RestoreFormState();
        }

        // (Optional) Welcome dialog logic can be added later
    }

    public void OnWindowClosing()
    {
        // Save window state
        StoreFormState();
    }

    public void OnWindowClosed()
    {
        IsCloseRequested = true;

        // Free resources
        Items.Clear();
        //xDocMain = null;
    }

    #endregion

    #region == Path Conversion ==

    public static string UrlEncode(string s)
    {
        var allowed = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_.:/~";
        var sb = new StringBuilder();

        foreach (var ch in s)
        {
            if (allowed.Contains(ch))
            {
                sb.Append(ch);
            }
            else
            {
                sb.Append('%');
                sb.Append(((int)ch).ToString("X2"));
            }
        }

        return sb.ToString();
    }

    public static string UrlDecode(string s)
    {
        var sb = new StringBuilder();

        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '%' && i + 2 < s.Length)
            {
                var hex = s.Substring(i + 1, 2);
                if (byte.TryParse(hex, NumberStyles.HexNumber, null, out var value))
                {
                    sb.Append((char)value);
                    i += 2;
                    continue;
                }
            }

            sb.Append(s[i]);
        }

        return sb.ToString();
    }

    public static string ConvertUriToLocalPath(string strPath)
    {
        string s = strPath;

        if (s.StartsWith("file:///"))
        {
            s = s.Replace("file:///", "");

            if (s.Contains(":/"))
            {
                // Windows drive letter
                s = s.Replace("/", "\\");
            }
            else
            {
                // Linux absolute path
                s = "/" + s;
            }
        }
        else if (s.StartsWith("file://"))
        {
            // Windows UNC
            s = s.Replace("file://", "\\\\");
            s = s.Replace("/", "\\");
        }

        return UrlDecode(s);
    }

    public static string ConvertLocalPathToUri(string strUri)
    {
        // return new Uri(path, UriKind.Absolute).ToString();

        if (string.IsNullOrEmpty(strUri) || strUri.Length < 2)
            return strUri;

        string s = strUri;

        // Windows UNC: \\server\share
        if (s.StartsWith("\\\\"))
        {
            s = s.Replace("\\", "/");
            s = UrlEncode(s);
            return "file:" + s;
        }

        // Windows relative path: \folder\file
        if (s.StartsWith("\\"))
        {
            s = s.Replace("\\", "/");
            return UrlEncode(s);
        }

        // macOS iTunes: Macintosh HD:Users:Toru:Music:...
        if (s.StartsWith("Macintosh HD:"))
        {
            s = s.Replace("Macintosh HD:", "");
            s = s.Replace(":", "/");
            s = UrlEncode(s);
            return "file:///" + s;
        }

        // Windows drive letter: C:\path
        if (s.Length >= 3 && s[1] == ':' && s.Contains("\\"))
        {
            s = s.Replace("\\", "/");
            s = UrlEncode(s);
            return "file:///" + s;
        }

        // Windows relative path with backslashes
        if (s.Contains("\\"))
        {
            s = s.Replace("\\", "/");
            return UrlEncode(s);
        }

        // Already a URI
        if (s.Contains("://") || s.Contains(":/") || s.Contains("//") || s.Contains("#"))
        {
            return s;
        }

        // Unix absolute path: /home/user/file
        if (s.StartsWith("/"))
        {
            s = "file://" + s;
            return UrlEncode(s);
        }

        // Unix relative path or relative URI
        return UrlEncode(s);
    }

    public static string ConvertMacPathToUnixPath(string strUri)
    {
        string s = strUri;

        if (s.StartsWith("Macintosh HD:"))
        {
            s = s.Replace("Macintosh HD:", "");
            s = s.Replace(":", "/");
            s = "/" + s;
        }

        return s;
    }

    #endregion

    public void ReorderItems(PlaylistItem dragged, PlaylistItem? target, bool isDropAbove)
    {
        //Debug.WriteLine($"ReorderItems: dragged={dragged.Text}, target={(target != null ? target.Text : "null")}");

        if (target == null || dragged == null || dragged == target)
            return;

        IsRearrangedOrder = true;
        IsDirty = true;

        int oldIndex = Items.IndexOf(dragged);
        int targetIndex = Items.IndexOf(target);
        
        if (oldIndex == -1 || targetIndex == -1)
            return;

        int newIndex = isDropAbove ? targetIndex : targetIndex + 1;

        if (oldIndex < newIndex)
        {
            newIndex--;
        }

        if (newIndex < 0 || newIndex >= Items.Count || oldIndex == newIndex)
            return;

        Items.RemoveAt(oldIndex);
        Items.Insert(newIndex, dragged);

        // Reindex Items
        for (int i = 0; i < Items.Count; i++)
        {
            Items[i].Index = i + 1;
            //Items[i].OriginalIndex = i + 1;
        }

        // Rebuild RawLines to match new order
        var newRaw = Items.Select(x => RawLines[x.Index - 1]).ToList();

        RawLines.Clear();
        foreach (var line in newRaw)
            RawLines.Add(line);
    }

    #endregion

    #region == Private Methods ==

    #region == Startup File Handling ==

    private void TryOpenStartupFile()
    {
        var args = Environment.GetCommandLineArgs();
        if (args.Length <= 1)
            return;

        var file = args[1];
        var ext = System.IO.Path.GetExtension(file).ToLowerInvariant();

        if (ext == ".m3u" || ext == ".m3u8")
        {
            OpenM3u(file);
        }
        else if (ext == ".xspf")
        {
            OpenXspf(file);
        }
        else if (ext == ".txt")
        {
            OpenITunesTxt(file);
        }
    }

    #endregion

    #region == Config Handling ==

    // Placeholder until you paste the Pascal versions
    private void RestoreFormState() { }
    private void StoreFormState() { }

    private bool ConfigExists()
    {
        return System.IO.File.Exists(ConfigFilePath);
    }

    private void LoadConfig()
    {
        // You will implement this once you paste XMLConfig1 usage
        // For now, placeholder
    }

    #endregion

    #region == Init Utilities ==

    private void DisableAllCommands()
    {
        CanSaveITunesTxt = false;
        CanSaveM3u = false;
        CanSaveM3uAnsi = false;
        CanSaveM3uBom = false;
        CanSaveM3uUtf8 = false;
        CanSaveXspf = false;

        CanConvertToM3u = false;
        CanConvertToXspf = false;

        CanCopySelectedPath = false;
        CanOpenContainingFolder = false;
        CanRemoveSelected = false;
        CanAddFiles = false;
    }

    public void ClearPlaylist()
    {
        RawLines.Clear();
        Items.Clear();
        IsDirty = false;
        IsRearrangedOrder = false;
        IsLocationEdited = false;
        CurrentFormat = PlaylistType.New;
        StatusTextLeft = "New";
        StatusTextRight = "";
        // Disable commands
        DisableAllCommands();
    }

    #endregion

    #region == Edit Utilities ==

    private void ApplyReindexing()
    {
        IsRearrangedOrder = true;
        IsDirty = true;

        for (int i = 0; i < Items.Count; i++)
        {
            Items[i].Index = i + 1;
            //Items[i].OriginalIndex = i + 1;
        }

        var newRaw = Items.Select(x => RawLines[x.Index - 1]).ToList();

        RawLines.Clear();
        foreach (var line in newRaw)
            RawLines.Add(line);
    }

    #endregion

    #endregion

    #region == Commands ==

    #region == Add Files ==

    [RelayCommand]
    public void AddFilesToPlaylist(string[] files)
    {
        if (files == null || files.Length == 0)
            return;

        IsDirty = true;

        switch (CurrentFormat)
        {
            case PlaylistType.ITunesTsv:
                AddFilesITunes(files);
                break;

            case PlaylistType.M3u:
                AddFilesM3u(files);
                break;

            case PlaylistType.Xspf:
                AddFilesXspf(files);
                break;

            case PlaylistType.New:
                AddFilesNew(files);
                break;
        }
    }

    private void AddFilesITunes(string[] files)
    {
        // Convert TSV to NEW format
        var newLines = new List<string>();

        foreach (var item in Items)
        {
            var row = RawLines[item.OriginalIndex].Split('\t');
            if (row.Length >= 31)
            {
                var unix = ConvertMacPathToUnixPath(row[30]);
                newLines.Add(unix);
            }
        }

        RawLines.Clear();
        foreach (var l in newLines)
            RawLines.Add(l);

        Items.Clear();
        for (int i = 0; i < RawLines.Count; i++)
        {
            Items.Add(new PlaylistItem
            {
                Index = i + 1,
                FilePath = RawLines[i],
                OriginalIndex = i + 1
            });
        }

        CurrentFormat = PlaylistType.New;

        // Append new files
        AddFilesNew(files);
    }

    private void AddFilesM3u(string[] files)
    {
        // Rebuild RawLines in current order
        var ordered = Items.Select(x => RawLines[x.OriginalIndex - 1]).ToList();

        RawLines.Clear();
        foreach (var l in ordered)
            RawLines.Add(l);

        Items.Clear();
        for (int i = 0; i < RawLines.Count; i++)
        {
            Items.Add(new PlaylistItem
            {
                Index = i + 1,
                FilePath = RawLines[i],
                OriginalIndex = i + 1
            });
        }

        // Append new files
        AddFilesNew(files);
    }

    private void AddFilesXspf(string[] files)
    {
        if (_xspfDoc == null)
            return;

        var trackList = _xspfDoc.Descendants("trackList").FirstOrDefault();
        if (trackList == null)
            return;

        int index = Items.Count;

        foreach (var file in files)
        {
            var uri = ConvertLocalPathToUri(file);

            var track = new XElement("track",
                new XElement("location", uri)
            );

            trackList.Add(track);

            RawLines.Add(uri);

            Items.Add(new PlaylistItem
            {
                Index = ++index,
                FilePath = uri,
                OriginalIndex = index
            });
        }
    }

    private void AddFilesNew(string[] files)
    {
        foreach (var file in files)
        {
            RawLines.Add(file);

            Items.Add(new PlaylistItem
            {
                Index = RawLines.Count,
                FilePath = file,
                OriginalIndex = RawLines.Count
            });
        }
    }

    #endregion

    #region == Remove ==

    [RelayCommand]
    public void RemoveSelected()
    {
        if (SelectedItem == null)
            return;

        RawLines.RemoveAt(SelectedItem.OriginalIndex - 1);
        Items.Remove(SelectedItem);

        // Reindex
        for (int i = 0; i < Items.Count; i++)
        {
            Items[i].Index = i + 1;
            Items[i].OriginalIndex = i + 1;
        }

        IsDirty = true;
        IsRearrangedOrder = true;
    }


    [RelayCommand]
    private void RemoveFilesFromPlaylist()
    {
    }

    #endregion

    [RelayCommand]
    public void OpenContainingFolder()
    {
        if (SelectedItem == null)
            return;

        var path = SelectedItem.FilePath;

        try
        {
            var local = ConvertUriToLocalPath(path);
            var folder = Path.GetDirectoryName(local);

            if (folder != null && Directory.Exists(folder))
                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch
        {
            // ignore errors
        }
    }

    #region == Find and Replace ==

    [RelayCommand]
    public void ReplaceAll()
    {
        if (string.IsNullOrWhiteSpace(FindText)) return;
        if (string.IsNullOrWhiteSpace(ReplaceText)) return;

        ReplaceAll(FindText, ReplaceText);
    }

    public void ReplaceAll(string find, string replace)
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(find)) return;
        if (RawLines.Count == 0) return;

        IsBusy = true;
        IsLocationEdited = true;
        IsDirty = true;

        StatusTextRight = "";
        StatusTextLeft = "Replacing...";
        IsStatusBarVisible = true;

        Items.Clear();

        switch (CurrentFormat)
        {
            case PlaylistType.ITunesTsv:
                ReplaceAllITunes(find, replace);
                break;

            case PlaylistType.M3u:
                ReplaceAllM3u(find, replace);
                break;

            case PlaylistType.Xspf:
                ReplaceAllXspf(find, replace);
                break;

            case PlaylistType.New:
                ReplaceAllNew(find, replace);
                break;

            case PlaylistType.Error:
                // do nothing
                break;
        }

        StatusTextRight = "";
        IsBusy = false;
    }

    private void ReplaceAllITunes(string find, string replace)
    {
        var newLines = new List<string>();

        for (int line = 0; line < RawLines.Count; line++)
        {
            var row = RawLines[line].Split('\t');

            if (row.Length >= 31)
            {
                row[30] = row[30].Replace(find, replace, StringComparison.Ordinal);

                // Rebuild TSV line
                var rebuilt = string.Join("\t", row);
                newLines.Add(rebuilt);

                Items.Add(new PlaylistItem
                {
                    Index = line + 1,
                    FilePath = row[30],
                    OriginalIndex = line + 1
                });
            }
            else
            {
                Items.Add(new PlaylistItem
                {
                    Index = line + 1,
                    FilePath = "Error",
                    OriginalIndex = line + 1
                });

                newLines.Add(RawLines[line]);
            }

            StatusTextRight = $"Processing... {line + 1}";
        }

        RawLines.Clear();
        foreach (var l in newLines)
            RawLines.Add(l);
    }

    private void ReplaceAllM3u(string find, string replace)
    {
        var newLines = new List<string>();
        int index = 1;

        for (int i = 0; i < RawLines.Count; i++)
        {
            var line = RawLines[i];

            if (!line.StartsWith("#") && !string.IsNullOrWhiteSpace(line))
            {
                var replaced = line.Replace(find, replace, StringComparison.Ordinal);
                newLines.Add(replaced);

                Items.Add(new PlaylistItem
                {
                    Index = index,
                    FilePath = replaced,
                    OriginalIndex = index
                });

                index++;
            }
            else
            {
                newLines.Add(line);
            }

            StatusTextRight = $"Processing... {index}";
        }

        RawLines.Clear();
        foreach (var l in newLines)
            RawLines.Add(l);
    }

    private void ReplaceAllXspf(string find, string replace)
    {
        if (_xspfDoc == null)
            return;

        var newLines = new List<string>();
        int index = 1;

        var tracks = _xspfDoc.Descendants("track");

        foreach (var track in tracks)
        {
            var loc = track.Element("location");
            if (loc == null) continue;

            var newValue = loc.Value.Replace(find, replace, StringComparison.Ordinal);
            loc.Value = newValue;

            newLines.Add(newValue);

            Items.Add(new PlaylistItem
            {
                Index = index,
                FilePath = newValue,
                OriginalIndex = index
            });

            StatusTextRight = $"Processing... [{index}]";
            index++;
        }

        RawLines.Clear();
        foreach (var l in newLines)
            RawLines.Add(l);
    }

    private void ReplaceAllNew(string find, string replace)
    {
        var newLines = new List<string>();

        for (int i = 0; i < RawLines.Count; i++)
        {
            var replaced = RawLines[i].Replace(find, replace, StringComparison.Ordinal);
            newLines.Add(replaced);

            Items.Add(new PlaylistItem
            {
                Index = i + 1,
                FilePath = replaced,
                OriginalIndex = i + 1
            });

            StatusTextRight = $"Processing... {i + 1}";
        }

        RawLines.Clear();
        foreach (var l in newLines)
            RawLines.Add(l);
    }

    #endregion

    #region == Edit Path ==

    [RelayCommand]
    private void EditPathUri()
    {
    }

    [RelayCommand]
    public void EditSelectedPath(string newValue)
    {
        if (SelectedItem == null)
            return;

        int i = SelectedItem.OriginalIndex; // Pascal Column2

        IsDirty = true;
        IsLocationEdited = true;

        switch (CurrentFormat)
        {
            case PlaylistType.ITunesTsv:
                EditITunesPath(i, newValue);
                break;

            case PlaylistType.M3u:
                EditM3uPath(i, newValue);
                break;

            case PlaylistType.Xspf:
                EditXspfPath(i, newValue);
                break;

            case PlaylistType.New:
                EditNewPath(i, newValue);
                break;

            case PlaylistType.Error:
                return;
        }

        // Update UI list
        SelectedItem.FilePath = newValue;
    }

    private void EditITunesPath(int index, string newValue)
    {
        var row = RawLines[index].Split('\t');

        if (row.Length >= 31)
        {
            row[30] = newValue;
            RawLines[index] = string.Join("\t", row);
        }
    }

    private void EditM3uPath(int index, string newValue)
    {
        RawLines[index - 1] = newValue;
    }

    private void EditXspfPath(int index, string newValue)
    {
        if (_xspfDoc == null)
            return;

        var tracks = _xspfDoc.Descendants("track").ToList();
        if (index - 1 >= tracks.Count)
            return;

        var track = tracks[index - 1];
        var loc = track.Element("location");
        loc?.Value = newValue;

        RawLines[index - 1] = newValue;
    }

    private void EditNewPath(int index, string newValue)
    {
        RawLines[index - 1] = newValue;
    }


    [RelayCommand]
    private async Task CopySelectedPath()
    {
        if (SelectedItem == null)
            return;
        var path = SelectedItem.FilePath;
        try
        {
            var local = ConvertUriToLocalPath(path);
            await _storageService.CopyTextToClipboardAsync(local);
        }
        catch
        {
            // ignore errors
        }
    }

    #endregion

    #region = New File ==

    [RelayCommand]
    private async Task NewFileAsync()
    {
        if (IsBusy) return;

        if (IsDirty)
        {
            var confirm = await _dialogService.ShowConfirmAsync(
                "Playlist has been changed",
                "Do you wish to discard changes and proceed?"
            );

            if (!confirm)
                return;
        }

        await _storageService.SelectFilesAsync("Select files to add to new playlist", new[] { "*.*" }, true).ContinueWith(task =>
        {
            if (task.Result != null && task.Result.Count > 0)
            {
                var files = task.Result.ToArray();
                CreateNew(files);
            }
        });

        /*
        await _storageService.OpenFileAsync("Select files to add to new playlist", new[] { "*.txt" }).ContinueWith(task =>
        {
            if (task.Result != null)
            {
                var (stream, name) = task.Result.Value;
                using (stream)
                {
                    // Read the file names from the stream
                    using (var reader = new StreamReader(stream))
                    {
                        var content = reader.ReadToEnd();
                        var file = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                        
                    }
                }
            }
        });
        */
    }

    public void CreateNew(string[] files)
    {
        IsBusy = true;
        StatusTextLeft = "New";
        StatusTextRight = "";
        CurrentFormat = PlaylistType.New;

        RawLines.Clear();
        Items.Clear();

        int index = 1;

        foreach (var file in files)
        {
            Debug.WriteLine(file);
            RawLines.Add(file);

            Items.Add(new PlaylistItem
            {
                Index = index,
                FilePath = file,
                OriginalIndex = index
            });

            StatusTextRight = $"Processing... [{index}/{files.Length}]";
            index++;
        }

        StatusTextRight = "";
        IsBusy = false;

        // Enable/disable commands
        CanSaveM3u = true;
        CanConvertToM3u = true;
        CanConvertToXspf = true;

        CanSaveITunesTxt = false;
        CanSaveXspf = false;
    }

    #endregion

    #region = Open file or Drag and Drop ==

    public async Task FilesDroped(IEnumerable<string?> files)
    {
        if (IsBusy) return;
        if (files is null) return;
        if (!files.Any()) return;

        if (IsDirty)
        {
            var confirm = await _dialogService.ShowConfirmAsync(
                "Playlist has been changed",
                "Do you wish to discard changes and proceed?"
            );

            if (!confirm)
                return;
        }

        ClearPlaylist();

        if (files.Count() == 1)
        {
            //PlaylistExtensions
            var extension = System.IO.Path.GetExtension(files.First());
            if (!string.IsNullOrEmpty(extension))
            {
                if (PlaylistExtensions.Contains(extension))
                {
                    if (extension.Equals(".txt"))
                    {
                        OpenITunesTxt(files.First()!);
                    }
                    else if (extension.Equals(".xspf"))
                    {
                        OpenXspf(files.First()!);
                    }
                    else if (extension.Equals(".m3u") || extension.Equals(".m3u8"))
                    {
                        OpenM3u(files.First()!);
                    }
                    else
                    {
                        AddFilesToPlaylist(files.Where(f => f != null).Select(f => f!).ToArray());
                    }
                }
            }
        }
        else
        {
            AddFilesToPlaylist(files.Where(f => f != null).Select(f => f!).ToArray());
        }

    }

    [RelayCommand]
    private async Task OpenTxtAsync()
    {
        if (IsBusy) return;

        if (IsDirty)
        {
            var confirm = await _dialogService.ShowConfirmAsync(
                "Playlist has been changed",
                "Do you wish to discard changes and proceed?"
            );

            if (!confirm)
                return;
        }

        await _storageService.SelectFilesAsync("Select files to add to new playlist", new[] { "*.*" }, false).ContinueWith(task =>
        {
            if (task.Result != null && task.Result.Count > 0)
            {
                var files = task.Result.ToArray();

                if (!File.Exists(files[0])) return;
                OpenITunesTxt(files[0]);
            }
        });

    }

    public void OpenITunesTxt(string filename)
    {
        IsBusy = true;
        StatusTextLeft = "iTunes";
        StatusTextRight = "";
        CurrentFormat = PlaylistType.ITunesTsv;

        RawLines.Clear();
        Items.Clear();

        // Load UTF-16 → convert to UTF-8
        var text = File.ReadAllText(filename, Encoding.Unicode);
        var utf8 = Encoding.UTF8.GetString(Encoding.Convert(Encoding.Unicode, Encoding.UTF8, Encoding.Unicode.GetBytes(text)));

        var lines = utf8.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        int index = 0;

        foreach (var line in lines)
        {
            if (index == 0)
            {
                index++;
                continue;
            }

            var cols = line.Split('\t');

            if (cols.Length < 31)
            {
                StatusTextRight = "Error: Wrong column count. Invalid format!";
                CurrentFormat = PlaylistType.Error;
                IsBusy = false;
                return;
            }

            var value = cols[30];

            RawLines.Add(value);

            Items.Add(new PlaylistItem
            {
                Index = index,
                FilePath = value,
                OriginalIndex = index
            });

            StatusTextRight = $"Processing... [{index}/{lines.Length}]";
            index++;
        }

        StatusTextRight = "";
        IsBusy = false;

        // Enable/disable commands
        CanSaveITunesTxt = true;
        CanSaveM3u = true;
        CanSaveM3uAnsi = true;
        CanSaveM3uBom = true;
        CanSaveM3uUtf8 = true;

        CanConvertToM3u = true;
        CanConvertToXspf = true;
    }

    [RelayCommand]
    private async Task OpenM3uAsync()
    {
        if (IsBusy) return;
        /*
        if (IsDirty)
        {
            var confirm = await ShowConfirmAsync(
                "Playlist has been changed",
                "Do you wish to discard changes?"
            );

            if (!confirm)
                return;
        }

        var dialog = new OpenFileDialog
        {
            AllowMultiple = false,
            Filters = { new FileDialogFilter { Name = "M3U", Extensions = { "m3u", "m3u8" } } }
        };

        var result = await dialog.ShowAsync(GetWindow());
        if (result == null || result.Length == 0)
            return;

        var file = result[0];
        if (!File.Exists(file)) return;

        OpenM3u(file);
        */
    }

    public void OpenM3u(string filename)
    {
        IsBusy = true;
        StatusTextLeft = "M3U";
        StatusTextRight = "";
        CurrentFormat = PlaylistType.M3u;

        RawLines.Clear();
        Items.Clear();

        var lines = File.ReadAllLines(filename, Encoding.UTF8);

        int index = 1;

        foreach (var line in lines)
        {
            if (line.StartsWith("#") || string.IsNullOrWhiteSpace(line))
                continue;

            RawLines.Add(line);

            Items.Add(new PlaylistItem
            {
                Index = index,
                FilePath = line,
                OriginalIndex = index
            });

            StatusTextRight = $"Processing... [{index}/{lines.Length}]";
            index++;
        }

        StatusTextRight = "";
        IsBusy = false;

        // Enable/disable commands
        CanSaveITunesTxt = false;

        CanSaveM3u = true;
        CanSaveM3uAnsi = true;
        CanSaveM3uBom = true;
        CanSaveM3uUtf8 = true;

        CanConvertToM3u = false;
        CanConvertToXspf = true;
    }

    [RelayCommand]
    private async Task OpenXspfAsync()
    {
        if (IsBusy) return;
        /*
        if (IsDirty)
        {
            var confirm = await ShowConfirmAsync(
                "Playlist has been changed",
                "Do you wish to discard changes?"
            );

            if (!confirm)
                return;
        }

        var dialog = new OpenFileDialog
        {
            AllowMultiple = false,
            Filters = { new FileDialogFilter { Name = "XSPF", Extensions = { "xspf" } } }
        };

        var result = await dialog.ShowAsync(GetWindow());
        if (result == null || result.Length == 0)
            return;

        var file = result[0];
        if (!File.Exists(file)) return;

        OpenXspf(file);
        */
    }

    public void OpenXspf(string filename)
    {
        Debug.WriteLine($"OpenXspf: {filename}");
        IsBusy = true;
        StatusTextLeft = "XSPF";
        StatusTextRight = "";
        CurrentFormat = PlaylistType.Xspf;

        RawLines.Clear();
        Items.Clear();

        try
        {
            var doc = XDocument.Load(filename);
            if (doc.Root is null)
            {
                IsBusy = false;
                StatusTextLeft = "XSPF";
                StatusTextRight = "XSPF file has no root element.";
                Debug.WriteLine("XSPF file has no root element.");
                return;
            }

            XNamespace ns = "http://xspf.org/ns/0/";

            var tracks = doc.Root.Descendants(ns + "track");
            Debug.WriteLine($"Found {tracks.Count()} tracks in XSPF file.");

            int index = 1;

            foreach (var track in tracks)
            {
                var loc = track.Element(ns + "location")?.Value;
                if (loc == null) continue;

                RawLines.Add(loc);

                Items.Add(new PlaylistItem
                {
                    Index = index,
                    FilePath = loc,
                    OriginalIndex = index
                });

                StatusTextRight = $"Processing... [{index}]";
                index++;
            }
        }
        catch (Exception ex)
        {
            StatusTextLeft = "XSPF";
            StatusTextRight = $"Error: {ex.Message}";
            Debug.WriteLine($"Error opening XSPF file: {ex.Message}");
            return;
        }
        finally
        {
            IsBusy = false;
        }


        // Enable/disable commands
        CanSaveITunesTxt = false;

        CanSaveM3u = false;
        CanSaveM3uAnsi = false;
        CanSaveM3uBom = false;
        CanSaveM3uUtf8 = false;

        CanSaveXspf = true;

        CanConvertToM3u = true;
        CanConvertToXspf = false;
    }

    #endregion

    #region = Convert Format ==

    [RelayCommand]
    private void ConvertToXspf()
    {
        IsLocationEdited = true;
        IsDirty = true;

        switch (CurrentFormat)
        {
            case PlaylistType.ITunesTsv:
                ConvertITunesToXspf();
                break;

            case PlaylistType.M3u:
                ConvertM3uToXspf();
                break;

            case PlaylistType.New:
                ConvertNewToXspf();
                break;

            case PlaylistType.Xspf:
                // nothing to do, already XSPF
                break;
        }
    }

    private void ConvertITunesToXspf()
    {
        IsBusy = true;
        StatusTextLeft = "XSPF";
        StatusTextRight = "Converting...";

        // Build XSPF XML
        var doc = new XDocument(
            new XElement("playlist",
                new XAttribute("xmlns", "http://xspf.org/ns/0/"),
                new XElement("trackList")
            )
        );

        var trackList = doc.Root!.Element("trackList")!;

        var newLines = new List<string>();

        // Items are already rearranged; use OriginalIndex to access RawLines
        foreach (var item in Items)
        {
            var line = RawLines[item.OriginalIndex]; // Pascal used Column2 as index into slMain
            var cols = line.Split('\t');

            if (cols.Length < 31)
                continue;

            var path = cols[30];
            var uri = ConvertLocalPathToUri(path);

            newLines.Add(uri);

            var track = new XElement("track",
                new XElement("location", uri)
            );

            trackList.Add(track);
        }

        _xspfDoc = doc;

        // Replace RawLines with URIs
        RawLines.Clear();
        foreach (var uri in newLines)
            RawLines.Add(uri);

        // Rebuild Items
        Items.Clear();
        for (int i = 0; i < RawLines.Count; i++)
        {
            Items.Add(new PlaylistItem
            {
                Index = i + 1,
                FilePath = RawLines[i],
                OriginalIndex = i + 1
            });
        }

        CurrentFormat = PlaylistType.Xspf;
        StatusTextLeft = "XSPF";
        StatusTextRight = "";

        // Command flags
        CanSaveITunesTxt = false;
        CanSaveM3u = false;
        CanSaveM3uAnsi = false;
        CanSaveM3uBom = false;
        CanSaveM3uUtf8 = false;
        CanSaveXspf = true;

        CanConvertToM3u = true;
        CanConvertToXspf = false;

        IsBusy = false;
    }

    private void ConvertM3uToXspf()
    {
        IsBusy = true;
        StatusTextLeft = "XSPF";
        StatusTextRight = "Converting...";

        var doc = new XDocument(
            new XElement("playlist",
                new XAttribute("xmlns", "http://xspf.org/ns/0/"),
                new XElement("trackList")
            )
        );

        var trackList = doc.Root!.Element("trackList")!;
        var newLines = new List<string>();

        foreach (var item in Items)
        {
            var line = RawLines[item.OriginalIndex - 1];

            if (line.StartsWith("#") || string.IsNullOrWhiteSpace(line))
                continue;

            var uri = ConvertLocalPathToUri(line);
            newLines.Add(uri);

            var track = new XElement("track",
                new XElement("location", uri)
            );

            trackList.Add(track);
        }

        _xspfDoc = doc;

        RawLines.Clear();
        foreach (var uri in newLines)
            RawLines.Add(uri);

        Items.Clear();
        for (int i = 0; i < RawLines.Count; i++)
        {
            Items.Add(new PlaylistItem
            {
                Index = i + 1,
                FilePath = RawLines[i],
                OriginalIndex = i + 1
            });
        }

        CurrentFormat = PlaylistType.Xspf;
        StatusTextLeft = "XSPF";
        StatusTextRight = "";

        CanSaveITunesTxt = false;
        CanSaveM3u = false;
        CanSaveM3uAnsi = false;
        CanSaveM3uBom = false;
        CanSaveM3uUtf8 = false;
        CanSaveXspf = true;

        CanConvertToM3u = true;
        CanConvertToXspf = false;

        IsBusy = false;
    }

    private void ConvertNewToXspf()
    {
        IsBusy = true;
        StatusTextLeft = "XSPF";
        StatusTextRight = "Converting...";

        var doc = new XDocument(
            new XElement("playlist",
                new XAttribute("xmlns", "http://xspf.org/ns/0/"),
                new XElement("trackList")
            )
        );

        var trackList = doc.Root!.Element("trackList")!;
        var newLines = new List<string>();

        foreach (var item in Items)
        {
            var line = RawLines[item.OriginalIndex - 1];

            if (line.StartsWith("#") || string.IsNullOrWhiteSpace(line))
                continue;

            var uri = ConvertLocalPathToUri(line);
            newLines.Add(uri);

            var track = new XElement("track",
                new XElement("location", uri)
            );

            trackList.Add(track);
        }

        _xspfDoc = doc;

        RawLines.Clear();
        foreach (var uri in newLines)
            RawLines.Add(uri);

        Items.Clear();
        for (int i = 0; i < RawLines.Count; i++)
        {
            Items.Add(new PlaylistItem
            {
                Index = i + 1,
                FilePath = RawLines[i],
                OriginalIndex = i + 1
            });
        }

        CurrentFormat = PlaylistType.Xspf;
        StatusTextLeft = "XSPF";
        StatusTextRight = "";

        CanSaveITunesTxt = false;
        CanSaveM3u = false;
        CanSaveM3uAnsi = false;
        CanSaveM3uBom = false;
        CanSaveM3uUtf8 = false;
        CanSaveXspf = true;

        CanConvertToM3u = true;
        CanConvertToXspf = false;

        IsBusy = false;
    }

    #endregion

    #region = Save File ==

    [RelayCommand]
    public async Task SaveITunesTxtAsync()
    {
        if (IsBusy || RawLines.Count == 0 || CurrentFormat != PlaylistType.ITunesTsv)
            return;
        /*
        var dialog = new SaveFileDialog
        {
            Filters = { new FileDialogFilter { Name = "Text", Extensions = { "txt" } } },
            DefaultExtension = "txt"
        };

        var file = await dialog.ShowAsync(GetWindow());
        if (string.IsNullOrWhiteSpace(file))
            return;

        try
        {
            // Rebuild TSV rows in current UI order
            var ordered = Items
                .Select(item => RawLines[item.OriginalIndex])
                .ToList();

            var utf8 = string.Join("\r\n", ordered);

            // Convert UTF‑8 → UTF‑16 LE with BOM
            var bytes = Encoding.Unicode.GetPreamble()
                .Concat(Encoding.Unicode.GetBytes(utf8))
                .ToArray();

            File.WriteAllBytes(file, bytes);

            StatusTextRight = $"Saved as txt: {file}";
            IsDirty = false;
        }
        catch
        {
            StatusTextRight = $"Error saving txt: {file}";
        }
        */
    }

    [RelayCommand]
    public Task SaveM3uUtf8Async() => SaveM3uAsync(ansi: false, utf8Bom: false, extension: ".m3u");

    [RelayCommand]
    public Task SaveM3uBomAsync() => SaveM3uAsync(ansi: false, utf8Bom: true, extension: ".m3u");

    [RelayCommand]
    public Task SaveM3u8Async() => SaveM3uAsync(ansi: false, utf8Bom: true, extension: ".m3u8");

    [RelayCommand]
    public Task SaveM3uAnsiAsync() => SaveM3uAsync(ansi: true, utf8Bom: false, extension: ".m3u");

    public async Task SaveM3uAsync(bool ansi, bool utf8Bom, string extension)
    {
        if (IsBusy || RawLines.Count == 0)
            return;
        /*
        var dialog = new SaveFileDialog
        {
            Filters = { new FileDialogFilter { Name = "M3U", Extensions = { extension.TrimStart('.') } } },
            DefaultExtension = extension.TrimStart('.')
        };

        var file = await dialog.ShowAsync(GetWindow());
        if (string.IsNullOrWhiteSpace(file))
            return;

        try
        {
            // Preserve rearranged order
            var ordered = Items
                .Select(item => RawLines[item.OriginalIndex - 1])
                .ToList();

            string text = string.Join("\n", ordered);

            if (ansi)
            {
                var bytes = Encoding.GetEncoding("shift_jis").GetBytes(text);
                File.WriteAllBytes(file, bytes);
            }
            else if (utf8Bom)
            {
                var bytes = Encoding.UTF8.GetPreamble()
                    .Concat(Encoding.UTF8.GetBytes(text))
                    .ToArray();

                File.WriteAllBytes(file, bytes);
            }
            else
            {
                File.WriteAllText(file, text, new UTF8Encoding(false));
            }

            StatusTextRight = $"Saved as m3u: {file}";
            IsDirty = false;
        }
        catch
        {
            StatusTextRight = $"Error saving m3u: {file}";
        }
        */
    }

    [RelayCommand]
    public async Task SaveXspfAsync()
    {
        if (IsBusy || CurrentFormat != PlaylistType.Xspf || _xspfDoc == null)
            return;
        /*
        var dialog = new SaveFileDialog
        {
            Filters = { new FileDialogFilter { Name = "XSPF", Extensions = { "xspf" } } },
            DefaultExtension = "xspf"
        };

        var file = await dialog.ShowAsync(GetWindow());
        if (string.IsNullOrWhiteSpace(file))
            return;

        try
        {
            if (IsRearrangedOrder)
            {
                // Rebuild XML from Items
                var doc = new XDocument(
                    new XElement("playlist",
                        new XAttribute("xmlns", "http://xspf.org/ns/0/"),
                        new XElement("trackList")
                    )
                );

                var trackList = doc.Root!.Element("trackList")!;

                foreach (var item in Items)
                {
                    var uri = RawLines[item.OriginalIndex - 1];

                    trackList.Add(
                        new XElement("track",
                            new XElement("location", uri)
                        )
                    );
                }

                doc.Save(file);
            }
            else
            {
                _xspfDoc.Save(file);
            }

            StatusTextRight = $"Saved as xspf: {file}";
            IsDirty = false;
        }
        catch
        {
            StatusTextRight = $"Error saving xspf: {file}";
        }
        */
    }

    #endregion

    #region = Menu Items ==

    [RelayCommand]
    public void MoveItemUp()
    {
        if (SelectedItem is null)
            return;

        MoveItemUp(SelectedItem);
    }

    [RelayCommand]
    public void MoveItemDown()
    {
        if (SelectedItem is null)
            return;

        MoveItemDown(SelectedItem);
    }

    public void MoveItemUp(PlaylistItem item)
    {
        int index = Items.IndexOf(item);
        if (index <= 0)
            return;

        Items.RemoveAt(index);
        Items.Insert(index - 1, item);

        ApplyReindexing();
    }

    public void MoveItemDown(PlaylistItem item)
    {
        int index = Items.IndexOf(item);
        if (index < 0 || index >= Items.Count - 1)
            return;

        Items.RemoveAt(index);
        Items.Insert(index + 1, item);

        ApplyReindexing();
    }

    #endregion



    #endregion

}
