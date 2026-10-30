# Contributing to XylemUI

XylemUI is in its design and spike phase. The design is `docs/design.ja.md` (Japanese), the order of work is `docs/roadmap.md`, and the conventions are `docs/conventions.md`.

## Workflow

1. If the design still branches, it is settled first (a Grill, then a `design` Issue that updates `docs/design.ja.md`).
2. Every change starts from an Issue opened with a template in `.github/ISSUE_TEMPLATE/`. Blank issues are disabled.
3. Branch from `main` as `type/<issue-number>-<slug>`.
4. Run `./build.ps1` before opening a pull request. It checks formatting (CSharpier), builds with warnings as errors, and runs every test project.
5. Open the pull request with the template. Put `Closes #N` alone on a line under `## Related`.
6. A maintainer reviews and squash-merges.

## Commits and pull requests

Titles follow Conventional Commits: `type(scope): subject`, in English. Types: `feat`, `fix`, `docs`, `style`, `refactor`, `test`, `chore`, `build`, `ci`.

A change on the frame path reports its time and allocations before and after under `## Measurements`.

## Requirements

- .NET 10 SDK (`global.json`).
- Windows for the GPU tests (WARP) and the merge gate. Linux and macOS run the other test layers.
- `dotnet tool restore` once after cloning (CSharpier).

## Golden images

The pixel tests compare a headless app's frame, drawn on WARP, with a PNG in the test project's `Goldens/` folder (design §12.3).
The goldens are made on the CI's Windows image (`windows-latest`): WARP gives the same pixels for the same version of Windows, and
another version may differ at the edges of text by more than the default tolerance.

- To add or change a golden, run the tests with `XYLEMUI_UPDATE_GOLDENS=1`, look at the PNG it wrote, and commit it.
- When a golden does not match on your machine but matches in CI, your Windows draws it differently: leave the golden as it is.
- When it does not match in CI, the failed job uploads the screenshot and the image of the difference (`golden-failures-<attempt>`);
  if the change is meant, commit that screenshot as the new golden.

## Versioning

Packages are versioned with SemVer from `VersionPrefix` in `Directory.Build.props`. Before 1.0, a minor version may break the public API. Nothing is published to NuGet yet.
