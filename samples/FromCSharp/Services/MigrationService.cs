using FromCSharp.Interfaces;
using Migrondi.Migrations;
using Migrondi.Types;

namespace FromCSharp
{
    public class MigrationService : IMigrationService
    {
        public void Init(InitOptions options)
        {
            MigrondiRunner.RunInit(options);
        }

        public void New(NewOptions options, MigrondiConfig? config)
        {
            throw new System.NotImplementedException();
        }

        public void Up(UpOptions options, MigrondiConfig? config)
        {
            throw new System.NotImplementedException();
        }

        public void Down(DownOptions options, MigrondiConfig? config)
        {
            throw new System.NotImplementedException();
        }

        public void List(ListOptions options, MigrondiConfig? config)
        {
            throw new System.NotImplementedException();
        }

        public void Status(StatusOptions options, MigrondiConfig? config)
        {
            throw new System.NotImplementedException();
        }
    }
}
