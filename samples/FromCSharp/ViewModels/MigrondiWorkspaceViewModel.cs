using ReactiveUI;
using Splat;

namespace FromCSharp.ViewModels
{
    public class MigrondiWorkspaceViewModel : ReactiveObject, IRoutableViewModel
    {
        public string? UrlPathSegment { get; } = "workspace";
        public IScreen HostScreen { get; }

        public MigrondiWorkspaceViewModel(IScreen? screen = null)
        {
            HostScreen = screen ?? Locator.Current.GetService<IScreen>();

        }
    }
}
