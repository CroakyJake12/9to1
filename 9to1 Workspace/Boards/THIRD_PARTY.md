# Third-party provenance: AppFlowy Board

Haven Boards currently references the standalone AppFlowy Board Flutter package for a bounded proof-of-concept.

## Upstream

- Project: AppFlowy Board
- Repository: `https://github.com/AppFlowy-IO/appflowy-board`
- Exact commit: `804d7898ac0becabf73e45527baf5d5c573cd6bb`
- Upstream package version at the inspected branch: `0.1.2`
- Upstream licence grant: dual licensed under GNU AGPL v3 and Mozilla Public License 2.0.
- Haven/CakeOS selected licence path for this dependency: **MPL 2.0**.

## Boundary rules

1. Do not paste AppFlowy source into Haven-owned files.
2. Keep any future modifications to AppFlowy-derived/MPL-covered files clearly separated and preserve upstream notices.
3. If CakeOS distributes modified MPL-covered source, make the covered source available in accordance with MPL 2.0.
4. Keep the exact Git commit pinned. Upgrades require a fresh source, dependency, licence and behaviour review.
5. The AppFlowy package must not become the canonical Haven persistence schema. Persist only the neutral Haven board snapshot.
6. The AppFlowy package must not own HUI navigation, theming, permissions, sync, attachments or generative actions.

## Why this component only

The full AppFlowy application and AppFlowy-Collab layer are AGPLv3 and bring substantially broader application/database/collaboration semantics. This slice intentionally reuses only the standalone board widget/controller package.

This file records engineering provenance and is not legal advice.
