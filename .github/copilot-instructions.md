# Copilot Instructions for Project Raka

This repository contains **Raka**, a Playwright-like CLI tool for WinUI 3 app automation. It helps AI agents and developers inspect, modify, and interact with running WinUI 3 applications.

## Project structure

- `src/Raka.Cli/` — The CLI tool (System.CommandLine, Native AOT)
- `src/Raka.DevTools/` — NuGet package injected into target apps (pipe server, command routing, visual tree walking)
- `src/Raka.Protocol/` — Shared message types
- `src/Raka.Tap/` — Native C++ DLL for inspecting any WinUI 3 app without NuGet
- `samples/SampleApp/` — Test app with Raka.DevTools

## Build & test

- Build all: `dotnet build -c Debug`
- Build CLI only: `dotnet build src/Raka.Cli -c Debug`
- Pack NuGet: `dotnet pack src/Raka.DevTools -c Debug -o artifacts`
- Run SampleApp: `cd samples/SampleApp && dotnet run -c Debug`
- Build + pack + CLI: `.\build.ps1`

## Conventions

- All commands return JSON (camelCase, null-ignoring via `RakaJson.Options`)
- CLI uses `System.CommandLine` with `Create()` factory pattern per command
- Server-side handlers are in `CommandRouter.cs` (switch on command name)
- New commands need: Protocol constant in `Messages.cs`, CommandRouter handler, CLI Command class in `Commands/`, params record in `CliJsonContext.cs`
- NuGet TFM is `net8.0-windows10.0.19041.0` for broad compatibility (works with net8, net9, net10)
- CLI TFM is `net10.0` (latest, for development)
- Raka.DevTools is automatically stripped from Release builds via `.targets`
- Use `RAKA_DEVTOOLS` preprocessor symbol to guard Raka-specific code

## Copilot CLI plugin

This repo is a Copilot CLI plugin. Install it with:
```bash
copilot plugin install ./
```
The plugin provides the `raka` skill — full Raka usage guide for AI agents building WinUI 3 apps. See `.github/plugin/` for the plugin manifest and skill definition.

## Key instructions files

- `.github/plugin/skills/raka/SKILL.md` — **How to use Raka** to inspect, modify, and interact with running WinUI 3 apps
- `samples/SampleApp/instructions/` — WinUI 3 best practices, accessibility, security, performance, etc.
