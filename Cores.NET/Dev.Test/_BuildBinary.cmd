dotnet clean -c Release

dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeSymbolsInSingleFile=true --self-contained -o bld\win-x64\Dev.Test

dotnet publish -c Release -r linux-x64 -p:PublishSingleFile=true -p:PublishReadyToRun=false -p:IncludeSymbolsInSingleFile=true --self-contained -o bld\linux-x64\Dev.Test

dotnet publish -c Release -r linux-arm64 -p:PublishSingleFile=true -p:PublishReadyToRun=false -p:IncludeSymbolsInSingleFile=true --self-contained -o bld\linux-arm64\Dev.Test

