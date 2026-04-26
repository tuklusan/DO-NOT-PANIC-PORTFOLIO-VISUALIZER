# AGENTS

## Process Management note
Whenever you start a background process, local development server, or testing instance, you MUST explicitly terminate it before reporting the task as complete.

## IDE orientation
Assume the primary developer uses **Visual Studio 2022**.
Do not make the project depend on a non-Visual-Studio-first workflow.

## Design note for graph overlays
These are not giant dashboard charts. They are small floating sparkline cards. Keep them elegant, semi-transparent, and readable.

## Motion note
The graph cards and the clock should bounce slowly around the screen like polite billiard balls, not pinball. Use low velocities and long-lived motion.

## Clock note
Show both local machine time and New York time. Treat the clock like another floating overlay card so it shares styling and motion rules.

## Data note
Historical cache belongs under `%LOCALAPPDATA%\PortfolioSaver\Caches\History`. Delete history files older than 14 days.

## Canonical Codex operational rule
When running Codex-Agent, cloud requests initiated by the agent must be spaced out by at least 15 seconds after the last response.
This is a Codex operational constraint only and is not a product/runtime throttling requirement for the screensaver codebase.

## Visual note
Green for upward segments, red for downward segments. The line should be split by movement direction, not just colored by final result.

