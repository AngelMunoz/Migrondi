using System;
using System.Collections.Generic;
using System.Reactive;
using System.Text;
using DynamicData.Binding;
using FromCSharp.Types;
using ReactiveUI;

namespace FromCSharp.ViewModels
{
    public class MainWindowViewModel : ReactiveObject, IScreen
    {
        public RoutingState Router { get; } = new RoutingState();

        public ObservableCollectionExtended<MigrondiWorkspace> workspaces { get; private set; } =
            new ObservableCollectionExtended<MigrondiWorkspace>();


        public MainWindowViewModel()
        {
            LoadWorkspaces();
        }


        public void GoToWorkspace(MigrondiWorkspace workspace)
        {
            Router.Navigate.Execute(new WorkspaceViewModel(workspace));
        }


        private void LoadWorkspaces()
        {
            var results = Database.GetWorkspaces();
            workspaces.Clear();
            workspaces.AddRange(results);
        }
    }
}
