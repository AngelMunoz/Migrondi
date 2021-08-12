using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using FromCSharp.ViewModels;

namespace FromCSharp.Views
{
    public partial class MigrondiWorkspace : ReactiveUserControl<MigrondiWorkspaceViewModel>
    {
        public MigrondiWorkspace()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            Console.WriteLine("Olv");
        }
    }
}
