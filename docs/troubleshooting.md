# Troubleshooting

## Build Fails Repeatedly

Check the `build_errors.log` file in the generated project directory. The `ErrorFixer` uses this log to rewrite failing files. If automatic correction stops working you can examine the errors manually.

## Missing .NET SDK

If you encounter `dotnet: command not found` ensure that the .NET SDK is installed and the `DOTNET_ROOT` environment variable points to the installation path.

## Invalid @using Directives

Projects created by the worker may contain unnecessary `@using` directives in `_Imports.razor`. Remove or comment out those that do not resolve to avoid build errors.
