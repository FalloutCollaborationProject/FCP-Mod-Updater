#!/bin/bash
APP_NAME="FCPModUpdater"

# Self-contained
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish/win-x64-sc ./FCPModUpdater.csproj
dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o ./publish/linux-x64-sc ./FCPModUpdater.csproj

# Archive self-contained builds
cd ./publish
zip -r "${APP_NAME}-win-x64-selfcontained.zip" win-x64-sc && rm -rf win-x64-sc
tar -czvf "${APP_NAME}-linux-x64-selfcontained.tar.gz" linux-x64-sc && rm -rf linux-x64-sc
cd ..

# Framework-dependent
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false -o ./publish/win-x64-fd ./FCPModUpdater.csproj
dotnet publish -c Release -r linux-x64 --self-contained false -p:PublishSingleFile=false -o ./publish/linux-x64-fd ./FCPModUpdater.csproj

# Archive framework-dependent builds (keep originals)
cd ./publish
zip -r "${APP_NAME}-win-x64.zip" win-x64-fd
tar -czvf "${APP_NAME}-linux-x64.tar.gz" linux-x64-fd
cd ..
