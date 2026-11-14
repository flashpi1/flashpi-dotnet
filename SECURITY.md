# Security

Please do not report security problems in public issues.

Report them privately from this repository's **Security** tab with **Report a vulnerability**
(GitHub private vulnerability reporting). Only the Flashpi maintainers see the report. Include
the version of the `Flashpi` package, your .NET version and the steps to reproduce.

For problems with the Flashpi API itself, use the same channel: we route them to the API team.

## Keeping your integration safe

- Your API key (`fp_live_...`) is a secret: keep it on your server, in an environment variable
  such as `FLASHPI_API_KEY` or a secret store, never in code that runs on your users' devices.
  The SDK never shows it in `ToString()` or in its messages.
- Verify every webhook with `Webhooks.Unwrap()` on the raw body before you act on it, and
  deduplicate on its `Id`.
