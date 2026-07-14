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

# DO NOT PANIC Portfolio Visualizer
# Avalonia Cross-Platform Desktop Migration Design

**Revision:** Rev-01  
**Status:** Locked architecture baseline  
**Primary product model:** Local, installed desktop or laptop application  
**Primary UX:** Avalonia UI integrated into the application  
**Backend target:** Portable .NET 10  
**Market-data process:** Locally managed YFinance.NET server on loopback  
**Release model:** Self-contained, non-AOT, platform-specific desktop packages

---

## 1. Purpose

This document defines the final architecture and migration workflow for converting **DO NOT PANIC Portfolio Visualizer** from a Windows/WPF application into a local-first, cross-platform desktop application using Avalonia UI and portable .NET 10.

The current application remains the functional and visual reference during migration. The new implementation will begin in a clean successor repository derived from the current repository. The successor will preserve useful source, assets, tests, behavior, copyright, licensing, and attribution while removing obsolete architectural constraints.

This design supersedes the discarded browser-first architecture document.

The primary product model is a local, installed desktop or laptop application.

The target product is not primarily a hosted service, browser application, or thin client. It is an installed application that runs locally on a desktop or laptop and manages its own local YFinance.NET server process.

---

## 2. Final Architecture Decision

The permanent architecture is:

```text
Avalonia desktop UX
        |
        v
Portable .NET 10 application engine
        |
        v
Portable data, settings, cache, and orchestration services
        |
        v
YFinance.NET.Client
        |
        v
Raw TCP over 127.0.0.1:14870 by default
        |
        v
Locally managed YFinance.NET.Server
        |
        v
Yahoo Finance and other external providers
```

The application remains one integrated installed product even though the YFinance component runs as a separate child or companion process.

The main application owns the normal YFinance lifecycle:

```text
Application starts
        |
        v
Validate configuration and local data paths
        |
        v
Ensure local YFinance.NET.Server is available
        |
        v
Start owned server if required
        |
        v
Connect through loopback
        |
        v
Run visualizer
        |
        v
Shut down owned server cleanly when appropriate
```

Remote YFinance connections may remain technically possible for diagnostics or advanced use, but they are not a primary product workflow and must not complicate the normal local experience.

---

## 3. Primary Goals

The migration shall:

- replace WPF with Avalonia UI;
- preserve the integrated desktop-application experience;
- move all non-UI application logic from `net10.0-windows` to portable `net10.0` where technically appropriate;
- isolate unavoidable platform-specific code behind narrow interfaces;
- preserve current portfolio, quote, graph, clock, news, background, cache, degraded-mode, and settings behavior;
- preserve local ownership and management of YFinance.NET.Server;
- remove legacy screensaver compatibility;
- support native application windows and native fullscreen behavior;
- support Windows, macOS, and desktop Linux as primary operating-system families;
- support x64 and ARM64 where supported and validated;
- produce self-contained, non-AOT release packages that include the required .NET 10 runtime;
- retain the current application as the parity reference until the Avalonia implementation passes repeated zero-gap audits;
- stop after the cross-platform .NET/Avalonia product is complete unless a separate, measurable requirement justifies additional architectural work.

---

## 4. Non-Goals

This migration does not include:

- a browser-first UX;
- a public hosted service;
- thin-client architecture as a primary use case;
- a multi-user SaaS account system;
- a required HTTP or WebSocket browser API;
- an immediate C++ rewrite;
- a direct rewrite of every WPF XAML file without architectural separation;
- native Android, iOS, tvOS, Roku, Xbox, PlayStation, or Nintendo clients;
- public exposure of the raw YFinance TCP protocol;
- preservation of `.scr` screensaver behavior;
- preservation of obsolete Windows-only UX assumptions;
- visual redesign merely for the sake of redesign.

A future hosted, browser, mobile, or C++ effort would require a separate CR and a new design decision.

---

## 5. Product Use Cases

### 5.1 Primary use case

A desktop or laptop user installs the application, launches it locally, configures portfolios and display preferences, and runs the visualizer in a normal window or fullscreen.

### 5.2 Secondary use cases

- dedicated local display on a Windows, macOS, or Linux machine;
- Raspberry Pi or other supported ARM Linux display where validated;
- offline or degraded operation using cached data;
- multiple independent application instances or displays where supported by configuration and process-lifecycle rules.

### 5.3 Explicitly secondary technical capability

A remote YFinance.NET.Server may be supported through advanced configuration, but normal installation, testing, documentation, and support shall assume a locally owned loopback service.

