# Usage

## Running the API

Start the HTTP API to accept prompts:

```bash
dotnet run --project AgentAPI
```

POST a JSON payload to `/prompt` containing a `titulo` and `descripcion`. Prompts are stored in `prompt-queue.json` at the repository root.

## Running the Worker

The worker processes queued prompts and generates code:

```bash
dotnet run --project AgentWorker
```

Projects are created under `AgentWorker/output/{prompt-name}`. The worker continues building and applying fixes until the project compiles or the configured maximum number of correction cycles is reached. Set `Worker__MaxCorrectionCycles=0` to keep retrying indefinitely.
