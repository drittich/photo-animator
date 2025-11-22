# Photo Animator

A Windows utility that scans a folder of JPEG frames and plays them back with scrubbing and keyboard shortcuts.

## Features
- Folder scan for `.jpg`/`.jpeg`, alphabetically sorted with lazy decode to keep startup fast.
- Playback controller with drop-frame math (elapsed-time driven), FPS selection (6-60), scrubbing slider, keyboard shortcuts, elapsed mm:ss:ff clock, and live dropped-frame counter.
- Auto-play kicks off as soon as a folder finishes preloading; controls stay disabled during preload to avoid interruptions.
- Double-buffered image surface to reduce flicker during swaps plus a memory-friendly cache that caps decoded frames to 1920x1080.
- Manual reload preserves FPS and attempts to restore state; a collapsible “Recent folders” tray keeps the last 6 folders handy and persists the last folder to seed the Open dialog.
- Help overlay (button or F1) lists the keyboard controls inline.

## Keyboard Shortcuts

| Key | Action |
| --- | ------- |
| Space | Play/Pause toggle |    
| Left Arrow | Previous frame |    
| Right Arrow | Next frame |
| Home | Rewind to first frame |
| R | Reload folder (re-scan frames) |
| F1 | Show help overlay |
| Esc | Close help overlay |

All shortcuts require the main window to have focus.

## Performance & Memory Safeguards
- Decode concurrency capped (default 1-4; heavy mode clamps to 2 when >500 frames).
- Soft preload cap of 500 frames; remaining frames decode on demand as you scrub/play.
- Soft memory ceiling (~500 MB) halts further preloads; decoding uses viewport-aware scaling plus a 1920x1080 cache cap and a 4096 px safety axis limit to avoid keeping full 6000x4000 frames in memory simultaneously.
- Cached frames cleared before reloads; decoded bitmaps frozen for lower marshaling and GC pressure.
- Corrupt image decodes are caught/logged and skipped so a single bad frame will not stop playback.

## Architecture Notes
- Minimal service locator bootstrap (`App.xaml.cs`) wires up services/view models without a full DI container, including a JSON-backed `AppSettingsService` for last/recent folders.
- `FrameCache` handles preload/on-demand decode with viewport-aware scaling, enforces a 1920x1080 cached size cap, and keeps memory/concurrency safeguards; `PlaybackMath` centralizes frame index math for drop-frame/loop calculations up to 60 FPS.
- `PlaybackController` emits frame change events with elapsed time and drop counts; `MainViewModel` auto-starts playback post-preload, tracks dropped frames/elapsed clock, and drives UI state (preload disabling, history tray, help overlay).

## Development
- Tests cover folder sorting and playback frame-index math: `dotnet test` (solution).
- Project targets `net9.0-windows` with WPF; no external runtime dependencies beyond the BCL.