---

## 6. Current Architecture and Constraints

The current implementation is approximately:

```text
Windows WPF desktop/configuration/screensaver projects
        |
        v
Windows-targeted presentation and rendering code
        |
        v
Core, shared, settings, media, and data services
        |
        v
YFinance.NET.Client and protocol
        |
        v
Locally owned YFinance.NET.Server
```

Known constraints include:

- several projects target `net10.0-windows`;
- WPF dependencies are spread across presentation, rendering, configuration, and executable projects;
- some apparently non-UI projects may still use Windows-only path, registry, imaging, dispatcher, cryptography, process, or shell APIs;
- the current UX includes custom rendering, motion, ticker behavior, graph cards, backgrounds, clocks, news, and fullscreen assumptions that require visual parity validation;
- installer, screensaver, validation, and process-management scripts contain Windows-specific behavior;
- YFinance runtime wiring assumes a locally owned server and loopback connection, which is correct for the primary use case and should be preserved deliberately rather than accidentally.

---

## 7. Target Solution Structure

The exact project names may change, but the intended dependency direction is:

```text
src/
  PortfolioSaver.Domain/             net10.0
  PortfolioSaver.Engine/             net10.0
  PortfolioSaver.Data/               net10.0
  PortfolioSaver.Settings.Core/      net10.0
  PortfolioSaver.Media.Core/         net10.0
  PortfolioSaver.Infrastructure/     net10.0
  PortfolioSaver.YFinanceBridge/     net10.0
  PortfolioSaver.Platform.Abstractions/ net10.0

  PortfolioSaver.Platform.Windows/   net10.0
  PortfolioSaver.Platform.MacOS/     net10.0
  PortfolioSaver.Platform.Linux/     net10.0

  PortfolioSaver.Avalonia/           net10.0

  YFinance.NET/                      net10.0
  YFinance.NET.Client/               net10.0
  YFinance.NET.Protocol/             net10.0
  YFinance.NET.Server/               net10.0

tests/
  PortfolioSaver.UnitTests/
  PortfolioSaver.IntegrationTests/
  PortfolioSaver.VisualTests/
  PortfolioSaver.PlatformTests/
  PortfolioSaver.PackagingTests/
```

### 7.1 Dependency rules

- Domain code depends on no UI or operating-system implementation.
- Engine code depends on domain abstractions and portable services only.
- Data and settings code must not reference Avalonia controls.
- Avalonia depends on portable application contracts and platform abstractions.
- Platform projects implement narrow operating-system services.
- Platform projects must not contain business logic.
- YFinance protocol and service boundaries remain independent of Avalonia.
- No portable project may reference WPF, XAML types from WPF, `System.Windows`, or Windows-only presentation assemblies.

---

## 8. Platform Abstraction Boundaries

The migration must identify and isolate platform-specific behavior rather than pretending it does not exist.

Potential abstractions include:

```text
IApplicationPaths
ISecretStore
IProcessLauncher
IChildProcessManager
IWindowPlacementService
IFullscreenService
IScreenInventoryService
IApplicationActivationService
IFilePickerService
IFolderPickerService
IOpenExternalUriService
INotificationService
IAutostartService
IUpdateService
ISystemThemeService
IPowerStateService
```

Only create abstractions required by actual behavior. Do not build a speculative framework.

### 8.1 Windows implementation examples

- Windows credential or data-protection facilities where retained;
- Windows startup shortcuts or registry integration where approved;
- Windows installer and uninstaller behavior;
- Windows monitor and window-placement behavior.

### 8.2 macOS implementation examples

- Application Support, Caches, and Logs paths;
- Keychain integration where required;
- `.app` bundle behavior;
- native menu and activation conventions;
- code signing and notarization.

### 8.3 Linux implementation examples

- XDG configuration, data, cache, and state paths;
- Secret Service integration where available;
- desktop-entry and icon installation;
- X11 window behavior for the supported Avalonia desktop path;
- package-specific service or launcher integration where needed.

---

## 9. Avalonia UX Architecture

### 9.1 Presentation model

The Avalonia application shall use a clean presentation boundary, preferably MVVM where it improves testability and state separation.

Suggested layers:

```text
Views
  |
  v
View models and presentation state
  |
  v
Application engine interfaces
  |
  v
Domain and infrastructure services
```

Views shall not own provider logic, cache policy, portfolio calculations, or YFinance scheduling.

