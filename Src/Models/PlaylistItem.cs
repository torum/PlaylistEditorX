using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Text;

namespace PlaylistEditorX.Models;

public partial class PlaylistItem : ObservableObject
{
    /*
    public string Location { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
    */
    [ObservableProperty]
    public partial int Index { get; set; }

    [ObservableProperty]
    public partial string FilePath { get; set; } = string.Empty;

    public int OriginalIndex { get; set; }
}