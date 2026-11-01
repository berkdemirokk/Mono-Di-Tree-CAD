# Offline licensing scope

The legacy `v1` verifier accepts RSA SHA-256 signatures and bounded time-limited claims. The separate `v2` verifier accepts a signed record containing an ID, customer, fixed product identifier and UTC issue-date metadata; it does not contain an expiry or check the current clock. A future signed issue date is accepted because the offline clock is not treated as authoritative. The record does not define update, support, transfer or seat rights. Token versions are signed and cannot be substituted for each other.

For both versions the trusted public key must be provisioned independently; the application must never trust a key contained in a license token. Private signing keys belong in a separately secured issuance environment and are not distributed here. Tests generate ephemeral keys only. Neither version defines a complete sales, licensing or customer agreement.

This module is not a subscription service or payment integration. It does not activate, revoke, synchronize accounts, enforce seat counts, or secure the local clock. A caller-supplied clock for `v1` can be rolled back; trustworthy expiry enforcement requires a signed trusted-time checkpoint and rollback-resistant storage or periodic online verification. The optional `v1` device identity is host-supplied and can be spoofed; `v2` is not device-bound and can be copied. Offline clients cannot learn revocations without an updated signed revocation list or connectivity.

The desktop development build remains independent of this verifier and has no paid feature gating. No real licenses or private keys are shipped.