### 9.2 Primary visual components

The Avalonia UX must reproduce or intentionally improve the current components:

- full-screen visualizer window;
- normal desktop window mode;
- ticker tape;
- quote and summary cards;
- floating or animated graph cards;
- portfolio and market-state displays;
- local and world clocks;
- finance-news scroller;
- background image and media system;
- connectivity and provider state;
- stale-data and offline indicators;
- settings and configuration screens;
- validation and error presentation;
- keyboard and pointer interaction;
- startup and shutdown transitions where useful.

### 9.3 Rendering strategy

The Avalonia renderer shall use Avalonia layout, drawing, animation, and control capabilities rather than carrying WPF rendering assumptions forward.

The implementation shall decide component by component whether to use:

- standard Avalonia controls;
- templated custom controls;
- custom drawing;
- composition or animation APIs;
- retained images and media assets;
- Skia-backed charts or custom chart rendering.

Performance shall be measured on low-end supported hardware, not inferred from development workstations.

### 9.4 Fullscreen behavior

Fullscreen is a first-class desktop feature and shall not depend on browser permission rules.

The UX shall define:

- entering and leaving fullscreen;
- monitor selection;
- window restoration;
- keyboard escape behavior;
- pointer behavior;
- multi-monitor rules;
- startup in fullscreen when configured;
- recovery when a previously selected monitor is missing;
- persistence of window position and size.

### 9.5 Multi-monitor behavior

The design shall document whether the product supports:

- one visualizer window on one selected monitor;
- multiple application windows;
- one window spanning monitors;
- independent portfolios per window;
- independent settings per display.

No multi-monitor behavior shall be assumed to be identical across Windows, macOS, X11, and embedded Linux without testing.

### 9.6 Accessibility and input

The Avalonia port shall assess:

- keyboard navigation;
- focus visibility;
- high-DPI scaling;
- text scaling;
- contrast;
- reduced motion;
- screen-reader semantics for configuration screens;
- touch and trackpad behavior where applicable.

The visualizer display may be intentionally non-interactive in some modes, but settings and configuration screens must remain operable.

---

## 10. YFinance.NET Lifecycle Design

### 10.1 Default mode

The default and supported product mode is:

```text
Mode: owned-local
Host: 127.0.0.1
Port: 14870
Transport: raw TCP
Exposure: loopback only
```

### 10.2 Startup sequence

1. Load application configuration.
2. Resolve local application-data paths.
3. Check whether a compatible YFinance.NET.Server is already available.
4. If not available, launch the packaged server executable for the current platform and architecture.
5. Wait for readiness with a bounded timeout.
6. Perform protocol handshake and compatibility validation.
7. Begin quote, history, timing, and metadata operations.
8. Report degraded state rather than freezing the UX if startup fails.

### 10.3 Shutdown sequence

1. Stop accepting new application operations.
2. Cancel or complete in-flight requests according to policy.
3. Save application state.
4. Disconnect the client cleanly.
5. Shut down the owned server if this application instance owns it and lifecycle policy permits.
6. Prevent orphaned owned processes.
7. Record shutdown diagnostics.

### 10.4 Multiple application instances

The design must define:

- whether multiple UI instances share one server;
- how ownership is transferred or avoided;
- how ports are selected;
- how shutdown avoids terminating a server used by another instance;
- how version mismatches are handled;
- how stale server processes are detected and recovered.

### 10.5 Remote mode

Remote mode, if retained, shall be advanced and opt-in:

```text
YFINANCE_SERVER_MODE=remote
YFINANCE_SERVER_HOST=<configured host>
YFINANCE_SERVER_PORT=<configured port>
```

Remote mode must not trigger local server startup. It must not be enabled by default. Security for non-loopback exposure requires a separate approved design.

---

## 11. Configuration, Data, Cache, and Secrets

### 11.1 Portable configuration model

Configuration objects and validation shall be portable and independent of Avalonia.

The application shall distinguish:

- user-editable settings;
- machine-local runtime settings;
- window and monitor state;
- cache data;
- secrets;
- diagnostics;
- migration metadata.

### 11.2 Platform paths

Use platform conventions:

- Windows: appropriate per-user application-data paths;
- macOS: Application Support, Caches, and Logs conventions;
- Linux: XDG base-directory conventions.

Do not force a Windows directory layout onto macOS or Linux.

### 11.3 Legacy migration

The first Avalonia release shall define how it discovers and migrates legacy Windows configuration.

Migration requirements:

