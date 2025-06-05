# Setup Guide

## Prerequisites

- .NET SDK 9.0 (preview) or later
- Optional: GitHub account if you plan to push changes

If `dotnet` is not already installed you can download the SDK tarball and extract it locally:

```bash
wget https://builds.dotnet.microsoft.com/dotnet/Sdk/9.0.300/dotnet-sdk-9.0.300-linux-x64.tar.gz
mkdir -p $HOME/dotnet && tar zxf dotnet-sdk-9.0.300-linux-x64.tar.gz -C $HOME/dotnet
export DOTNET_ROOT=$HOME/dotnet
export PATH=$DOTNET_ROOT:$PATH
```

## Building the Solution

```bash
dotnet build DesarrolladorAutonomo.sln
```

This command restores packages and builds all projects in the solution.
