# Native Revision Bank rebuild — unselected work in progress

This branch checkpoints freshly authored source over 515e5fb5596f47332d24892afa67155ef7a192b3. The earlier RAM-only Native Bank source was lost after a runtime reset; its reviews do not apply to this implementation.

Only original guarded Spaces composition is implemented in this initial checkpoint. The native page, view integration and owning tests are still being authored. No compilation, test, installed Windows or product acceptance is claimed. Missing configured actor, receipt authority, guarded settings owner or Space write policy must refuse; Home compatibility and Core reads grant no writes.

C remains the global integrator. This isolated branch is not selected for integration.
