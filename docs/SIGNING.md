# Code signing with SignPath Foundation

LiteDPI's release builds can be signed for free by [SignPath Foundation](https://signpath.org/), which runs a code-signing service for open-source projects. The workflow is already set up: signing switches on by itself once the two repo settings in step 4 exist. Until then, releases are published unsigned, exactly as before.

## 1. Before you apply

- **Turn on two-factor login on GitHub** (Settings > Password and authentication). SignPath requires it for everyone on the project, and on your SignPath account too.
- The repo already meets the other conditions:
  - An open-source license: [MIT](../LICENSE).
  - A "Code signing policy" section with the required wording, team roles and privacy statement ([README](../README.md#code-signing-policy)).
  - A published release ([v1.0.0](https://github.com/CARLOSDAJER/LiteDPI/releases/tag/v1.0.0)).
  - Builds made on GitHub-hosted runners from source.

## 2. Apply

Go to <https://signpath.org/apply> and fill in the form:

| Field | Answer |
|-------|--------|
| Project | LiteDPI |
| Repository | https://github.com/CARLOSDAJER/LiteDPI |
| Description | Tiny Windows tray app that sets a Logitech mouse's DPI per game, without G HUB. |
| License | MIT |
| Build system | GitHub Actions (`.github/workflows/build.yml`) |
| What gets signed | `LiteDPI.exe` and `litedpi-probe.exe` (.NET Framework PE files) |

Approval is done by people, so it can take a while.

## 3. Set up the SignPath project

Once you're accepted, you get a SignPath organization. Some of this may already be done for you; check each item.

1. **Project** with slug `LiteDPI`.
2. **Trusted build system:** add the predefined **GitHub.com** one to the organization, and link it to the project.
3. **Artifact configuration:** paste the contents of [`.signpath/artifact-configuration.xml`](../.signpath/artifact-configuration.xml) as the default configuration.
4. **Signing policy** with slug `release-signing`, with yourself as approver.
5. **CI user:** create one with the *Submitter* role on that policy, and copy its API token.
6. Optional: install the SignPath GitHub App on the repo. It's only needed for audit logs.

## 4. Connect GitHub

Run these from the repo folder. Replace the placeholders with your values; `gh` asks for the token when you set the secret.

```bash
gh secret set SIGNPATH_API_TOKEN -R CARLOSDAJER/LiteDPI
```

```bash
gh variable set SIGNPATH_ORGANIZATION_ID -R CARLOSDAJER/LiteDPI --body "<your organization id>"
```

If your project or policy slug differs from `LiteDPI` / `release-signing`, also set `SIGNPATH_PROJECT_SLUG` / `SIGNPATH_POLICY_SLUG` the same way.

## 5. Release a signed version

1. Bump `Version` in `src/AssemblyInfo.cs` and add an entry to `CHANGELOG.md`. Rename `## [Unreleased]` to the new version.
2. Commit, then tag and push:
   ```bash
   git tag v1.0.1
   git push origin main v1.0.1
   ```
3. The workflow stops at **"Sign with SignPath"** and waits for you. Approve the signing request in SignPath within **one hour**.
4. The workflow checks that both exes are validly signed, then publishes the release with the signing note added to the release page.

If the approval window passes, the workflow fails and nothing is published. Re-run the failed job from the Actions tab; it sends a new signing request, so approve that one in time.