- preserve the original before conversion;
- validate migrated data;
- produce a migration log without leaking secrets;
- support rollback or restore;
- distinguish unsupported legacy values;
- never silently discard portfolios, watchlists, backgrounds, news settings, or user preferences.

### 11.4 Secrets

Secret handling shall be abstracted. Plaintext secrets shall not be introduced merely to gain portability.

If equivalent secure storage is unavailable on a platform, the product must define an explicit fallback policy and disclose its limitations.

---

## 12. Clean Successor Repository Strategy

### 12.1 Preserve the authoritative legacy baseline

Before creating the successor repository:

- tag the authoritative legacy commit;
- record repository and submodule state;
- archive installers and release artifacts;
- record source and binary hashes;
- capture screenshots and representative videos;
- capture settings samples with secrets removed;
- preserve test baselines;
- preserve YFinance protocol documents;
- create a known-defects ledger;
- create a feature and visual inventory.

### 12.2 Create the successor repository

The successor repository shall:

- be copied or derived from the authoritative baseline;
- preserve copyright and license requirements;
- identify the original repository and baseline commit;
- establish a new solution structure;
- establish portable build defaults;
- remove obsolete release assumptions from active pipelines;
- retain legacy source only where needed for temporary parity or reference;
- never depend on the legacy repository at runtime.

### 12.3 Import classification

Every imported project or component shall be classified as:

- retained unchanged;
- retained after portable refactor;
- temporary Windows compatibility layer;
- reference-only;
- retired immediately;
- replaced by Avalonia implementation.

### 12.4 Branch and release discipline

- The legacy repository remains maintenance-only.
- The successor repository becomes the migration and future product repository.
- Each migration phase has an explicit branch or CR scope.
- No phase is declared complete without its acceptance gate.
- Visual and behavioral parity evidence is retained in the repository or release evidence store.

---

## 13. Migration Workflow

# Phase 0 - Freeze, preserve, and inventory

## Objective

Create an immutable, auditable baseline before changing code.

## Work

- freeze the authoritative legacy release;
- inventory projects, packages, scripts, assets, tests, and generated artifacts;
- identify all WPF and Windows-only dependencies;
- capture runtime behavior and visual references;
- identify known bugs separately from required behavior;
- document current YFinance startup, connection, recovery, and shutdown behavior;
- create the successor repository.

## Deliverables

- `docs/LEGACY_BASELINE.md`
- `docs/FEATURE_INVENTORY.md`
- `docs/VISUAL_INVENTORY.md`
- `docs/DEPENDENCY_CLASSIFICATION.md`
- `docs/KNOWN_DEFECTS.md`
- baseline hashes and evidence

## Gate

No migration code begins until the baseline can be independently reproduced.

---

# Phase 1 - Establish portable project boundaries

## Objective

Separate portable application logic from WPF and operating-system implementations.

## Work

- map the project dependency graph;
- move pure models, services, interfaces, and orchestration into `net10.0` projects;
- split mixed projects instead of blindly retargeting them;
- introduce only the platform abstractions required by actual dependencies;
- keep the WPF UX temporarily functional against the new portable core;
- preserve current tests and add portability tests.

## Gate

- portable projects build without WPF assemblies;
- no portable project references `System.Windows` or Windows-only presentation assemblies;
- the legacy WPF UX can still exercise the portable core;
- two successive dependency audits reveal no new Windows leakage into portable projects.

---

# Phase 2 - Make YFinance packaging and lifecycle cross-platform

## Objective

Preserve the owned-local architecture on every primary platform.

## Work

- ensure YFinance.NET, Client, Protocol, and Server target portable `net10.0`;
- remove Windows-only assumptions from process discovery and startup;
- package the correct server executable for each RID;
- implement platform-neutral readiness, ownership, and shutdown rules;
- add platform-specific process handling only behind abstractions;
- test multiple instances, stale processes, port conflicts, protocol mismatch, and failed startup;
- preserve loopback-only default binding.

## Gate

- a test host starts, connects to, uses, and stops YFinance.NET.Server on each primary OS family;
- no owned-local server is exposed beyond loopback;
- repeated lifecycle tests leave no orphaned processes;
- two successive lifecycle audits reveal no new gaps.

---

# Phase 3 - Create the Avalonia application shell

## Objective

Create the permanent cross-platform executable and establish application lifecycle, dependency injection, logging, paths, settings, and basic windows.

## Work

