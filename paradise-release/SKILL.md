---
name: paradise-release
description: Publish a requested ParadiseEngine release and update the specified consumers after NuGet becomes available.
---

# Release ParadiseEngine

Complete the release and consumer updates requested by the user. Loading this skill does not
authorize merging, pushing, tagging, or updating unrelated consumers.

## Release the selected revision

- Confirm the target repository, requested version, revision, and any PR's current required checks.
  Use explicit PR numbers and SHAs; do not infer them from a branch in a deferred command.
- ParadiseEngine releases are tag-driven: push an annotated `vX.Y.Z` tag at the intended commit.
  `publish-nuget.yml` derives the package version from the tag. Do not change the default
  `<Version>` or create a version-bump PR just to release.
- If merging is included, satisfy the branch policies first. Retarget downstream stacked PRs
  before deleting their base branch. Preserve unrelated work when updating the local checkout.
- A new packable project must appear in the publish workflow's project list. Inspect that list
  when the release adds packages; a green job alone cannot prove an omitted package shipped.

## Establish availability

Find the workflow run for the exact tag and revision, then inspect its conclusion and package
push results. Poll only the required package indexes while indexing is pending. A green publish
can precede NuGet availability by several minutes; do not repeatedly push or move a released tag.
Diagnose a failed or stalled run and report the concrete blocker if it cannot be resolved within
the authorized release. Published NuGet versions cannot be reused.

Prove availability with a fresh package-mode restore using an empty `NUGET_PACKAGES` directory.
`--no-cache` alone still uses the local package cache. Check restore success before using any
`--no-build` test or runtime command.

## Update requested consumers

Update the consumer's shared `ParadiseVersion` pin and relevant CLI installation examples.
Keep engine package pins aligned; `Paradise.Godot.Editor` has a separate addon version line.
Build and run affected checks in package mode (`-p:ParadiseUseEngineSource=false`). If the task
includes consumer publication, continue through its requested commit, push, PR, or CI result.

Report the release tag and revision, publishing result, fresh-restore evidence, and consumer
status. Record memory only under the user's applicable memory instructions.
