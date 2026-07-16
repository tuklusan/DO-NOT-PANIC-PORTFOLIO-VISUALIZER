<!--
============================================================================
Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
Proprietary rights reserved except as expressly licensed herein.

DO NOT PANIC PORTFOLIO VISUALIZER
This file is governed by the SANYALnet Labs Non-Commercial License in the
root LICENSE file. Non-Commercial use is permitted; Commercial Use and use
for AI/ML model training are prohibited unless separately authorized.

Attribution is required: "Based on original work by Supratim Sanyal of
SANYALnet Labs." See LICENSE for full terms, warranty disclaimer, termination,
patent, trademark, and governing-law provisions.
============================================================================
-->

# DO NOT PANIC Portfolio Visualizer: Stock Market Dashboard

DO NOT PANIC Portfolio Visualizer is a free animated Windows stock market
dashboard for Windows 10 and Windows 11.

It turns delayed stock and ETF market data into a cinematic financial display
with customizable portfolio ticker tapes, floating charts, global market
clocks, macro indicators, financial news, and rotating city or stock-exchange
backgrounds. Use it as a fullscreen stock ticker on a second monitor, an
office financial dashboard, an ambient television display, or a visual
portfolio monitor that does not resemble a spreadsheet designed by a committee.

## Watch DO NOT PANIC Portfolio Visualizer in Action