- add `PortfolioSaver.Avalonia`;
- define application startup and shutdown;
- wire portable services;
- implement platform service selection;
- create basic main, settings, error, and diagnostics windows or views;
- implement theme, typography, assets, and resource loading;
- implement window-state persistence and basic fullscreen;
- add smoke tests on Windows, macOS, and Linux.

## Gate

- the Avalonia shell launches on every primary OS family;
- it starts and connects to owned-local YFinance;
- settings can be loaded and saved through portable services;
- startup failures produce controlled diagnostics rather than crashes;
- two successive shell audits reveal no new gaps.

---

# Phase 4 - Port the visualizer UX component by component

## Objective

Replace WPF rendering with Avalonia while preserving visual and behavioral intent.

## Recommended order

1. application frame and background system;
2. typography and scaling system;
3. clocks and market-state indicators;
4. ticker tape;
5. quote and summary cards;
6. graph cards and chart rendering;
7. news scroller;
8. connectivity, stale-data, and degraded-state overlays;
9. settings and configuration UX;
10. animations, transitions, and polish;
11. fullscreen and multi-monitor completion.

## Work rules

- port behavior, not WPF implementation accidents;
- retain existing assets unless separately approved;
- use fixed test data for visual comparison;
- measure CPU, GPU, memory, and frame pacing;
- test high-DPI and fractional scaling;
- avoid platform-specific visual divergence unless required by convention or capability.

## Gate

Each component passes functional, visual, degraded-mode, scaling, and performance checks before the next component is accepted.

---

# Phase 5 - Dual-UX parity validation

## Objective

Run WPF and Avalonia implementations against identical inputs until the new UX reaches approved parity.

## Compare

- portfolio calculations;
- symbol ordering;
- quote values and formatting;
- timestamps and market status;
- chart ranges and labels;
- ticker content, speed, spacing, and continuity;
- news ordering and scrolling;
- background selection, scaling, and transitions;
- clocks and time-zone behavior;
- settings behavior;
- offline, stale, throttled, and provider-error states;
- startup, recovery, and shutdown;
- window placement and fullscreen behavior.

## Validation method

- fixed provider fixtures;
- automated state comparison;
- screenshot comparison with tolerances;
- manual pixel-level review where animation or text rendering differs;
- performance traces;
- regression tests for every approved discrepancy.

## Gate

- all differences are corrected or explicitly approved;
- no unexplained feature loss remains;
- two successive full parity scans reveal zero new material gaps.

---

# Phase 6 - Migrate user data and remove legacy runtime dependence

## Objective

Make Avalonia independently usable by existing and new users.

## Work

- implement legacy settings migration;
- validate backups and rollback;
- migrate media/background references safely;
- migrate secrets through approved secure mechanisms;
- ensure the Avalonia app no longer launches or depends on WPF executables;
- preserve a documented legacy rollback package during the release-candidate period.

## Gate

- migration succeeds on representative legacy configurations;
- failed migration leaves the original intact;
- the Avalonia application runs independently;
- two successive migration audits reveal zero new gaps.

---

# Phase 7 - Retire WPF and screensaver code

## Objective

Remove obsolete presentation and release paths after Avalonia parity and migration are proven.

## Retire

- WPF desktop executable;
- WPF configuration executable;
- WPF rendering projects or controls with no remaining use;
- screensaver executable and `.scr` behavior;
- screensaver installer logic;
- screensaver-specific validation harnesses;
- obsolete Windows-only UX assets and scripts;
- compatibility code that no longer serves migration or packaging.

## Retain where needed

- Windows platform integration used by the Avalonia application;
- Windows installer code that remains part of supported packaging;
- legacy evidence and documentation;
- tests that verify behavior independent of WPF.

## Gate

- the active solution contains no WPF dependency;
- release builds contain no WPF binaries;
- all primary platform builds pass;
- two successive retirement audits reveal zero new obsolete dependencies.

---

# Phase 8 - Production hardening and cross-platform release

## Objective

Ship a stable local-first Avalonia product for the supported release matrix.

## Work

- complete platform packaging;
- complete code signing and notarization;
- validate install, upgrade, migration, repair, and uninstall;
- complete performance and endurance testing;
- complete security and dependency review;
- complete crash and diagnostic behavior;
- complete update strategy;
- complete documentation and support matrix;
- generate checksums and release manifests;
- verify runtime inclusion in self-contained packages;
- validate clean machines without a separately installed .NET runtime.

## Gate

