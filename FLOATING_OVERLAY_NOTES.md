# Floating Overlay Notes

## Intent
The screensaver should feel alive even when markets are quiet. The mini-graphs and clock should wander around the screen with slow bounce physics.

## Recommended motion behavior
- Use a shared animation timer on the UI thread or CompositionTarget rendering hook.
- Maintain per-overlay position and velocity.
- Clamp/bounce against the visible viewport edges.
- Reserve safe top and bottom bands so overlays do not live on top of the status bar or benchmark strip.
- Seed starting positions by tape so the first frame is already distributed.

## Clock behavior
- One floating card.
- Show local machine time and New York time side by side.
- Refresh every second.
- Use `TimeZoneInfo.FindSystemTimeZoneById` with Windows/IANA mapping as needed during implementation.

## Graph behavior
- Graph cards should stay small.
- Group by tape and pick a few symbols per tape.
- Rising path segments should be green; falling path segments should be red.
- Use the same semi-transparent card shell as the clock.
