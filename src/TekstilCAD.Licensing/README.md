# Offline licensing scope

The verifier accepts RSA SHA-256 signatures and bounded subscription claims. The trusted public key must be provisioned independently; the application must never trust a key contained in a license token. Private signing keys belong in a separate issuer service and are not distributed here. Tests generate ephemeral keys only.

This module is not a subscription service or payment integration. It does not activate, revoke, synchronize accounts, enforce seat counts, or secure the local clock. A caller-supplied clock can be rolled back; trustworthy expiry enforcement requires a signed trusted-time checkpoint and rollback-resistant storage or periodic online verification. Device identity must be supplied by the host and can be spoofed without a stronger device-bound trust design. Offline clients cannot learn revocations without an updated signed revocation list or connectivity.

The desktop development build remains independent of this verifier and has no paid feature gating. No real licenses or private keys are shipped.
