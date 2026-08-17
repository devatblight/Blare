# Blare — feature directions

87 candidate features grouped by the direction each pulls the product in, scoped
against what exists in the repo today.

Status: **SHIPPED** working · **NEXT** buildable now · **RESEARCH** needs a spike ·
**HEAVY** weeks, or a driver. Effort is 1–3, relative not estimated.

## The positioning call

FluentFlyout's identity is the flyout. Raycast's is the keystroke. Blare's should
be **hearing** — it's the only mixer that watches what your listening does to your
ears. EarTrumpet already does per-app volume well; nobody owns hearing health.

So: lead with **Hearing** as the identity, **Glance** as the daily surface (health
features are useless if you must open a window to benefit from them).

Blare stays local. No telemetry, no cloud sync, no accounts. The only sanctioned
network call in the product is the updater.

---

## HEAR — Hearing & exposure

Windows exposes relative signal level only, never true SPL at the ear. Everything
here must present itself in relative terms and say so.

| ID | Feature | Status | Effort |
|----|---------|--------|--------|
| HEAR-01 | Time-above-threshold tracking (`LoudnessTracker`, tested) | SHIPPED | 1 |
| HEAR-02 | Exposure timeline — when you were loud, not just a total | NEXT | 2 |
| HEAR-03 | Listening budget ring that visibly depletes | NEXT | 2 |
| HEAR-04 | Break reminders with a working "Turn it down" action | NEXT | 1 |
| HEAR-05 | Gentle auto-attenuate after sustained exposure (opt-in) | NEXT | 2 |
| HEAR-06 | Quiet hours — hard ceiling during set hours | NEXT | 1 |
| HEAR-07 | Per-app hard caps ("Discord never above 60%") | NEXT | 1 |
| HEAR-08 | Headphone vs speaker profiles with separate thresholds | NEXT | 2 |
| HEAR-09 | Startup loudness guard — catch apps opening at 100% | NEXT | 1 |
| HEAR-10 | Transient spike protection (`Limiter` exists, needs a live path) | RESEARCH | 3 |
| HEAR-11 | Weekly summary, generated locally | NEXT | 2 |
| HEAR-12 | Protection audit log — when safeguards were off | NEXT | 1 |
| HEAR-13 | Supervised mode — PIN-locked ceiling | NEXT | 2 |

## GLA — Glance & flyout

Control without opening the app. The main window is where you configure; the
flyout is where you live.

| ID | Feature | Status | Effort |
|----|---------|--------|--------|
| GLA-01 | Tray flyout mixer — highest-value item here | NEXT | 2 |
| GLA-02 | Volume HUD overlay showing which app changed | NEXT | 2 |
| GLA-03 | Taskbar mini widget with live spectrum | RESEARCH | 3 |
| GLA-04 | Scroll tray icon to set master volume | NEXT | 1 |
| GLA-05 | Middle-click tray to mute focused app | NEXT | 1 |
| GLA-06 | Per-monitor flyout placement at correct DPI | NEXT | 2 |
| GLA-07 | Acrylic tint & opacity control | NEXT | 1 |
| GLA-08 | Compact and expanded flyout modes | NEXT | 1 |
| GLA-09 | Hide silent apps holding idle streams | NEXT | 1 |
| GLA-10 | Pin flyout open | NEXT | 1 |

## CMD — Command & keyboard

The Raycast lane. Hotkeys acting on the *focused* app are what Windows cannot do.

| ID | Feature | Status | Effort |
|----|---------|--------|--------|
| CMD-01 | Command palette (`mute spotify`, `discord 40`) | NEXT | 2 |
| CMD-02 | Global hotkey: mute the focused app | NEXT | 1 |
| CMD-03 | Focused-app volume keys | NEXT | 1 |
| CMD-04 | Per-app hotkeys regardless of focus | NEXT | 2 |
| CMD-05 | Cycle output device with a naming HUD | NEXT | 2 |
| CMD-06 | Focus-mode toggle | NEXT | 1 |
| CMD-07 | Type an exact level into the readout | NEXT | 1 |
| CMD-08 | Scroll anywhere over a strip | NEXT | 1 |
| CMD-09 | Full keyboard navigation with focus visuals | NEXT | 1 |
| CMD-10 | Summon chord opening the flyout at the cursor | NEXT | 1 |

## DESK — Desk & level control

True above-100% boost is **blocked**: per-process loopback capture is applied
*after* session volume and mute, so silencing the original also silences the copy
you would amplify. Measured — mute → captured peak 0.000000 (0.567 unmuted);
volume 100/50/0% → 0.699/0.349/0.000.

| ID | Feature | Status | Effort |
|----|---------|--------|--------|
| DESK-01 | Relative focus boost — duck others, lift master | NEXT | 1 |
| DESK-02 | Solo, restoring exact prior state | NEXT | 1 |
| DESK-03 | Link strips (every Chromium renderer as one fader) | NEXT | 1 |
| DESK-04 | Logarithmic fader taper | NEXT | 1 |
| DESK-05 | True boost via endpoint redirection (`IPolicyConfig`, undocumented) | RESEARCH | 3 |
| DESK-06 | True boost via virtual audio device (signed driver) | HEAVY | 3 |
| DESK-07 | Per-app EQ — a health feature in audio clothing | HEAVY | 3 |
| DESK-08 | Loudness matching across apps (relative) | HEAVY | 3 |
| DESK-09 | Mono & balance per app (accessibility) | HEAVY | 3 |

