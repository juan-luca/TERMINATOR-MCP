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

Before running any component you must provide a Gemini API key using an
environment variable. The configuration system looks for `GEMINI__APIKEY`:

```bash
export GEMINI__APIKEY="<your-gemini-key>"
```

Then run the API:

```bash
dotnet run --project AgentAPI
```

To run the background worker instead:

```bash
dotnet run --project AgentWorker
```

## Running tests

Execute the unit tests with:

```bash
dotnet test DesarrolladorAutonomo.sln
```