- all primary releases pass the release matrix;
- all critical and high-severity issues are resolved or release-blocked;
- two successive complete release audits reveal zero new material gaps;
- the Avalonia/.NET product is declared the completed cross-platform architecture.

---

## 14. Supported Platform and Release Matrix

Product support shall follow validated Avalonia, .NET, native dependency, packaging, and test coverage. Technical compiler or RID availability alone does not create product support.

### 14.1 Primary desktop targets

```text
Windows x64      RID: win-x64
Windows ARM64    RID: win-arm64
macOS x64        RID: osx-x64
macOS ARM64      RID: osx-arm64
Linux x64        RID: linux-x64
Linux ARM64      RID: linux-arm64
```

### 14.2 Secondary target

```text
Raspberry Pi OS ARM64 / desktop or embedded scenario
RID: linux-arm64
```

Raspberry Pi support shall be declared only after the chosen windowing or embedded rendering mode, required native libraries, input, fullscreen, performance, and installation workflow have been validated.

### 14.3 Optional future targets

```text
Linux ARM32      RID: linux-arm
Other distributions or architectures only after explicit validation
```

Musl-based distributions require separate native dependency analysis and are not automatically supported by a glibc build.

### 14.4 Platform support policy

For each supported platform, record:

- minimum operating-system version;
- CPU architecture;
- Avalonia support tier at release time;
- .NET 10 support status;
- required native packages;
- graphics/windowing requirements;
- installer/package type;
- signing requirements;
- test environment;
- known limitations.

The policy must be reviewed for every major Avalonia or .NET upgrade.

---

## 15. Publishing and Package Design

### 15.1 Publishing model

Initial public releases shall be:

- self-contained;
- non-AOT;
- platform- and architecture-specific;
- untrimmed unless trimming is separately proven safe;
- reproducible;
- signed where applicable;
- accompanied by SHA-256 checksums and a release manifest.

Example:

```text
dotnet publish src/PortfolioSaver.Avalonia \
  -c Release \
  -f net10.0 \
  -r <RID> \
  --self-contained true
```

Self-contained publication includes the required .NET runtime, but platform native dependencies must still be included or documented as appropriate.

### 15.2 Suggested package filenames

```text
DO-NOT-PANIC-Portfolio-Visualizer-<version>-win-x64-self-contained.exe
DO-NOT-PANIC-Portfolio-Visualizer-<version>-win-arm64-self-contained.exe

DO-NOT-PANIC-Portfolio-Visualizer-<version>-osx-x64-self-contained.dmg
DO-NOT-PANIC-Portfolio-Visualizer-<version>-osx-arm64-self-contained.dmg

DO-NOT-PANIC-Portfolio-Visualizer-<version>-linux-x64-self-contained.tar.gz
DO-NOT-PANIC-Portfolio-Visualizer-<version>-linux-arm64-self-contained.tar.gz
```

Optional native packages:

```text
DO-NOT-PANIC-Portfolio-Visualizer_<version>_amd64.deb
DO-NOT-PANIC-Portfolio-Visualizer_<version>_arm64.deb
DO-NOT-PANIC-Portfolio-Visualizer-<version>.x86_64.rpm
DO-NOT-PANIC-Portfolio-Visualizer-<version>.aarch64.rpm
```

### 15.3 Package contents

Each package shall contain:

- Avalonia executable and assemblies;
- required .NET 10 runtime files;
- YFinance.NET.Server executable and dependencies for the same RID;
- protocol and client dependencies;
- application assets, fonts, icons, and backgrounds approved for distribution;
- licenses and third-party notices;
- configuration defaults;
- migration support where applicable;
- uninstall or removal instructions;
- version and build metadata.

### 15.4 Windows packaging

Validate:

- x64 and ARM64 installers separately;
- code signing;
- Start Menu and desktop integration as approved;
- per-user or per-machine installation policy;
- clean uninstall;
- settings retention policy;
- no screensaver registration;
- no firewall change for loopback-only operation.

### 15.5 macOS packaging

Validate:

- correct `.app` bundle layout;
- x64 and ARM64 bundles;
- code signing;
- hardened runtime where required;
- notarization;
- DMG or PKG packaging;
- Application Support, Caches, and Logs paths;
- quarantine and first-launch behavior;
- child-process packaging and signing for YFinance.NET.Server.

### 15.6 Linux packaging

Validate:

