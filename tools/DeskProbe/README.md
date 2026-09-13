# DeskProbe

A throwaway-sized diagnostic that answers one question: **which window owns the pixels
of the empty desktop, and can we put a window of our own there?**

It exists because the desktop groups need to live on the desktop — never covering another
app, and surviving "Show desktop" — and two attempts at that failed for reasons that
turned out to be misdiagnosed.

## Running it

```
dotnet run --project tools/DeskProbe                      # describe the desktop
dotnet run --project tools/DeskProbe -- --paint           # red probe, child of Progman
dotnet run --project tools/DeskProbe -- --paint --defview # red probe, child of SHELLDLL_DefView
dotnet run --project tools/DeskProbe -- --tree            # window tree under Progman
```

The `--paint` modes create a plain Win32 child window filled with solid red, above the
icon view, for twelve seconds. Press <kbd>Win</kbd>+<kbd>D</kbd> while it is up to check
that it survives, and watch the console line reporting `WindowFromPoint` over its centre
to check that it still receives mouse input.

## What it established

On Windows 11 build 26340 the classic desktop structure is intact:

```
Progman "Program Manager" [explorer]  1920x1080  visible, not cloaked
  └ SHELLDLL_DefView                  1920x1080  ex=0x00080000 (WS_EX_LAYERED)
      └ SysListView32 "FolderView"    ← what WindowFromPoint returns on bare desktop
```

The finding that matters:

| Parent of the probe | Paints? |
| ------------------- | ------- |
| `Progman`           | **no**  |
| `SHELLDLL_DefView`  | **yes** |

So a window must be parented to `SHELLDLL_DefView` itself, not to the `Progman` that owns
it. Parenting to `Progman` produces a window that reports itself visible, at the right
rectangle, above the icon view — and never draws a pixel. That silent failure is what
sent the first two attempts down the wrong path, including a wrong conclusion that
cross-process `SetParent` no longer works on this build. It does.

Confirmed with the probe parented to `SHELLDLL_DefView`:

- it receives mouse input (`WindowFromPoint` over it returns the probe, not the icon list);
- it survives repeated <kbd>Win</kbd>+<kbd>D</kbd> cycles;
- it never covers another application;
- its opaque red **blends with the wallpaper**, because `SHELLDLL_DefView` is itself a
  layered window and children compose into its surface. Whether that is enough to keep
  WPF's per-pixel transparency is the open question this tool was written to settle next.

## A warning about PowerShell

The same probes written as inline PowerShell gave two wrong answers, because passing
`$null` for a `string` P/Invoke parameter did not marshal as `NULL`: `FindWindow("Progman",
$null)` returned 0 while `FindWindow("Progman", "Program Manager")` returned the handle.
Any follow-up investigation here belongs in this project, not in a shell one-liner.
