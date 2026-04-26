# Provider Throttle Notes

Quote refresh is already conservative. History refresh should be even more conservative.

## Recommended history behavior
- refresh historical series every 12 hours
- always consult cache first
- batch symbols where provider supports it
- never re-fetch a fresh 14-day series just because the live quote loop fired
