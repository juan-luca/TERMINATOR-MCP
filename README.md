# TERMINATOR

This repository contains a multi-project .NET solution. The projects target .NET 9.0, which may not be preinstalled on all systems. If `dotnet` is missing, you can download a .NET 9 SDK tarball and extract it locally:

```bash
wget https://builds.dotnet.microsoft.com/dotnet/Sdk/9.0.300/dotnet-sdk-9.0.300-linux-x64.tar.gz
mkdir -p $HOME/dotnet && tar zxf dotnet-sdk-9.0.300-linux-x64.tar.gz -C $HOME/dotnet
export DOTNET_ROOT=$HOME/dotnet
export PATH=$DOTNET_ROOT:$PATH
```

Then build the solution:

```bash
dotnet build DesarrolladorAutonomo.sln
```

The Gemini configuration is bundled with the repository. A default API key
is already included in the `appsettings.json` files, so no environment
variables are required for a basic run.

Then run the API:

```bash
dotnet run --project AgentAPI
```

To run the background worker instead:

```bash
dotnet run --project AgentWorker
```

By default the worker will attempt to build and automatically fix the generated
project up to three times. You can adjust this behaviour in `AgentWorker/appsettings.json`
under the `Worker:MaxCorrectionCycles` setting or via the `Worker__MaxCorrectionCycles`
environment variable. Using a value of `0` (or any negative number) means the
worker will continue correcting until the build succeeds.

## Running tests

Execute the unit tests with:

```bash
dotnet test DesarrolladorAutonomo.sln
```