- generic `tar.gz` release;
- Debian package where supported;
- RPM package where supported;
- desktop entry, icon, MIME or protocol integration only if needed;
- X11 and required native libraries;
- XDG paths;
- executable permissions;
- child-process location and launch behavior;
- uninstall and configuration retention;
- distro-specific support statements.

---

## 16. Build and Continuous Integration Matrix

### 16.1 Required build jobs

```text
Windows x64 build and tests
Windows ARM64 publish validation
macOS x64 build or publish validation
macOS ARM64 build and tests
Linux x64 build and tests
Linux ARM64 publish validation and hardware or VM tests
```

Cross-compilation may create artifacts, but it does not replace native runtime testing.

### 16.2 Test layers

- portable unit tests on multiple operating systems;
- YFinance protocol tests;
- owned-server lifecycle integration tests;
- Avalonia view-model tests;
- Avalonia headless or rendering tests where appropriate;
- screenshot and visual regression tests;
- platform service tests;
- installer and package tests;
- migration tests;
- endurance and recovery tests.

### 16.3 Toolchain pinning

Pin and record:

- .NET SDK version;
- Avalonia version;
- Skia/native graphics dependencies;
- package-manager lock files;
- operating-system runner images;
- installer tool versions;
- signing and notarization tooling;
- generated asset versions.

---

## 17. Visual and Behavioral Validation

### 17.1 Golden scenarios

Create deterministic scenarios for:

- normal market-open data;
- market closed;
- partial quote failure;
- stale cache;
- complete offline mode;
- YFinance server unavailable;
- upstream throttling;
- invalid symbol;
- slow response;
- background transition;
- multiple clocks and time-zone boundaries;
- daylight-saving changes;
- high-DPI scaling;
- 1080p, 1440p, 4K, ultrawide, and portrait displays where supported.

### 17.2 Successive zero-gap audits

A phase requiring successive zero-gap audits is accepted only when:

1. a complete audit finds and records all observed issues;
2. all approved issues are corrected or explicitly accepted;
3. a new complete audit is performed from the beginning;
4. at least two successive complete audits reveal no new material gaps.

Sampling or checking only changed areas does not count as a complete zero-gap audit.

### 17.3 Visual differences

Differences caused by font rendering, graphics backends, operating-system scaling, or platform conventions must be classified as:

- defect;
- acceptable platform variation;
- intentional design improvement;
- unsupported platform limitation.

Every accepted difference requires recorded rationale.

---

## 18. Performance and Resource Requirements

Measure and establish thresholds for:

- cold startup;
- warm startup;
- time to first usable frame;
- YFinance server readiness;
- idle CPU;
- active animation CPU;
- GPU usage where measurable;
- working-set memory;
- private memory;
- long-run memory growth;
- ticker frame pacing;
- chart animation frame pacing;
- image transition latency;
- network recovery;
- shutdown time;
- package size.

Test at least one modest x64 system and one modest ARM64 system. Development-workstation performance is not sufficient evidence.

---

## 19. Security and Privacy

The local-first product shall:

- bind YFinance to loopback by default;
- avoid unnecessary inbound listeners;
- avoid firewall exceptions for normal operation;
- validate downloaded or remote content;
- protect secrets with platform-appropriate storage;
- redact secrets and sensitive configuration from logs;
- verify update packages if automatic updates are introduced;
- document telemetry and default it according to project policy;
- inventory third-party dependencies and licenses;
- scan dependencies and release artifacts for known vulnerabilities;
- preserve user control over local portfolio and configuration data.

Remote YFinance exposure, LAN APIs, hosted services, and user accounts are outside this design and require separate security review.

---

## 20. Updating and Version Compatibility

Define:

- application semantic versioning;
- configuration schema versioning;
- YFinance protocol compatibility rules;
- minimum compatible child-server version;
- upgrade and downgrade behavior;
- migration backup retention;
- automatic-update policy, if any;
- security update process for bundled .NET runtime and Avalonia dependencies.

Because self-contained packages bundle .NET, runtime security updates require a new application release.

---

## 21. Diagnostics and Recovery

The application shall provide:

- structured logs;
- application, Avalonia, .NET, OS, architecture, and YFinance versions;
- startup-stage diagnostics;
- child-process launch diagnostics;
- protocol handshake state;
- provider and cache state;
- display and graphics information useful for support;
- safe export of diagnostic bundles;
- controlled degraded mode;
- recovery from child-process failure;
- recovery from display removal or resolution change;
- recovery from corrupt settings using backup and repair procedures.

Diagnostics shall not leak secrets or unnecessary portfolio information.

---

