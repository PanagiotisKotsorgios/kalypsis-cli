# Contributing to KALYPSIS CLI

[English](CONTRIBUTING.md) · [Ελληνικά](CONTRIBUTING.el.md)

Thank you for your interest. The Windows CLI is in preparation and has not
been implemented. These guidelines do not define an approved CLI architecture,
feature set, delivery date or installation process.

## Before starting

1. Read the [project status](README.md) and existing issues.
2. Open an issue describing the problem or proposal before substantial work.
3. Explain the intended Windows workflow, expected result, alternatives and
   any accessibility, privacy or security implications.
4. Wait for maintainer agreement on scope before implementing a new feature,
   adding dependencies or proposing changes to external KALYPSIS services.

## Reporting a problem or suggesting an improvement

Use a clear title and explain the context, expected behaviour and actual
behaviour. Include reproducible steps and relevant Windows/version information
when applicable. Clearly distinguish an idea from an existing, working feature.
Use redacted screenshots and synthetic data only.

## Pull requests

- Fork the repository and use a focused branch for one agreed change.
- Explain what changed, why, how it was checked and which issue it addresses.
- Keep unrelated changes, generated binaries and large files out of the PR.
- Keep the English and Greek documentation equivalent. Update both versions
  together and verify their relative links.
- Do not introduce a CLI executable, installer, API contract or automated
  production operation as part of a documentation-only change.
- Accept review feedback constructively. Merging and releases are maintained
  by the repository owner; submitting a PR does not guarantee acceptance.

There are currently no project-specific build, test or install commands.
For documentation changes, check Markdown formatting, links, both languages,
technical accuracy and consistency with the project's unimplemented status.

## Privacy and responsible security reporting

Never commit credentials, API keys, personal information, insurance records,
database exports or production configuration. Do not test against production
or third-party systems without explicit authorisation. Do not publish exploitable
security details in a public issue; contact KALYPSIS privately through
[the contact page](https://mykalypsis.gr/contact), without including secrets.

## Conduct and rights

Be respectful, specific and inclusive. Discuss ideas rather than individuals.
Submit only material you have the right to contribute, and disclose relevant
third-party origins and licensing requirements. These guidelines do not grant
rights to KALYPSIS trademarks, private code or production data, and do not
establish a new software licence.
