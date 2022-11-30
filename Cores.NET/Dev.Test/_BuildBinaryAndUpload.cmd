cd /D "%~dp0"

rmdir /s /q bld\

rmdir /s /q c:\tmp\cores_built_binary\
mkdir c:\tmp\cores_built_binary\

mkdir bld\

..\Dev.Tools\CompiledBin\WriteTimeStamp.exe > c:\tmp\cores_built_binary\TimeStamp.txt

dotnet clean -c Release

dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeNativeLibrariesForSelfExtract=true --self-contained -o bld\win-x64\Dev.Test

dotnet publish -c Release -r win-arm64 -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeNativeLibrariesForSelfExtract=true --self-contained -o bld\win-arm64\Dev.Test

dotnet publish -c Release -r linux-x64 -p:PublishSingleFile=true -p:PublishReadyToRun=false -p:IncludeNativeLibrariesForSelfExtract=true --self-contained -o bld\linux-x64\Dev.Test

dotnet publish -c Release -r linux-arm64 -p:PublishSingleFile=true -p:PublishReadyToRun=false -p:IncludeNativeLibrariesForSelfExtract=true --self-contained -o bld\linux-arm64\Dev.Test

copy /y bld\win-x64\Dev.Test\Dev.Test.exe c:\tmp\cores_built_binary\Dev.Test.Win.x86_64.exe
copy /y bld\win-arm64\Dev.Test\Dev.Test.exe c:\tmp\cores_built_binary\Dev.Test.Win.aarch64.exe
copy /y bld\linux-x64\Dev.Test\Dev.Test c:\tmp\cores_built_binary\Dev.Test.Linux.x86_64
copy /y bld\linux-arm64\Dev.Test\Dev.Test c:\tmp\cores_built_binary\Dev.Test.Linux.aarch64

S:\CommomDev\SE-DNP-CodeSignClientApp\SE-DNP-CodeSignClientApp_signed.exe SignDir c:\tmp\cores_built_binary\ /CERT:SoftEtherEv /COMMENT:'Dev.Test.Win"

cd /d c:\tmp\cores_built_binary\

c:\tmp\cores_built_binary\Dev.Test.Win.x86_64.exe Hello

IF NOT "%ERRORLEVEL%" == "0" GOTO L_END

call H:\Secure\220623_Upload_CoresLib_DevTest\lts_upload_url_with_password.cmd

c:\windows\system32\curl.exe --insecure %lts_upload_url_with_password% -k -f -F "json=false" -F "getfile=false" -F "getdir=true" -F "file=@Dev.Test.Win.x86_64.exe" -F "file=@Dev.Test.Win.aarch64.exe" -F "file=@Dev.Test.Linux.x86_64" -F "file=@Dev.Test.Linux.aarch64" -F "file=@TimeStamp.txt"


:L_END
pause