## ROUTE — Routing & devices

| ID | Feature | Status | Effort |
|----|---------|--------|--------|
| ROUTE-01 | Per-app output device | RESEARCH | 3 |
| ROUTE-02 | Per-device volume memory | NEXT | 2 |
| ROUTE-03 | Auto-switch on headphone plug | NEXT | 1 |
| ROUTE-04 | Survive device hot-swap without losing state | NEXT | 2 |
| ROUTE-05 | Microphone strips (`eCapture` sessions) | NEXT | 2 |
| ROUTE-06 | Rename devices to something human | NEXT | 1 |
| ROUTE-07 | Hide unused devices | NEXT | 1 |
| ROUTE-08 | Bluetooth battery & codec | RESEARCH | 2 |
| ROUTE-09 | Follow-focus routing | RESEARCH | 3 |

## SIG — Signal & visualisation

Real per-app FFT already runs. Most mixers only have a peak meter; this should be
visibly the best part of the product.

| ID | Feature | Status | Effort |
|----|---------|--------|--------|
| SIG-01 | Per-app FFT spectrum, log-spaced bands | SHIPPED | 2 |
| SIG-02 | Meter modes — spectrum / VU / scope / peak-hold | NEXT | 2 |
| SIG-03 | Peak hold & latching clip lights | NEXT | 1 |
| SIG-04 | Now-playing metadata via media transport controls | NEXT | 2 |
| SIG-05 | Art-sampled strip colour | NEXT | 2 |
| SIG-06 | Tunable band count, falloff, attack, floor | NEXT | 1 |
| SIG-07 | Animated tray icon spectrum | NEXT | 2 |
| SIG-08 | Full-screen visualiser | NEXT | 2 |
| SIG-09 | Adaptive analysis budget (battery, game, many apps) | NEXT | 2 |

## SCENE — Scenes & automation

| ID | Feature | Status | Effort |
|----|---------|--------|--------|
| SCENE-01 | Named scenes recalling a full level set | NEXT | 2 |
| SCENE-02 | Trigger on app launch, restore on close | NEXT | 2 |
| SCENE-03 | Voice-activity ducking, per-app and tunable | NEXT | 2 |
| SCENE-04 | Rules — when/then over app, time, device | NEXT | 3 |
| SCENE-05 | Fullscreen game detection | NEXT | 2 |
| SCENE-06 | Schedules (shares machinery with quiet hours) | NEXT | 1 |
| SCENE-07 | Idle restore to a baseline scene | NEXT | 1 |
| SCENE-08 | Scene import/export as local files | NEXT | 1 |

## WIN — Windows fabric

Windows 10 gets graceful degradation, never a broken window.

| ID | Feature | Status | Effort |
|----|---------|--------|--------|
| WIN-01 | Start with Windows, minimised to tray | NEXT | 1 |
| WIN-02 | Windows 10 fallback — Acrylic below build 22000 | NEXT | 1 |
| WIN-03 | Actionable toasts | NEXT | 1 |
| WIN-04 | Media key handling | RESEARCH | 2 |
| WIN-05 | Taskbar jump list | NEXT | 1 |
| WIN-06 | Settings deep-links | NEXT | 1 |
| WIN-07 | Lock / RDP suspend and resume | NEXT | 2 |
| WIN-08 | Packaged apps resolved by AUMID, not exe path | NEXT | 2 |
| WIN-09 | Session grouping in the UI (`SessionGroupTracker` built, unused) | NEXT | 1 |
| WIN-10 | Battery-aware throttling | NEXT | 1 |

## A11Y — Access & inclusion

An app about hearing has no business being inaccessible.

| ID | Feature | Status | Effort |
|----|---------|--------|--------|
| A11Y-01 | Screen reader support, consent dialog keyboard-verified | NEXT | 2 |
| A11Y-02 | Honour Windows high contrast | NEXT | 1 |
| A11Y-03 | Reduced motion — freeze meters | NEXT | 1 |
| A11Y-04 | Colour-blind meter palettes | NEXT | 1 |
| A11Y-05 | Density options, correct at 150% / 200% scaling | NEXT | 1 |
| A11Y-06 | Localisation, safety copy included | NEXT | 2 |

## SHELL — Shell & distribution

| ID | Feature | Status | Effort |
|----|---------|--------|--------|
| SHELL-01 | Single-project MSIX packaging | SHIPPED | 1 |
| SHELL-02 | Real icon set (current assets are placeholders) | NEXT | 2 |
| SHELL-03 | Updater — the one sanctioned network call | NEXT | 2 |
| SHELL-04 | Crash-safe atomic settings writes | NEXT | 1 |
| SHELL-05 | Backup and restore | NEXT | 1 |
| SHELL-06 | Portable mode | NEXT | 1 |
| SHELL-07 | Consent persistence (`ConsentState` tested but in-memory) | NEXT | 1 |

---

## If you only do five

1. **GLA-01 tray flyout** — the surface that makes everything else get used.
2. **SHELL-07 consent persistence** — a safety feature that forgets itself on restart isn't one.
3. **CMD-02 mute the focused app** — the one capability Windows lacks and everyone wants.
4. **HEAR-04 break reminders** — the cheapest expression of the product thesis.
5. **WIN-09 session grouping** — already written and tested, just not wired up.

Anything at effort 3 should get a throwaway spike before it gets a milestone. The
boost pipeline is the standing reminder of what happens when that step is skipped.
