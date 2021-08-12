using FromCSharp.Types;
using ReactiveUI;
using Splat;

namespace FromCSharp.ViewModels
{
    public class WorkspaceViewModel : ReactiveObject, IRoutableViewModel
    {
        public string? UrlPathSegment { get; } = "workspace";
        public IScreen HostScreen { get; }

        public WorkspaceViewModel(MigrondiWorkspace workspace, IScreen? screen = null)
        {
            HostScreen = screen ?? Locator.Current.GetService<IScreen>();
        }
    }
}
