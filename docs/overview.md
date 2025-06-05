# Project Overview

This repository contains a multi-project solution that demonstrates a fully automated code generation pipeline using .NET and the Gemini language model. The system orchestrates several agents to transform natural language prompts into working projects. The main components are:

- **AgentAPI** – A lightweight HTTP API used to submit prompts. Each prompt describes a small application or set of features that should be generated.
- **AgentWorker** – A background service that reads prompts from a queue, plans tasks, generates code, verifies file completeness, and continuously builds and fixes the generated project until it compiles.
- **Infraestructura** – Shared infrastructure containing agents for planning, code generation, error fixing, and completeness checking.
- **Shared** – Common abstractions used across projects, including the `Prompt` record and logging helpers.

Each generated project is placed inside `AgentWorker/output` under a folder derived from the prompt title. A build log is written for every compilation attempt so errors can be inspected if the automatic fixer fails.
