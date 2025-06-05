# Configuration

Configuration values are stored in `appsettings.json` files for each project and can be overridden using environment variables.

## Gemini API Key

Set the key used by `GeminiClient` using `Gemini__ApiKey`:

```bash
export Gemini__ApiKey=YOUR_KEY_HERE
```

## Worker Settings

- `Worker:MaxCorrectionCycles` (or `Worker__MaxCorrectionCycles` via environment) controls how many times the worker will attempt to build and fix a generated project. Using a value of `0` allows unlimited retries.

## Logging

By default logs are written to the console. Build failures also create `build_errors.log` in the generated project directory so you can inspect the compiler output.
