using System;
using System.Collections.Generic;
using System.Reactive;
using System.Text;
using ReactiveUI;

namespace FromCSharp.ViewModels
{
    public class MainWindowViewModel : ReactiveObject, IScreen
    {
        public RoutingState Router { get; } = new RoutingState();

        public MainWindowViewModel()
        {
            Router.Navigate.Execute(new MigrondiWorkspaceViewModel());
        }
        

    }
}
