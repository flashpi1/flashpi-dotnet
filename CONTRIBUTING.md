# Contributing to Flashpi for .NET

Read [docs/decisions.md](docs/decisions.md) first: it records why the SDK is built the way it is.
Decisions about the product (names, behavior, releases) belong to the Flashpi team: ask before
changing them.

## Setup

The [.NET SDK](https://dotnet.microsoft.com/download) 10.0.100 or a later 10.0 SDK (`global.json`
rolls forward within 10.0; CI's .NET 11 job rewrites it to use .NET 11's SDK). Without a local SDK, the official image works:

```bash
docker run --rm -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet test --project tests/Flashpi.Tests
```

## Checks

Run the same checks as CI before every commit:

```bash
dotnet format Flashpi.slnx --verify-no-changes
dotnet run --project tools/Flashpi.Tools -- openapi check
dotnet run --project tools/Flashpi.Tools -- readme check
dotnet build Flashpi.slnx -c Release
dotnet test --project tests/Flashpi.Tests
```

- The build fails on any warning: nullable reference types, the .NET analyzers (pinned at
  `10.0-recommended`) and the code style of `.editorconfig`. The library builds for net10.0 and
  netstandard2.0, and package validation checks both expose the same API.
- CI measures coverage and fails below 95 % of the lines written by hand:

  ```bash
  dotnet test --project tests/Flashpi.Tests --coverlet --coverlet-output-format cobertura --coverlet-include "[Flashpi]*" --coverlet-exclude-by-attribute GeneratedCode --results-directory coverage
  dotnet run --project tools/Flashpi.Tools -- coverage check coverage 95
  ```

- CI also runs the tests on .NET 11 (`-p:TestTargetFrameworks=net11.0`, with the .NET 11 SDK), on
  .NET 8 (`net8.0`) and on .NET Framework 4.8 on Windows (`net48`); the last two use the
  netstandard2.0 build.
- This packs the package and fails on anything missing or extra in it; CI then installs it in a
  clean project and uses it on net10.0, net8.0 and net48:

  ```bash
  dotnet pack src/Flashpi -c Release -o dist
  dotnet run --project tools/Flashpi.Tools -- package check dist
  dotnet run --project tests/Flashpi.InstalledCheck -f net10.0 -p:FlashpiVersion="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' src/Flashpi/Flashpi.csproj)"
  ```

- `dotnet test --project tests/Flashpi.LiveTests` calls the real API in test mode. It needs
  `FLASHPI_TEST_API_KEY` (an `fp_test_` key, read_write) and `FLASHPI_DEMO_ISSUER_ID` (in CI, an
  organization secret and variable), and issues one test invoice on the demo issuer, which has a
  daily quota: run it only when needed. Without them every test is skipped.

## Code conventions

- English for code, identifiers, comments and commit messages. The READMEs are in Spanish
  (`README.md`) and English (`README.en.md`); keep both in step. Every `csharp` block in them is
  compiled: after changing one, run `dotnet run --project tools/Flashpi.Tools -- readme generate`.
  A block that continues an earlier one takes its names from `tests/Flashpi.Examples/ReadmeContext.cs`.
- The library has **no dependencies** on net10.0, and only System.Text.Json and
  Microsoft.Bcl.AsyncInterfaces on netstandard2.0. What netstandard2.0 lacks (`init`, `required`,
  nullable attributes) is in `src/Flashpi/Internal/Polyfills.cs`.
- Every assembly is strong-named with `Flashpi.snk` (`Directory.Build.props`). The key is public on
  purpose and must never change: a new key breaks every signed app that uses the package. An
  `InternalsVisibleTo` needs `Key="$(FlashpiPublicKey)"`.
- Every operation is declared once, as a `RequestSpec`, and sent by `Internal/Engine.cs`, which
  holds the retries, the idempotency, the recoveries and the errors.
- Resource methods mirror the API: `CreateAsync`, `ListAsync`, `GetAsync`, `UpdateAsync`,
  `DeleteAsync`, then actions named after the operation (`SendEmailAsync`, `RotateSecretAsync`,
  `ReplayAsync`, ...). Path ids come first, then the body or the list's parameters, then
  `RequestOptions? options = null` and the `CancellationToken`. Each resource has a `WithResponse`
  twin with the same methods. `tests/Flashpi.Tests/OperationsTests.cs` checks that every
  operation of the OpenAPI document has exactly one method, which sends it as the contract says.

## The API contract

