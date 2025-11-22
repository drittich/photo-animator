# Photo Animator

Small WPF utility that scans a folder of JPEG frames and plays them back with scrubbing, keyboard shortcuts, and adaptive decode safeguards.

## Features
- Folder scan for `.jpg`/`.jpeg`, alphabetically sorted with lazy decode to keep startup fast.
- Playback controller with drop-frame math (elapsed-time driven), FPS selection (6–24), scrubbing slider, and keyboard shortcuts: space (play/pause), arrows (step), Home (rewind), R (reload).
- Double-buffered image surface to reduce flicker during swaps.
- Manual reload preserves FPS and attempts to restore the current frame/resume playback after re-scan.

## Performance & Memory Safeguards
- Decode concurrency capped (default 1–4; heavy mode clamps to 2 when >500 frames).
- Soft preload cap of 500 frames; remaining frames decode on demand as you scrub/play.
- Soft memory ceiling (~500 MB) halts further preloads; decoding uses viewport-aware scaling plus a 4096 px axis limit to avoid keeping full 6000x4000 frames in memory simultaneously.
- Cached frames cleared before reloads; decoded bitmaps frozen for lower marshaling and GC pressure.
- Corrupt image decodes are caught/logged and skipped so a single bad frame will not stop playback.

## Architecture Notes
- Minimal service locator bootstrap (`App.xaml.cs`) wires up services/view models without a full DI container.
- `FrameCache` handles preload/on-demand decode with scaling strategy and safety caps; `PlaybackMath` centralizes frame index math for drop-frame/loop calculations.

## Development
- Tests cover folder sorting and playback frame-index math: `dotnet test` (solution).
- Project targets `net9.0-windows` with WPF; no external runtime dependencies beyond the BCL.
