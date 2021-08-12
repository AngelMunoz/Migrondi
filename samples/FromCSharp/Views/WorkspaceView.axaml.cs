using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using FromCSharp.ViewModels;

namespace FromCSharp.Views
{
    public partial class WorkspaceView : ReactiveUserControl<WorkspaceViewModel>
    {
        public WorkspaceView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