See the [Windows stock market dashboard running](https://youtu.be/sEtRox-geWM)
with animated portfolio ticker tapes, floating stock and ETF charts, global
market clocks, financial news, macro indicators, and rotating backgrounds.

The video demonstrates DO NOT PANIC Portfolio Visualizer as a fullscreen stock
ticker, second-monitor portfolio dashboard, and animated financial market
display for Windows 10 and Windows 11.

## Windows Stock Market Dashboard Features

- Four customizable stock and ETF ticker-tape lanes
- Independent ticker direction and movement speed
- Blue, green, and red flashes for unchanged, rising, and falling quotes
- Up to 16 floating charts selected from major portfolio movers
- Daily, intraday, and early quote-based graph data
- A macro-market ribbon with indexes, volatility, Treasury yields, gold, oil,
  Bitcoin, and the US dollar index
- A Global Markets ribbon with exchange-local times, market direction, session
  status, countdowns, and available weather
- RSS financial news with optional AI-written summaries
- Built-in, downloadable, and user-selected rotating backgrounds
- Windowed, maximized, fullscreen, and multi-monitor operation

The dashboard fills progressively. Market symbols are requested in sequence,
while completed responses are processed asynchronously so one slow lookup does
not have to freeze the rest of the display.

## Portfolio Charts and Stock Ticker Updates

Floating stock and ETF charts can appear from the first usable quote instead of
waiting for every historical-data request to finish. When richer data becomes
available, it replaces the provisional chart for the same symbol.

On weekends, market holidays, or other periods without current intraday
history, the visualizer can use recent daily market closes. Daily charts show
exchange-local dates, while intraday charts show exchange-local times.

Ticker flashes are driven by actual quote updates:

- Blue: the displayed value refreshed but did not change
- Green: the displayed value moved higher
- Red: the displayed value moved lower

Only the updated ticker item flashes. Stale quotes, missing values,
configuration changes, and symbols not displayed on the ticker do not create
false update signals.

## Global Markets, Financial News, and Backgrounds

The Macro Market ribbon provides an at-a-glance view of major market
indicators. The separate Global Markets ribbon shows financial centers around
the world with available exchange times, session states, market direction,
countdowns, and weather.

RSS financial news works immediately and requires no account or API key.
Headlines appear in a character-by-character news scroller with readable
pauses.

Optional AI processing can turn financial headlines into Douglas
Adams-inspired Vogon haiku or classical Shakespearean summaries.
OpenRouter-compatible defaults are included, but no API key is supplied. Users
may add a personal OpenRouter key or configure another OpenAI-compatible
endpoint and model through Settings.

If AI access becomes unavailable, DO NOT PANIC falls back to RSS news and
retries during a later refresh.

Backgrounds may come from the built-in collection, managed downloads, or your
own image folder and subfolders. Slow rotation and zoom create an ambient
financial display rather than a static wallpaper with stock prices nailed to
it.

## Fullscreen and Second-Monitor Use

DO NOT PANIC can run in a normal window, maximized, or fullscreen. Press `F11`
or double-click to enter fullscreen, and press `Esc` to return to the normal
window.

The application is designed for second monitors, ultrawide displays, office
screens, televisions, and wall-mounted financial dashboards. Version 1.0
includes improved fullscreen, background-transition, rendering-recovery, and
multi-monitor behavior for long-running displays.

## Install on Windows 10 or Windows 11

Important: DO NOT PANIC Portfolio Visualizer 1.0 is independently published
and currently unsigned. Microsoft Defender SmartScreen may therefore display
`Windows protected your PC`.

The complete source code is publicly available for inspection in the
[official DO NOT PANIC Portfolio Visualizer GitHub repository](https://github.com/tuklusan/DO-NOT-PANIC-PORTFOLIO-VISUALIZER).
Every official release also includes published checksums and a VirusTotal
advisory report so users can inspect and verify the installer.

1. Download `DoNotPanicPortfolioVisualizerSetup-1.0.exe` from this official
   itch.io page.
2. Download the supplied SHA-256 checksum and VirusTotal advisory report.
3. Verify that the installer's SHA-256 value matches the published checksum.
4. Review the VirusTotal advisory report and, if desired, inspect the complete
   source code on GitHub.
5. Run the installer.
6. If SmartScreen displays `Windows protected your PC`, select `More info`.
7. Confirm that you downloaded the official installer and verified its
   checksum.
8. Select `Run anyway`.
9. Read and accept the SANYALnet Labs Non-Commercial License.
10. Launch DO NOT PANIC from the Desktop or Start menu.

The SmartScreen warning does not by itself mean that malware was detected. It
commonly appears for unsigned software or applications that have not yet built
enough download reputation with Microsoft.

VirusTotal is an independent multi-engine advisory signal, not a
certification, warranty, or guarantee of safety. Checksums confirm that the
downloaded file matches the published release; they do not turn software into
an enchanted force field.

The installer is self-contained. Visual Studio and the .NET SDK are not
required.

## Customize Your Portfolio Display

Open `Options > Configure` to choose:

- Portfolio symbols
- Ticker directions and speeds
- Background timing
- A personal image folder
- RSS or optional AI financial news
- The AI news-writing style

Configuration checks proposed market symbols and optional AI access before
applying changes.

## Privacy, Storage, and Network Use

Settings, protected provider secrets, cached news and charts, diagnostic
traces, and downloaded backgrounds are stored locally under:

`%LOCALAPPDATA%\DoNotPanicPortfolioVisualizer`

Internet access is used for delayed market data, RSS news, optional AI
processing, available world-market weather, and managed background downloads.
The uninstaller removes the application and its app-owned Local AppData. It
leaves any external image folder selected by the user untouched.

## Windows System Requirements

- Windows 10 or Windows 11
- 64-bit Windows computer
- Internet connection for delayed market data and financial news
- Optional OpenAI-compatible API key only for AI-generated news summaries

## Important Financial Disclaimer

DO NOT PANIC is an artistic market-data visualization for education and
entertainment.

Quotes are delayed by at least 15 minutes and may be incomplete, revised,
unavailable, or inaccurate.

DO NOT PANIC is not a trading terminal, brokerage application, real-time
market-data service, financial-planning tool, portfolio-accounting system, tax
tool, valuation tool, alerting platform, or dependable financial monitoring
application. Never use it for investment, trading, tax, valuation, or
financial-planning decisions.

Enjoy the Windows stock market dashboard. Do not hand it your retirement.

## Source Code and License

The complete source code, issue tracker, release history, and technical
documentation are available in the
[official GitHub repository](https://github.com/tuklusan/DO-NOT-PANIC-PORTFOLIO-VISUALIZER).

Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.

The application is source-available under the SANYALnet Labs Non-Commercial
License for strictly non-commercial personal, educational, and hobbyist use.
Commercial use, corporate internal use, monetization, and AI model training
are prohibited unless separately authorized.

This is not a public-domain or OSI open-source release.

Developed by Supratim Sanyal of SANYALnet Labs.
