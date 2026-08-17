# Blare — feature directions

87 candidate features grouped by the direction each pulls the product in, scoped
against what exists in the repo today.

Status: **SHIPPED** working · **PARTIAL** logic built and tested, no UI to reach
it · **NEXT** buildable now · **RESEARCH** needs a spike · **BLOCKED** can't be
done as scoped, reason given · **WITHDRAWN** deliberately not doing.
Effort is 1–3, relative not estimated.

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
| HEAR-04 | Break reminders with a working "Turn it down" action | SHIPPED | 1 |
| HEAR-05 | Gentle auto-attenuate after sustained exposure (opt-in) | NEXT | 2 |
| HEAR-06 | Quiet hours — hard ceiling during set hours | PARTIAL | 1 |
| HEAR-07 | Per-app hard caps ("Discord never above 60%") | SHIPPED | 1 |
| HEAR-08 | Headphone vs speaker profiles with separate thresholds | NEXT | 2 |
| HEAR-09 | Startup loudness guard — catch apps opening at 100% | NEXT | 1 |
| HEAR-11 | Weekly summary, generated locally | NEXT | 2 |
| HEAR-12 | Protection audit log — when safeguards were off | NEXT | 1 |
| HEAR-13 | Supervised mode — PIN-locked ceiling | NEXT | 2 |

HEAR-06 is enforced and tested; it has no settings UI yet, so the window can only
be changed in the settings file.

## GLA — Glance & flyout

Control without opening the app. The main window is where you configure; the
flyout is where you live.

| ID | Feature | Status | Effort |
|----|---------|--------|--------|
| GLA-01 | Tray flyout mixer — highest-value item here | NEXT | 2 |
| GLA-02 | Volume HUD overlay showing which app changed | SHIPPED | 2 |
| GLA-03 | Taskbar mini widget with live spectrum | RESEARCH | 3 |
| GLA-04 | Scroll tray icon to set master volume | BLOCKED | 1 |
| GLA-05 | Middle-click tray to mute focused app | SHIPPED | 1 |
| GLA-06 | Per-monitor flyout placement at correct DPI | PARTIAL | 2 |
| GLA-07 | Acrylic tint & opacity control | NEXT | 1 |
| GLA-08 | Compact and expanded flyout modes | NEXT | 1 |
| GLA-09 | Hide silent apps holding idle streams | NEXT | 1 |
| GLA-10 | Pin flyout open | NEXT | 1 |

GLA-02 arrived as a side effect of the hotkeys: every hotkey reports what it
changed through the flyout, naming the app.

GLA-04 is **blocked**, not merely unbuilt. `NotifyIcon` exposes no wheel event
because the taskbar receives `WM_MOUSEWHEEL`, not this process. Doing it needs a
global `WH_MOUSE_LL` hook running on every mouse move system-wide, which is a
permanent cost on the whole machine in exchange for a convenience. Revisit only
if the tray flyout (GLA-01) makes the hook worth installing anyway.

GLA-06 places correctly on the primary display's work area and scales with its
DPI; a genuinely multi-monitor placement picker is still open.

## CMD — Command & keyboard

The Raycast lane. Hotkeys acting on the *focused* app are what Windows cannot do.

| ID | Feature | Status | Effort |
|----|---------|--------|--------|
| CMD-01 | Command palette (`mute spotify`, `discord 40`) | NEXT | 2 |
| CMD-02 | Global hotkey: mute the focused app | SHIPPED | 1 |
| CMD-03 | Focused-app volume keys | SHIPPED | 1 |
| CMD-04 | Per-app hotkeys regardless of focus | NEXT | 2 |
| CMD-05 | Cycle output device with a naming HUD | NEXT | 2 |
| CMD-06 | Focus-mode toggle | NEXT | 1 |
| CMD-07 | Type an exact level into the readout | SHIPPED | 1 |
| CMD-08 | Scroll anywhere over a strip | SHIPPED | 1 |
| CMD-09 | Full keyboard navigation with focus visuals | NEXT | 1 |
| CMD-10 | Summon chord opening the flyout at the cursor | NEXT | 1 |

Shipped defaults: `Ctrl+Alt+M` mute the app in front, `Ctrl+Alt+Up/Down` move its
level, `Ctrl+Alt+Numpad0` mute everything. A combination already claimed by
another app is reported at startup rather than failing silently.

## DESK — Desk & level control

**Boost was removed.** It captured an app's audio, amplified it and rendered it
back out. On a real machine Blare's own process ended up on the desk, so it
captured its own output and re-rendered it in a feedback loop, producing a loud
burst of noise through the speakers. A limiter caps amplitude but cannot help:
a feedback loop pinned at the ceiling *is* the screech.

The conclusion is not "add another guard". An app whose purpose is protecting
hearing has no business being an audio source, so Blare no longer renders audio
at all. Volume only goes down from unity, through the same Windows APIs the
built-in mixer uses. DESK-05 through DESK-09 are withdrawn.

| ID | Feature | Status | Effort |
|----|---------|--------|--------|
| DESK-01 | Relative focus — duck others to make one app dominant | SHIPPED | 1 |
| DESK-02 | Solo, restoring exact prior state | SHIPPED | 1 |
| DESK-03 | Link strips (every Chromium renderer as one fader) | SHIPPED | 1 |
| DESK-04 | Logarithmic fader taper | PARTIAL | 1 |
| DESK-05–09 | Boost, gain, limiter, re-render | WITHDRAWN | — |

DESK-04's conversion is built and tested (`FaderTaper`). It is not yet applied to
the faders: switching the taper silently remaps every saved level, so it needs a
migration for stored volumes before it can be turned on.

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

