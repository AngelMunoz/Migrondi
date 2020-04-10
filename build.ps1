Param([string] $rid = 'win10-x64')
dotnet publish -c Release -r $rid --self-contained true -p:PublishSingleFile=true -o dist


