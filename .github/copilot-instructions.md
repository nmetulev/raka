# Copilot Instructions for Project Raka

This repository contains **Raka**, a CLI tool for WinUI 3 app automation.

## Key instructions files

- `instructions/raka.instructions.md` — **How to use Raka** to inspect, modify, and interact with running WinUI 3 apps. Read this when working on WinUI 3 projects that use Raka.
- `samples/SampleApp/instructions/` — WinUI 3 best practices, accessibility, security, performance, etc.

## Project structure

- `src/Raka.Cli/` — The CLI tool (System.CommandLine, Native AOT)
- `src/Raka.DevTools/` — NuGet package injected into target apps (pipe server, command routing, visual tree walking)
- `src/Raka.Protocol/` — Shared message types
- `src/Raka.Tap/` — Native C++ DLL for inspecting any WinUI 3 app without NuGet
- `samples/SampleApp/` — Test app with Raka.DevTools

## Conventions

- All commands return JSON (camelCase, null-ignoring)
- CLI uses `System.CommandLine` with `Create()` factory pattern per command
- Server-side handlers are in `CommandRouter.cs` (switch on command name)
- New commands need: Protocol constant, CommandRouter handler, CLI Command class, CliJsonContext params record
- Build: `dotnet build -c Debug` (Raka.DevTools is Debug-only)
- Pack NuGet: `dotnet pack src/Raka.DevTools -c Debug -o artifacts`