ROUTE-01 is the one users ask for most and the one with no supported API. Windows
exposes it in Settings through `IAudioPolicyConfigFactory`, which is undocumented
and has changed shape between releases. It needs a spike that answers what
happens when the interface is missing, before it gets a milestone.

## SIG — Signal & visualisation

Real per-app FFT already runs. Most mixers only have a peak meter; this should be
visibly the best part of the product.

| ID | Feature | Status | Effort |
|----|---------|--------|--------|
| SIG-01 | Per-app FFT spectrum, log-spaced bands | SHIPPED | 2 |
| SIG-02 | Meter modes — spectrum / VU / scope / peak-hold | NEXT | 2 |
| SIG-03 | Peak hold & latching clip lights | SHIPPED | 1 |
| SIG-04 | Now-playing metadata via media transport controls | NEXT | 2 |
| SIG-05 | Art-sampled strip colour | NEXT | 2 |
| SIG-06 | Tunable band count, falloff, attack, floor | NEXT | 1 |
| SIG-07 | Animated tray icon spectrum | NEXT | 2 |
| SIG-08 | Full-screen visualiser | NEXT | 2 |
| SIG-09 | Adaptive analysis budget (battery, game, many apps) | NEXT | 2 |

## SCENE — Scenes & automation

| ID | Feature | Status | Effort |
|----|---------|--------|--------|
| SCENE-01 | Named scenes recalling a full level set | PARTIAL | 2 |
| SCENE-02 | Trigger on app launch, restore on close | NEXT | 2 |
| SCENE-03 | Voice-activity ducking, per-app and tunable | NEXT | 2 |
| SCENE-04 | Rules — when/then over app, time, device | NEXT | 3 |
| SCENE-05 | Fullscreen game detection | NEXT | 2 |
| SCENE-06 | Schedules (shares machinery with quiet hours) | NEXT | 1 |
| SCENE-07 | Idle restore to a baseline scene | NEXT | 1 |
| SCENE-08 | Scene import/export as local files | PARTIAL | 1 |

`SceneBook` and `SceneStore` are built, tested and persisted, import and export
included. Neither has a UI, so scenes can't yet be saved or recalled by a user.

## WIN — Windows fabric

Windows 10 gets graceful degradation, never a broken window.

| ID | Feature | Status | Effort |
|----|---------|--------|--------|
| WIN-01 | Start with Windows, minimised to tray | SHIPPED | 1 |
| WIN-02 | Windows 10 fallback — Acrylic below build 22000 | NEXT | 1 |
| WIN-03 | Actionable toasts | SHIPPED | 1 |
| WIN-04 | Media key handling | RESEARCH | 2 |
| WIN-05 | Taskbar jump list | NEXT | 1 |
| WIN-06 | Settings deep-links | NEXT | 1 |
| WIN-07 | Lock / RDP suspend and resume | NEXT | 2 |
| WIN-08 | Packaged apps resolved by AUMID, not exe path | NEXT | 2 |
| WIN-09 | Session grouping in the UI (`SessionGroupTracker`) | SHIPPED | 1 |
| WIN-10 | Battery-aware throttling | NEXT | 1 |

WIN-03 is served by the flyout rather than by Windows toasts — same job, one
surface, and it works when notifications are suppressed.

## A11Y — Access & inclusion

An app about hearing has no business being inaccessible.

| ID | Feature | Status | Effort |
|----|---------|--------|--------|
| A11Y-01 | Screen reader support, consent dialog keyboard-verified | NEXT | 2 |
| A11Y-02 | Honour Windows high contrast | NEXT | 1 |
| A11Y-03 | Reduced motion — freeze meters | SHIPPED | 1 |
| A11Y-04 | Colour-blind meter palettes | NEXT | 1 |
| A11Y-05 | Density options, correct at 150% / 200% scaling | PARTIAL | 1 |
| A11Y-06 | Localisation, safety copy included | NEXT | 2 |

A11Y-05: cards adapt across three density bands and the flyout now sizes from the
OS DPI rather than a scale that reads null before first show. A user-facing
density preference is still open.

## SHELL — Shell & distribution

| ID | Feature | Status | Effort |
|----|---------|--------|--------|
| SHELL-01 | Single-project MSIX packaging | SHIPPED | 1 |
| SHELL-02 | Real icon set (current assets are placeholders) | NEXT | 2 |
| SHELL-03 | Updater — the one sanctioned network call | SHIPPED | 2 |
| SHELL-04 | Crash-safe atomic settings writes | SHIPPED | 1 |
| SHELL-05 | Backup and restore | NEXT | 1 |
| SHELL-06 | Portable mode | NEXT | 1 |
| SHELL-07 | Consent persistence | SHIPPED | 1 |
| SHELL-08 | Signed MSIX | NEXT | 2 |

SHELL-08 is new and matters: the shipped `.msix` is unsigned, so Windows will not
install it by double-click. The portable zip is the only working download today.

---

## What to do next

1. **GLA-01 tray flyout** — still the surface that makes everything else get used,
   and the only remaining item on the original top five.
2. **UI for what's already built** — quiet hours, scenes and the fader taper are
   finished and tested behind no controls. Cheapest real value left.
3. **SHELL-08 signing** — an installer nobody can run is not a distribution.
4. **ROUTE-01 spike** — most-requested, no supported API; find out what breaks.

Anything at effort 3 should get a throwaway spike before it gets a milestone. The
boost pipeline is the standing reminder of what happens when that step is skipped.
