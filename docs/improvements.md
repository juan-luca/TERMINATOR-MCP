# Agent Improvements

## Developer Agent

Below are some recommended improvements for the developer agent.

- Validate that generated projects build successfully before returning results
- Limit code generation to referenced namespaces and packages
- Normalize file paths to prevent invalid names
- Use asynchronous I/O when writing files
- Log a summary of tasks processed by the worker
- Support environment-based configuration for API keys
- Provide option to run dotnet format after code generation
- Add progress output for long-running tasks
- Generate unit test stubs for new classes
- Automatically clean temporary directories before start
## Error Fixer

Below are some recommended improvements for the error fixer component.
- Parse compiler output to highlight missing dependencies
- Suggest common fixes for CS and MSB errors
- Remove invalid using directives and add required ones
- Run dotnet build with detailed verbosity for troubleshooting
- Apply code formatting to reduce style-related warnings
- Provide a summary of changes after each fix attempt
- Stop after a configurable number of unsuccessful iterations
- Allow custom scripts to be executed before and after fixing
- Record build logs for each attempt
- Fallback to generating a minimal example when errors persist