## 22. Major Risks and Mitigations

### Risk: treating Avalonia as a drop-in WPF compiler switch

Mitigation: perform a deliberate UI port with component-level parity and use Avalonia-native patterns.

### Risk: Windows dependencies hidden in non-UI projects

Mitigation: dependency scans, multi-OS builds, and two successive zero-leakage audits.

### Risk: visual parity becomes subjective

Mitigation: fixed fixtures, screenshots, manual reviews, tolerances, and a recorded difference ledger.

### Risk: platform support claims exceed actual testing

Mitigation: separate technical possibility, Avalonia support tier, and product-supported release status.

### Risk: YFinance child-process lifecycle behaves differently by OS

Mitigation: explicit process abstractions and repeated native-platform integration testing.

### Risk: packaging child executables fails signing or sandbox expectations

Mitigation: design package layout early and test signed release candidates on clean systems.

### Risk: Linux fragmentation

Mitigation: define supported distributions and native dependencies explicitly; publish generic archives plus selected native packages.

### Risk: performance regression on ARM or low-end hardware

Mitigation: establish hardware baselines during Phase 3 and profile every major visual component.

### Risk: configuration or secret loss during migration

Mitigation: backup-first migration, validation, rollback, and no silent discard.

### Risk: the clean successor repository loses important behavior

Mitigation: immutable legacy baseline, feature inventory, golden scenarios, and dual-UX parity period.

---

## 23. Completion Criteria

The migration is complete only when:

1. the successor repository is traceable to the locked legacy baseline;
2. all permanent non-platform projects target portable `net10.0`;
3. no active WPF or screensaver dependency remains;
4. Avalonia is the sole permanent UX;
5. normal operation remains a local installed application;
6. the main application manages YFinance.NET.Server locally over loopback;
7. startup, shutdown, multiple-instance, recovery, and orphan-process behavior are validated;
8. current features are preserved or differences are explicitly approved;
9. legacy settings migrate safely;
10. Windows x64, Windows ARM64, macOS x64, macOS ARM64, Linux x64, and Linux ARM64 releases are built or explicitly dispositioned by the approved support matrix;
11. releases are self-contained, non-AOT, signed where applicable, and tested on clean machines;
12. packaging includes the correct YFinance server for each RID;
13. security, dependency, license, performance, and recovery reviews are complete;
14. at least two successive complete parity audits reveal zero new material gaps;
15. at least two successive complete release audits reveal zero new material gaps;
16. the Avalonia/.NET architecture is recorded as the completed product solution.

---

## 24. Explicit Stop Point

When the completion criteria are satisfied, the project stops at:

```text
Avalonia desktop UX
        |
        v
Portable .NET 10 backend
        |
        v
Locally managed YFinance.NET service
```

A browser UX, hosted service, or native C++ backend is not an automatic next phase.

Any such change requires:

- a new problem statement;
- measurable justification;
- a separate CR;
- a separate design document;
- independent cost, risk, security, and maintenance analysis.

---

## 25. Final Migration Sequence

```text
0. Freeze and inventory the authoritative WPF application.
1. Separate portable logic from WPF and operating-system code.
2. Make YFinance packaging and owned-local lifecycle cross-platform.
3. Create the Avalonia application shell.
4. Port the UX component by component.
5. Run WPF and Avalonia in parallel until parity is proven.
6. Migrate user data and remove legacy runtime dependence.
7. Retire WPF and screensaver code.
8. Harden, package, validate, and release the cross-platform product.
```

The final product remains a local, integrated desktop or laptop application. Avalonia replaces WPF; portable .NET 10 remains the backend; and YFinance.NET.Server remains a locally managed companion process listening on loopback by default.

---

## 26. Authoritative External References

The implementation team shall re-check current documentation at the beginning of each release cycle because platform support tiers and operating-system requirements can change.

- Avalonia supported platforms: `https://docs.avaloniaui.net/docs/supported-platforms`
- .NET application publishing overview: `https://learn.microsoft.com/en-us/dotnet/core/deploying/`
- .NET runtime identifier catalog: `https://learn.microsoft.com/en-us/dotnet/core/rid-catalog`

At the time this design was locked, Avalonia documented desktop support for Windows, macOS, and Linux, with platform-specific support tiers and architecture details. Avalonia also documented Raspberry Pi OS ARM64 and ARM32 embedded Linux support. Self-contained .NET publishing was documented as including the required .NET runtime while still requiring appropriate platform native dependencies.
