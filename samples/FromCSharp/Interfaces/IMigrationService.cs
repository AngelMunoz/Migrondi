using Migrondi.Types;

namespace FromCSharp.Interfaces
{
    public interface IMigrationService
    {
        public void Init(InitOptions options);
        public void New(NewOptions options, MigrondiConfig? config);
        public void Up(UpOptions options, MigrondiConfig? config);
        public void Down(DownOptions options, MigrondiConfig? config);
        public void List(ListOptions options, MigrondiConfig? config);
        public void Status(StatusOptions options, MigrondiConfig? config);
    }
}
