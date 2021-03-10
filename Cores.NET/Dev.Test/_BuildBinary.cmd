rmdir /s /q bld\

mkdir bld\

dotnet clean -c Release

dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeNativeLibrariesForSelfExtract=true --self-contained -o bld\win-x64\Dev.Test

dotnet publish -c Release -r linux-x64 -p:PublishSingleFile=true -p:PublishReadyToRun=false -p:IncludeNativeLibrariesForSelfExtract=true --self-contained -o bld\linux-x64\Dev.Test

dotnet publish -c Release -r linux-arm64 -p:PublishSingleFile=true -p:PublishReadyToRun=false -p:IncludeNativeLibrariesForSelfExtract=true --self-contained -o bld\linux-arm64\Dev.Test

rmdir /s /q c:\tmp\cores_built_binary\
mkdir c:\tmp\cores_built_binary\

copy /y bld\win-x64\Dev.Test\Dev.Test.exe c:\tmp\cores_built_binary\Dev.Test.Win.x86_64.exe
copy /y bld\linux-x64\Dev.Test\Dev.Test c:\tmp\cores_built_binary\Dev.Test.Linux.x86_64
copy /y bld\linux-arm64\Dev.Test\Dev.Test c:\tmp\cores_built_binary\Dev.Test.Linux.aarch64

cd /d c:\tmp\cores_built_binary\

c:\windows\system32\curl.exe --insecure http://private.lts.dn.ipantt.net/u/210308_001_dev_test_81740/0021_8070_1218/ -k -f -F "json=false" -F "getfile=false" -F "getdir=true" -F "file=@Dev.Test.Win.x86_64.exe" -F "file=@Dev.Test.Linux.x86_64" -F "file=@Dev.Test.Linux.aarch64"


