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

# Microsoft Store Publication Plan

Research date: 2026-07-12

Status: Research and implementation plan only. Public distribution remains
frozen under `docs/RELEASE_1_0_BASELINE.md`; this document does not authorize a
Store submission or alter the administratively closed status of `CR-174`.

> **HARD STOP:** This is a research artifact, not permission to begin MSIX
> packaging. Do not start Phase 1 or create Store submissions until the owner
> explicitly approves Phase 0, its evidence is recorded in new follow-on CRs,
> and the account, privacy, AI, and third-party-rights blockers are resolved.

## Executive Decision

Use a **Store-ingested MSIX package** built from source. Do not submit the
existing Inno Setup EXE as the Store payload.

This is the only path that satisfies the project's trust and cost goals without
buying and renewing an Authenticode certificate:

- Microsoft hosts, signs, updates, and distributes Store MSIX packages.
- Microsoft re-signs the package after certification; no production signing
  certificate is purchased or managed by this project.
- The Store checks for MSIX updates automatically.
- Package flights, private audiences, gradual rollout, and clean package
  lifecycle are available.

The alternative MSI/EXE Store path is rejected for this project because every
PE file would require a CA-trusted Authenticode signature, the project would
have to host an immutable versioned installer URL, and installation must be
silent. That path preserves Inno but does not solve the signing-cost problem.

Official basis:

