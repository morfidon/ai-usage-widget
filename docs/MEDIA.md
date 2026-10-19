# README screenshots and demo

The README media is exported from the **actual WPF application views**, using the
same preview fixtures as UI verification. It does not capture the desktop or read
real provider accounts, credentials, conversation history or personal project names.
The visible sample names and quota values come from the repository's preview data.
The exporter explicitly selects English, independent of the Windows language or
the user's saved widget settings. Keep public screenshots and the GIF in English.

## Regenerate

On native Windows x64, with .NET SDK 9 and FFmpeg on PATH:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\export-readme-media.ps1
```

This builds and self-tests the widget in `.verify/readme-app`, compiles a separate
documentation-only harness in `.verify/readme-export`, and exports into `docs/media`.
The template under `scripts/readme-media` is not compiled into the shipped widget.
Existing running widget executables and user settings are not replaced.

After a successful widget build, `-SkipBuild` repeats just the documentation export.
An interactive Windows desktop is needed to create the views. The exporter reads
the current startup setting when constructing menus, but does not change it.
FFmpeg is needed only to encode this documentation GIF, not to use the widget.

## What it exports

| Asset | Contents |
|---|---|
| `demo.gif` | 132 frames at 12 fps: two providers, hiding Claude through its real menu handler, then opening Codex quota history. The native mascots animate between frames. |
| `overview.png` | Static first frame of the demo, including its sample-data label. |
| `widget.png` | Compact Codex/Claude widget with sample activity. |
| `one-provider.png` | Widget after hiding Claude. |
| `history.png` | Quota history in the details window. |
| `statistics.png` | Local activity summary and token chart, followed by live status. |
| `projects.png` | Local project/token ranking in the details window. |

Frames remain under `.verify`, which is ignored by Git. The GIF uses a shared
192-color palette and difference rectangles to keep its download small. Review
all stills and the first frame of each stage before committing regenerated assets.
Keep the sample-data caption in the README. The still screenshots provide a
non-animated way to inspect the UI.

## README layout references

The layout uses an early demo, a short explanation of value, visible setup/help
links and separate detailed guides. These choices follow [GitHub's README guidance](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/about-readmes)
and [Make a README's advice on visuals, installation and usage examples](https://www.makeareadme.com/).
Media and documentation use repository-relative links so they also work in clones.

## Statistics readability references

[NN/g on dashboard perception](https://www.nngroup.com/articles/dashboards-preattentive/)
and [Datawrapper on chart text](https://www.datawrapper.de/blog/text-in-data-visualizations)
informed the layout: consistent number alignment, labels beside their data, restrained
color, and fewer competing font sizes. The activity chart starts at zero and shows
its scale; empty buckets mean no recorded tokens. Exact counts remain available on
hover and through keyboard selection. Project token counts and shares have separate
fixed-width columns; current status stays separate from the selected time period.
