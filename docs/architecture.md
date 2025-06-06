# Architecture

The application is organized into separate projects so each agent can be developed and tested independently.

## Projects

- **AgentAPI** exposes a single `POST /prompt` endpoint. Incoming prompts are saved via a prompt store. Logging shows where each prompt is stored on disk.
- **AgentWorker** hosts the background worker service. It repeatedly checks the prompt queue, plans tasks using the `PlanificadorAgent`, generates code with `DesarrolladorAgent`, ensures completeness with `CodeCompletenessCheckerAgent`, then builds and fixes the project using `ErrorFixer` if compilation fails.
- **Infraestructura** contains the implementations of each agent along with the `GeminiClient` used to call the generative model.
- **Shared** defines interfaces and common types that allow the agents to communicate.

## Process Flow

1. The API writes prompts to `prompt-queue.json`.
2. The worker reads a prompt and converts it into an ordered backlog of tasks.
3. For each task the developer agent generates or modifies files in the target project.
4. After generation, the completeness checker scans the project for incomplete files and regenerates them when necessary.
5. The worker attempts to build the project. On failure, the error fixer reads `build_errors.log` and uses the generative model to rewrite problematic files.
6. Steps 4–5 repeat until the build succeeds or the maximum number of correction cycles is exceeded.
