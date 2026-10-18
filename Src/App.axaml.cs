using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using PlaylistEditorX.ViewModels;
using PlaylistEditorX.Views;
using System.Linq;
using PlaylistEditorX.Services;

namespace PlaylistEditorX
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();
                Services.IStorageService storageService = new Services.StorageService(desktop.MainWindow);
                Services.IDialogService dialogService = new Services.DialogService();
                desktop.MainWindow.DataContext = new MainWindowViewModel(storageService, dialogService);
            }

            base.OnFrameworkInitializationCompleted();
        }

    }
}