- [Win32 Store distribution options](https://learn.microsoft.com/en-us/windows/apps/distribute-through-store/how-to-distribute-your-win32-app-through-microsoft-store)
- [Windows code-signing options](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options)
- [MSI/EXE Store package requirements](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msi/app-package-requirements)

## Cost and Account Gate

### Expected costs

- Store MSIX production signing: **USD 0**; Microsoft signs after certification.
- Store certification: **USD 0** per submission.
- New Individual developer onboarding: **USD 0** only where Microsoft's new
  individual onboarding flow is available and entered through
  `storedeveloper.microsoft.com`.
- Company developer account: approximately **USD 99 one time**, regionally
  adjusted; no annual renewal fee is documented.

### Do not register until account type is confirmed

Microsoft says Individual accounts are for independent hobby/non-commercial
developers, but Store policy also requires a Company account when a reasonable
consumer would interpret the application or publisher name as a business
entity. The publisher name **SANYALnet Labs** may meet that test even though the
software is free and non-commercial. Account type and publisher display name
are difficult or impossible to change later.

Required human decision gate:

1. Ask Partner Center onboarding support in writing whether Supratim Sanyal may
   use the free Individual account while publishing the free app under the
   SANYALnet Labs brand.
2. Include the exact product name, publisher display name, non-commercial
   license, no-payment/no-account behavior, and the fact that the app displays
   public delayed market data but never requests financial-account credentials.
3. Save Microsoft's response in project-private release records.
4. If Microsoft does not explicitly approve Individual use, create a Company
   account and budget the one-time fee.
5. Do not choose an Individual account merely to avoid the fee; an incorrect
   account type creates certification and account-enforcement risk.

Owner: Supratim Sanyal. Trigger: before Partner Center registration or any MSIX
implementation CR is opened. Evidence: dated Microsoft support response and the
owner's recorded account-type decision.

The app does **not** require bank, brokerage, credit-card, tax, cryptocurrency,
or exchange credentials, so Policy 10.8.3's financial-account-information rule
does not independently force Company status. The business-like publisher-name
rule remains the issue.

Official basis:

- [Open a Partner Center developer account](https://learn.microsoft.com/en-us/windows/apps/publish/partner-center/open-a-developer-account)
- [Microsoft Store Policies, including account type](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies)
- [Partner Center account management](https://learn.microsoft.com/en-us/windows/apps/publish/faq/manage-your-account)

## Target Store Product

- Product name reservation: `DO NOT PANIC PORTFOLIO VISUALIZER`
- Publisher: the Partner Center-approved form of `SANYALnet Labs`
- Developed by: `Supratim Sanyal`
- Price: Free
- Trial: None
- Architecture: x64
- Device family: Windows Desktop only
- Proposed minimum OS: Windows 10 version 2004 / build 19041, pending clean
  package validation on that minimum
- Proposed primary category: `Personal finance`
- Proposed subcategory: `Banking + investments`
- Secondary-category candidate: `Productivity` or `Utilities + tools`
- Initial listing language: English (United States) only
- Initial markets: start with a deliberately reviewed market set rather than
  automatically selecting every market; expand after policy/content review
- Initial publish control: certification with **manual publishing hold**, then
  an explicit owner decision to publish

The listing must describe the app as a delayed, passive visualization and
ambient-information product. It must prominently state that it is not suitable
for financial planning, portfolio monitoring, investment decisions, trading,
or real-time market use.

## MSIX Architecture

### Packaging mechanism

Add a Windows Application Packaging Project to the existing Visual Studio
solution and use it to produce an x64 `.msixupload` for Partner Center. Microsoft
documents this source-based path for WPF/Win32 apps and permits multiple desktop
executables in one package, with one tile entry point.

Official basis:

- [Package a desktop app from source with Visual Studio](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-packaging-dot-net)
- [Upload MSIX packages](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/upload-app-packages)
- [MSIX packaging concepts](https://learn.microsoft.com/en-us/windows/msix/desktop/before-packaging-overview)

### Package contents

- `PortfolioSaver.Desktop.exe`: sole Start-menu/tile entry point
- desktop/config shared assemblies and content
- `YFinance.NET.Server.exe` and dependencies under the package's existing
  `YFinanceServer` layout
- root `LICENSE`
- `THIRD-PARTY-NOTICES.md`
- `THIRD-PARTY-LICENSES/APACHE-2.0.txt`
- three bundled backgrounds and attribution manifest
- required Store icon/tile assets

`PortfolioSaver.Config.exe` may remain packaged for diagnostics/harness
compatibility, but it should not be a second Store tile. Normal settings already
open in-process from the desktop app.

### Manifest contract

- Use Partner Center's reserved package identity values exactly:
  `Package/Identity/Name`, `Package/Identity/Publisher`, and
  `PublisherDisplayName`.
- One `Application` entry for `PortfolioSaver.Desktop.exe`.
- `uap10:TrustLevel="mediumIL"` and `runFullTrust`, because this is a full-trust
  WPF/Desktop Bridge app that launches its bundled child server.
- Declare only capabilities actually required. Do not request broad file-system,
  documents-library, enterprise-authentication, or similar capabilities.
- Explain `runFullTrust` in Partner Center: it is needed for the WPF desktop
  process, child YFinance.NET server process, localhost TCP IPC, user-selected
  background folders, and normal Win32 window/process integration.
- Keep the human-visible/project version `1.0`. The MSIX manifest must use the
  required four-part numeric representation, initially `1.0.0.0`; this is a
  platform encoding and delivery sequence, not a second human product version.
  For Store updates within human release 1.0, increment the third component
  (`1.0.1.0`, `1.0.2.0`, and so on). Keep the fourth component zero because it
  is reserved for Store use. Do not expose this package sequence in the About
  dialog as a separate product version.

Restricted capabilities require a usage explanation and may lengthen review:
[app capability declarations](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations).

### Code compatibility spikes required before implementation is accepted

1. **Child-process launch:** prove the packaged desktop can locate, start,
   handshake with, and stop the bundled `YFinance.NET.Server.exe` from the
   read-only package path.
2. **Single instance and IPC:** prove the named mutex and localhost TCP port
   behave under package identity and medium integrity.
3. **Settings UI:** prove in-process Settings, validation, folder picker, API-key
   protection, and immediate apply behavior.
4. **Storage:** decide whether to keep the explicit
   `%LOCALAPPDATA%\DoNotPanicPortfolioVisualizer` root or introduce a
   package-aware root. Verify update persistence and uninstall behavior rather
   than assuming MSIX deletes this explicit external folder.
5. **Backgrounds:** prove bundled images load from a read-only package, remote
   images download only to writable Local AppData, `.TMP` cleanup works, and a
   user-selected external folder remains untouched.
6. **Network:** prove Yahoo/YFinance, RSS, optional AI, OpenRouter model
   discovery, weather, image downloads, and upstream-sync checks work without
   unnecessary capabilities.
7. **License files:** prove legal files are present in the installed package and
   About/Help can read them from a read-only location.
8. **Full-screen/scaling:** repeat VM, laptop, high-DPI, multi-monitor, and
   ultrawide validation under package activation.
9. **Update:** prove settings/cache survival across `1.0.0.0` to a higher test
   package version.
10. **Uninstall:** prove package files and package-owned state are removed;
    document any intentionally retained explicit Local AppData and provide an
    in-app reset/delete-data action if MSIX cannot remove it.

MSIX package files are read-only and installed per user under WindowsApps;
packaged desktop behavior differs from the current elevated, all-users Inno
installation. See [how packaged desktop apps run](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes).

## Installer-to-Store Behavior Changes

MSIX has no equivalent of the current custom Inno wizard pages or elevated
uninstall cleanup script. Therefore:

- Do not attempt custom install actions.
- Keep RSS as the first-run default.
- Move optional AI-key setup entirely to Settings/first-run guidance.
- Present the project's license in the Store's additional license terms and in
  About/Help; do not rely on a pre-install license page.
- Let Windows create the Start-menu entry. Do not create a desktop shortcut
  automatically.
- Remove assumptions about `%PROGRAMFILES%\SANYALnet Labs\...` from the Store
  build path.
- Do not launch the app automatically at installation completion.
- Treat Inno and Store as separate distribution channels during migration.

Existing Inno users are not automatically converted into Store users. Before
public launch, define a safe migration story:

- detect/document side-by-side Inno + Store installations
- prevent confusing duplicate shortcuts and single-instance conflicts
- preserve or export settings before the Inno uninstaller deletes Local AppData
- recommend uninstalling Inno before acquiring the Store version unless
  side-by-side behavior has been explicitly proven

## Store Policy Work

### Privacy policy is mandatory

Microsoft policy treats Desktop Bridge and Win32 products as inherently needing
a privacy policy. Publish a stable public privacy-policy URL and link it from
the app and Partner Center. It must accurately cover:

- no Microsoft/brokerage account and no telemetry by the app
- locally stored settings, API key, cache, traces, and downloaded images
- user-configured ticker symbols and background-folder paths
- network requests to Yahoo/YFinance endpoints, RSS sources, weather/image
  sources, GitHub upstream checks, and a user-selected AI provider
- API-key protection and the fact that the key is sent to the configured AI
  endpoint to perform the requested news operation
- retention/deletion controls and uninstall limitations
- no sale of personal information
- support/privacy contact method

Desktop Bridge privacy-policy requirement:
[Microsoft Store Policies 10.5](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies) and
[MSIX privacy/support information](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/support-info).

Phase 0 must produce a tracked draft privacy policy and a proposed stable public
URL, cross-checked against `README.md`, `LICENSE`, actual storage/network code,
and uninstall behavior. Owner review is required before Phase 1. Publication of
the final policy URL is required before Partner Center submission.

### Live generative AI requires product work

The optional AI news mode is dynamic generative-AI content. Before submission:

- disclose live generative AI in Store metadata
- identify it in Partner Center submission declarations/notes
- retain RSS as the safe default and clearly label AI as optional
- add an in-app **Report inappropriate AI news** action that reaches the
  developer without exposing the API key or other secrets
- publish a support process for reviewing reports and taking corrective action
- ensure prompts/output handling minimize prohibited or age-inappropriate
  content; RSS fallback must remain functional

These are explicit Store Policy 11.16 requirements:
[Microsoft Store Policies](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies).

### Third-party content and financial posture

- Verify that Yahoo/YFinance usage and displayed data comply with applicable
  terms; the existing trademark disclaimer is necessary but not by itself proof
  of distribution rights.
- Keep Apache 2.0 notices for yfinance/YFinance.NET lineage.
- Keep Wikimedia/background attribution and licenses reviewable.
- Ensure Store screenshots do not feature third-party images unless their
  licenses permit promotional use and attribution is supplied where required.
- Treat RSS and AI output as live external content in the IARC questionnaire.
- Do not describe quotes as real-time. Use the existing minimum-15-minute delay
  disclosure and the no-planning/no-monitoring disclaimer in the first listing
  paragraph.
- Do not collect brokerage credentials, execute transactions, recommend trades,
  or imply fiduciary/financial-advisory functionality.

## Store Listing Deliverables

Create a versioned `store/` source directory containing:

- long description
- short description (target under 270 characters)
- up to 20 concise feature lines
- up to 7 accurate keywords/phrases
- copyright/trademark text
- additional license terms or a stable license URL
- privacy-policy URL
- support email/URL and app website
- AI disclosure and reporting instructions
- certification notes
- IARC answer record
- market/category decisions
- asset manifest with source/license/size/hash

Listing assets:

- at least 4 clean desktop screenshots, although only 1 is mandatory
- settings screenshot showing RSS default and optional AI configuration without
  any real key
- full-screen screenshot showing delayed-data disclaimer
- 300 x 300 app tile icon and all package-manifest scale variants
- recommended 1920 x 1080 promotional art without embedded UI text, currency,
  flags, or unlicensed imagery
- no VirtualBox chrome, developer paths, API keys, test overlays, or stale
  version labels

Microsoft listing requirements and asset guidance:

- [Store listing information](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/add-and-edit-store-listing-info)
- [Screenshots and images](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/screenshots-and-images)
- [App categories](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msi/categories-and-subcategories)

## Certification Test Plan

### Automated gates

1. Build Release x64 package and `.msixupload` from a clean checkout.
2. Validate manifest schema and package contents.
3. Secret-scan package and listing artifacts.
4. Install a self-signed test package only on isolated test machines with the
   test certificate trusted locally.
5. Run the latest Windows App Certification Kit from an active user session.
6. Treat all required WACK failures as blockers and review optional Desktop
   Bridge warnings explicitly.
7. Run the full local suite and package-specific tests.
8. Run DeepSeek code/document review and artifact second opinion under existing
   project workflow.

WACK guidance:
[Windows App Certification Kit](https://learn.microsoft.com/en-us/windows/uwp/debug-test-perf/windows-app-certification-kit) and
[Desktop Bridge tests](https://learn.microsoft.com/en-us/windows/uwp/debug-test-perf/windows-desktop-bridge-app-tests).

### Installed-package matrix

- Windows 10 x64 22H2 VM, including the current 2-CPU constrained VM
- Windows 11 x64 current supported release
- 1366 x 768 at 100%/125% scaling
- 1920 x 1080 at 100%/150% scaling
- laptop native high-DPI display
- multi-monitor and ultrawide where available
- standard user, no administrative elevation
- online, offline at startup, DNS failure, Yahoo throttling/latency, RSS failure,
  AI failure/429/timeout, background-download failure, and recovery
- install, first launch, Settings apply/cancel, full screen, double-click/F11/ESC,
  update, repair/reset, uninstall, reinstall
- child-server ownership and zero orphan processes after exit
- two consecutive clean two-hour installed-package soaks before submission

### Certification notes draft topics

- app purpose and prominent non-financial-tool limitation
- delayed public market data and network dependency
- exact path to Settings and RSS/AI mode
- AI is optional; RSS is default
- no login is required for RSS mode
- if AI mode is tested, provide a temporary reviewer key only through the secure
  certification-notes mechanism, never in the package
- steps to exercise full screen and background folders
- reason for `runFullTrust`
- child YFinance.NET server is bundled first-party application functionality,
  localhost-only, owner-lifetime-bound, and not an NT service
- expected offline/degraded UI behavior
- date of notes and support contact

Certification typically takes up to three business days and covers security,
technical behavior, and content/listing compliance:
[certification FAQ](https://learn.microsoft.com/en-us/windows/apps/publish/faq/get-your-app-certified).

## Partner Center Submission Sequence

1. Resolve account type and publisher identity with Microsoft.
2. Open/verify the Partner Center account and join Windows and Xbox.
3. Reserve `DO NOT PANIC PORTFOLIO VISUALIZER` as an MSIX app name.
4. Record the Store identity values in a private release configuration file;
   inject them into the manifest without hard-coding personal secrets.
5. Create the first submission with Free pricing, reviewed markets, Desktop x64,
   category, privacy/support details, and manual publishing hold.
6. Complete the IARC questionnaire accurately for live news, web content, and
   optional AI output.
7. Upload the `.msixupload` package.
8. Upload listing text and reviewed assets.
9. Declare `runFullTrust` usage and live generative AI; add complete tester notes.
10. Submit for certification while retaining manual publish hold.
11. Triage every certification message into project-native evidence/CRs.
12. After certification, install the Store-delivered package on clean Windows 10
    and 11 systems and run a release smoke/soak before selecting Publish now.
13. Publish initially to a controlled audience/link if Partner Center permits the
    desired validation cohort; otherwise use a narrow reviewed market rollout.
14. Expand to public discoverability only after owner approval.

App identity and submission controls:

- [Reserve an MSIX app name](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/reserve-your-apps-name)
- [View Store product identity](https://learn.microsoft.com/en-us/windows/apps/publish/view-app-identity-details)
- [Pricing and availability](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/price-and-availability)
- [Submission options and publishing hold](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/manage-submission-options)

## Release and Update Operations

For the first submission, use Partner Center manually. Do not automate an
unproven workflow.

After first certification:

- add a deterministic Store package build script
- archive package-content manifest, SHA-256, WACK report, test evidence, and
  Partner Center submission ID
- use package flights/private audience for pre-release validation
- use gradual rollout for updates, beginning at 5-10%, monitor crash/quality
  analytics, then expand or halt
- never reuse a package version
- keep GitHub/Itch Inno publishing separate until the owner decides whether to
  retire or retain unsigned non-Store distribution
- do not publish private/self-signed MSIX test packages publicly

Microsoft supports gradual update rollout and halting without rolling already
updated users backward:
[gradual package rollout](https://learn.microsoft.com/en-us/windows/apps/publish/gradual-package-rollout).

## Phased Work Plan and Exit Gates

### Phase 0 - Account and policy clearance

Deliver:

- written account-type answer from Microsoft
- approved publisher display name
- reserved product name
- tracked privacy-policy draft, proposed stable public URL, and owner review
- support endpoint and AI-reporting policy drafts
- Yahoo/RSS/background-content rights review
- explicit owner approval to open Phase 1 implementation CRs

Exit gate: no unresolved account, publisher, privacy, AI, or third-party-content
policy blocker.

### Phase 1 - MSIX feasibility spike

Deliver:

- Windows Application Packaging Project
- Partner Center-compatible placeholder identity for local testing
- self-signed x64 package
- successful desktop launch and YFinance.NET child-server lifecycle
- storage/update/uninstall behavior report

Exit gate: core runtime works as packaged mediumIL/full-trust without elevation
or custom install actions.

### Phase 2 - Product compliance changes

Deliver:

- package-aware storage/lifecycle changes if Phase 1 proves they are needed
- in-app privacy/support/license links
- AI-content reporting mechanism and support process
- first-run/settings UX replacing installer-only AI setup
- Inno-to-Store migration/side-by-side safeguards

Exit gate: all policy and migration acceptance tests pass.

### Phase 3 - Store-grade packaging and assets

Deliver:

- production manifest and complete asset set
- deterministic `.msixupload` build
- Store listing source package
- WACK automation/report archiving
- package install/update/uninstall harness

Exit gate: WACK required tests pass and package/listing have no secret, license,
identity, version, or asset gaps.

### Phase 4 - Certification candidate

Deliver:

- clean Windows 10/11 matrix
- two consecutive clean two-hour Store-package soaks
- DeepSeek advisory reviews
- completed Partner Center draft with manual publishing hold
- certification notes and restricted-capability explanation

Exit gate: owner approves submission to certification, not public release.

### Phase 5 - Certification and controlled publication

Deliver:

- certification report
- remediation CRs for every failure or warning accepted for action
- Store-delivered package validation
- explicit owner Publish-now decision
- controlled initial publication and monitored expansion

Exit gate: Store listing is live, Store package installs/updates cleanly, support
and privacy endpoints are operational, and no release-blocking issue remains.

## Risks Ranked

1. **Publisher/account mismatch:** SANYALnet Labs may require Company account.
2. **AI policy gap:** no current in-app inappropriate-AI-content reporting path.
3. **Third-party rights:** Yahoo/RSS/background promotional and distribution
   rights require explicit review, not only attribution.
4. **Package lifecycle:** explicit Local AppData may survive MSIX uninstall.
5. **Child process:** packaged server discovery/startup may need path changes.
6. **Inno migration:** Store acquisition does not automatically replace the
   existing machine-wide Inno installation.
7. **Installer-only configuration:** custom AI setup cannot carry into MSIX.
8. **Version representation:** MSIX requires four numeric fields even though the
   human product version remains `1.0`.
9. **Restricted capability review:** `runFullTrust` requires a clear explanation.
10. **Dynamic content rating:** RSS and AI content must be represented accurately
    in IARC, metadata, privacy, and support processes.

## Recommended Next Action

Complete **Phase 0 only** before writing packaging code. The first action is to
obtain Microsoft's written account-type guidance for the SANYALnet Labs
publisher name. In parallel, draft the privacy policy, support endpoint, AI
disclosure/reporting policy, and third-party-content rights matrix. These items
can block certification regardless of whether the MSIX itself builds.
