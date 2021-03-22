@echo off

set solution=Remoting.NET.sln

dotnet build %solution% -c Release
dotnet build %solution% -c Debug
