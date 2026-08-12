# Why these files are duplicated

These are the same model classes as `src/Megruli.Shared/*.cs`, copied here instead of
referenced via a project reference.

**Reason:** the GitHub deployment repo only holds this one app (no solution, no sibling
projects) and gets updated by manually uploading files through GitHub's web UI — a
`ProjectReference` to `../Megruli.Shared` can't work there since nothing exists above the
repo root. Keeping a self-contained copy here means this whole `Megruli.App` folder can be
uploaded as-is.

**Trade-off:** if you change a model in `src/Megruli.Shared/`, copy the same change into
`src/Megruli.App/Shared/` (or vice versa) — they will drift silently otherwise.
`tools/Megruli.AudioSlicer` still references `src/Megruli.Shared` directly, so that copy
can't be removed either.
