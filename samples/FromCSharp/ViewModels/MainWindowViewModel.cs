using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reactive;
using System.Text;
using DynamicData.Binding;
using FromCSharp.Types;
using ReactiveUI;

namespace FromCSharp.ViewModels
{
    public class MainWindowViewModel : ReactiveObject, IScreen
    {
        private ObservableCollection<MigrondiWorkspace> _workspaces = new ObservableCollection<MigrondiWorkspace>();

        public RoutingState Router { get; } = new RoutingState();

        public ObservableCollection<MigrondiWorkspace> Workspaces { get => _workspaces; private set => this.RaiseAndSetIfChanged(ref _workspaces, value); }
        public Action<string> OnFolderSelected { get; private set; }

        public MainWindowViewModel()
        {
            LoadWorkspaces();
            OnFolderSelected += path =>
            {
                var pathId = Database.AddWorkspace(path);
                var workspace = MigrondiWorkspace.Create(path);
                Debug.WriteLine($"Added: {pathId}, Workspace: {workspace}", nameof(MainWindowViewModel));
                Workspaces.Add(workspace);
            };
        }


        public void GoToWorkspace(MigrondiWorkspace workspace)
        {
            Router.Navigate.Execute(new WorkspaceViewModel(workspace));
        }


        private void LoadWorkspaces()
        {
            var results = Database.GetWorkspaces();
            Debug.WriteLine($"Got Results: {results.Length}", nameof(MainWindowViewModel));
            Workspaces.Clear();
            foreach (var workspace in results)
            {
                Workspaces.Add(workspace);
            }
        }
    }
}
