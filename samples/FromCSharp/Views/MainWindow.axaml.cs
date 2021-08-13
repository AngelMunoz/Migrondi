using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using FromCSharp.ViewModels;

namespace FromCSharp.Views
{
    public partial class MainWindow : ReactiveWindow<MainWindowViewModel>
    {
        public MainWindow()
        {
            InitializeComponent();
            ViewModel = new MainWindowViewModel();
#if DEBUG
            this.AttachDevTools();
#endif
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public async void SelectFolder(object sender, RoutedEventArgs args)
        {
            var dialog = new OpenFolderDialog();
            try
            {
                var result = await dialog.ShowAsync(this);
                ViewModel?.OnFolderSelected?.Invoke(result);
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine(ex, nameof(MainWindow));
            }

        }
    }
}
