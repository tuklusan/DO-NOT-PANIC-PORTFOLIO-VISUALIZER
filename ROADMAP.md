# Roadmap

## Immediate Visual Studio-oriented priorities
1. Open and build the solution in Visual Studio 2022 Debug x64.
2. Fix compile breaks without destabilizing project structure.
3. Make settings load/save path concrete.
4. Finish Finnhub live quote flow.
5. Finish Twelve Data fallback or disable it honestly until complete.
6. Finish 14-day historical fetches using Finnhub first and Twelve Data second.
7. Materialize cache files under `%LocalAppData%\PortfolioSaver\Caches\History`.
8. Purge stale cache older than 14 days before writes and at startup.
9. Render graph overlays with green and red line segments per move direction.
10. Animate graph cards with slow bouncing motion inside the visible viewport.
11. Add a floating local/New York clock card with one-second updates and matching bounce logic.
12. Upgrade `/p` preview mode to render the real scene.
13. Publish Release x64 and validate `.scr` packaging.