`openapi/openapi.json` is a copy of the public document at
https://flashpi.com.ec/docs/openapi.json and `src/Flashpi/Generated/Models.g.cs` is generated from
it. Never edit either by hand:

```bash
dotnet run --project tools/Flashpi.Tools -- openapi sync      # download the document and regenerate the models
dotnet run --project tools/Flashpi.Tools -- openapi generate  # regenerate from the committed copy
```

The `OpenAPI sync` workflow does this every day and opens a pull request when the contract
changes. The generator fails on anything it does not know (a new union, a new list operation
without a name for its query, ...): extend it, or its tables at the top of
`tools/Flashpi.Tools/OpenApi/Generator.cs`. New operations need a method. A contract that now
types one of the whole numbers of `IntegerMembers` as `integer` fails it too: remove the entry.

## Commits and releases

Commit with [Conventional Commits](https://www.conventionalcommits.org/) (`feat:`, `fix:`,
`docs:`, `test:`, `ci:`, `chore:`). They drive the version and the changelog:
[release-please](https://github.com/googleapis/release-please) keeps a release pull request open,
and merging it tags the version and publishes it to NuGet from the `Release` workflow, with NuGet
trusted publishing (OIDC, no stored key). release-please writes the version to `version.txt`,
`src/Flashpi/Flashpi.csproj` and `src/Flashpi/Internal/SdkVersion.cs`.

While the version is 0.x, `feat:` raises the minor version and `fix:` the patch version.

The release PR and the OpenAPI sync PR are opened with a token of the organization's GitHub App,
so CI runs on them like on any pull request. (One opened with `GITHUB_TOKEN` would get its CI only
after someone approves it.) The Release workflow also starts the live API test (`live.yml`) on the
release branch each time release-please updates it. A dispatched `ci.yml` run does not count for a
pull request's required checks: to run CI again, re-run the PR's own checks.

### The GitHub App

An app of the `flashpi1` organization, installed on `flashpi-dotnet` (as on the other SDKs), with
no webhook and these repository permissions: Contents, Pull requests and Issues, read and write.
Its Client ID is the organization variable `FLASHPI_BOT_CLIENT_ID` and its private key the
organization secret `FLASHPI_BOT_PRIVATE_KEY`, shared with this repository. The workflows create a
one-hour token per run with only the permissions each job needs.

The release builds in two jobs. The build job checks out the tag, packs it with no cache and checks
it (`package check`), with a read-only token and no secret. The publish job, the only one allowed
to get a NuGet key, checks out nothing and runs no code of this repository or of its dependencies:
it downloads those files, gets a one-hour key with NuGet/login (pinned by commit) and pushes them
with the runner's dotnet CLI. It runs in the `nuget` environment, which waits for a maintainer's
approval in GitHub before it publishes. The build job keeps the files for 30 days, as long as
GitHub waits for that approval. If the publish job fails because they expired anyway, open the run
and re-run the "Build the release" job alone (not "Re-run all jobs", which skips the release once
it exists): the publish job runs again after it and waits for approval.

The release's build job pins the .NET SDK by version (`RELEASE_SDK` in `release.yml`, written into
`global.json` with roll-forward disabled, since the runner image also has newer SDKs), so no new SDK
reaches a published version by itself; Dependabot does not update it. Raise it by hand to a 10.0 SDK
released at least two weeks earlier.

### NuGet trusted publishing

No NuGet API key exists for this project: nuget.org trusts the `Release` workflow through OpenID
Connect. Before the first release PR is merged:

1. An owner creates the Flashpi account on nuget.org (with two-factor authentication) and, under
   **Trusted Publishing**, a policy: repository owner `flashpi1`, repository `flashpi-dotnet`,
   workflow `release.yml`, environment `nuget`. The account's name is the `user` of the
   NuGet/login step in `release.yml` (`flashpi`). Done on 2026-11-14 (policy "flashpi-dotnet
   release", for the package ID `Flashpi`).
2. In this repository's settings, the `nuget` environment has the maintainers as required
   reviewers and is limited to the `main` branch.
3. Drop the not-on-NuGet notice from both READMEs in its own pull request (NuGet shows
   `README.md` as the package's page and keeps it with each version). CI's "Pack and install"
   fails on a release PR that still has it.
4. Merge the release PR. release-please tags `v0.1.0` and the build job packs and checks the tag;
   approve the `nuget` environment, and the publish job pushes the package and its symbols.
5. Once 0.1.0 is on nuget.org, the `flashpi` account asks nuget.org to reserve the `Flashpi` ID
   prefix.

From then on, versions are published by the workflow only: do not create NuGet API keys for the
package.
