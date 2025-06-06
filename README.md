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

And run the API:

```bash
dotnet run --project AgentAPI
```

## Execution Memory

Each time the worker processes a prompt it records the prompt, generated backlog
and final build result in `execution_memory.json`. Additional metadata such as
the project path and the Git commit hash are stored so you can trace exactly
which version produced each run. This file grows over time and can be inspected
to learn from previous runs.
