# Codex Prompt

Complete this **Visual Studio 2022 / .NET 8 / WPF / Windows x64** screensaver project.

## Primary operating assumptions

- Canonical Codex operational rule: while running Codex-Agent, cloud requests initiated by the agent must be spaced out by at least 6 seconds. This is operational policy for Codex execution only, not a product/runtime requirement for project code.
- The main development environment is **Visual Studio 2022**.
- Keep the solution structure stable and friendly to Visual Studio users.
- Build and test on Windows.
- Prefer clean WPF/XAML patterns over unusual tooling.

## First tasks

1. Open the solution in Visual Studio 2022.
2. Build in `Debug | x64`.
3. Fix compile errors without changing the overall project structure unnecessarily.
4. Verify `sample-settings.initial-tapes.json` maps cleanly to the C# settings model.
5. Make the config app and screensaver both start.

## New required feature
Add floating mini-graphs for each tape, showing the last two weeks of performance history for selected symbols in that tape.

## Additional visual behavior
Because this is a real screensaver and not a tax form, the graph cards should move around the screen like slow bouncing balls.
Also add a floating clock card that bounces around the screen and shows both:
- local machine time
- New York time (`America/New_York`)

## Hard requirements
- Use 14 calendar days of history.
- Cache historical data under LocalAppData: `%LocalAppData%\PortfolioSaver\Caches\History`.
- Purge cached history files older than 14 days.
- Historical data refresh should default to every 12 hours, not every quote refresh.
- Render rising segments green and falling segments red.
- Floating graph cards should be grouped by tape.
- Floating graph cards and the clock should animate with bounded bouncing motion inside the viewport.
- Avoid collisions with the top status bar and bottom benchmark strip where practical.
- Keep the graph cards readable over darkened exchange backgrounds.
- Update the clock every second.
- Show local time zone text and New York time zone text.

## Provider order
- Try Finnhub first for history and quotes.
- Fall back to Twelve Data when Finnhub fails or lacks the needed symbol coverage.
- Respect the conservative throttle settings already in the project.

## Implementation notes
- Reuse a shared floating-sprite motion controller for both graph cards and the clock.
- Prefer Canvas positioning for floating overlays.
- Graph cards should not overlap too aggressively; start them in different screen regions.
- The clock card should be semi-transparent and visually consistent with the graph cards.
- Keep the solution runnable from Visual Studio with `/s`, `/c`, and `/p`.
- Upgrade `/p` from placeholder mode to a true preview when possible.

