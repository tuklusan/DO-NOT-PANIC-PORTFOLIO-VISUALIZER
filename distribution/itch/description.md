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

# DO NOT PANIC PORTFOLIO VISUALIZER 1.0

**Windows SmartScreen notice:** Version 1.0 is an unsigned, independently
published Windows application. If you downloaded the installer from this
official itch.io page, verify it against the SHA-256 file provided below, then
choose **More info** and **Run anyway** when Windows displays "Windows protected
your PC." The complete source code is publicly available for inspection at the
[official GitHub repository](https://github.com/tuklusan/DO-NOT-PANIC-PORTFOLIO-VISUALIZER).
Every official 1.0 download set includes checksums and a VirusTotal advisory
report. VirusTotal is an independent multi-engine scan, not a certification,
warranty, or guarantee of safety.

DO NOT PANIC PORTFOLIO VISUALIZER is a cinematic fullscreen stock market
dashboard for Windows 10 and Windows 11. It combines delayed market data,
custom portfolio ticker tapes, floating stock charts, global markets,
financial news, macro indicators, and rotating city or stock-exchange
photography in one animated ambient finance display.

It is designed for a second monitor, office display, television, wall-mounted
screen, ultrawide monitor, or anyone who wants a beautiful stock ticker and
portfolio visualization instead of another dense spreadsheet.

## What You Will See

- Four customizable stock and ETF ticker-tape lanes with independent direction
  and speed
- Blue, green, and red update flashes showing unchanged, rising, or falling
  ticker values
- Up to 16 floating graph cards selected from the biggest portfolio movers
- Fast green ceiling motion for rising graph cards and red floor motion for
  falling graph cards
- A macro-market ribbon with volatility, indexes, Treasury yields, gold, oil,
  the dollar index, and Bitcoin
- A Global Markets ribbon with major financial centers, exchange-local times,
  market direction, session state, and available weather
- A character-by-character financial news scroller with readable pauses
- RSS financial news by default, with optional AI-generated Douglas
  Adams-inspired Vogon haiku or classical Shakespearean summaries
- Built-in and downloadable financial-center backgrounds with slow rotation and
  zoom
- Support for your own background-image folder and subfolders
- Fullscreen control by F11 or double-click, with Esc to return to a window

The dashboard fills progressively and requests market symbols one at a time at
approximately one-second intervals. Incoming responses are processed
asynchronously so one slow lookup does not have to freeze the whole display.

## Important Financial Disclaimer

This is an artistic market-data visualization for education and entertainment.
Quotes are delayed by at least 15 minutes and may be incomplete, unavailable,
revised, or inaccurate. DO NOT PANIC is **not** a trading terminal,
financial-planning tool, portfolio-accounting system, alerting platform, or
dependable financial monitoring application. Never use it for investment,
trading, tax, valuation, or financial-planning decisions.

## Install on Windows

1. Download the Windows installer and the SHA-256 checksum from the files below.
2. Check the supplied VirusTotal advisory report and verify the SHA-256 value.
3. Run the installer.
4. If Microsoft Defender SmartScreen appears, choose **More info**, confirm the
   filename is the official download, and select **Run anyway**.
5. Read and accept the SANYALnet Labs Non-Commercial License.
6. Launch DO NOT PANIC from the Desktop or Start menu shortcut.

The installer is self-contained. You do not need Visual Studio or the .NET SDK.
The uninstaller removes the application and app-owned Local AppData while
leaving any external image folder you selected alone.

## Customize Your Market Display

Open **Options > Configure** to choose portfolio symbols, tape directions and
speeds, background timing, a custom image folder, RSS or AI financial news, and
the news writing style. Configuration validation checks proposed symbols and
AI access before applying changes.

RSS news works immediately and requires no account. Optional AI summaries ship
with OpenRouter-compatible defaults (`https://openrouter.ai/api/v1` and
`openrouter/free`) but no API key. Users can create a personal OpenRouter key or
configure another OpenAI-compatible endpoint and model. If AI access becomes
unavailable, the visualizer falls back to RSS and retries on a later refresh.

## Privacy, Storage, and Network Use

Settings, protected provider secrets, cached news and charts, traces, and
downloaded backgrounds are stored locally under
`%LOCALAPPDATA%\DoNotPanicPortfolioVisualizer`. Internet access is used for
market data, RSS news, optional AI processing, world-market weather, and
managed background downloads.

## System Requirements

- Windows 10 or Windows 11, 64-bit
- Internet connection for current delayed market data and financial news
- Optional OpenAI-compatible API key only for AI-summarized news

## Source and License

The complete source code, issue tracker, technical documentation, and canonical
releases are available at the
[DO NOT PANIC Portfolio Visualizer GitHub repository](https://github.com/tuklusan/DO-NOT-PANIC-PORTFOLIO-VISUALIZER).

Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs. The application is
source-available under the SANYALnet Labs Non-Commercial License for strictly
non-commercial personal, educational, and hobbyist use. Commercial use,
corporate internal use, monetization, and AI model training are prohibited.
This is not a public-domain or OSI open-source release.

Developed by **Supratim Sanyal** of **SANYALnet Labs**.
