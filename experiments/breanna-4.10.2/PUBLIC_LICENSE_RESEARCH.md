# Public-source license provenance research — 2026-11-02

This is research evidence, not an attribution bundle, a commercial-use clearance, or legal advice. No paid account or service was used. Source links and registry data are public. Production and plotter integration remain closed.

## FreeSewing 4.10.2

The npm registry's version-specific metadata for all 14 locked FreeSewing packages says `license: MIT`, repository `codeberg:freesewing/freesewing`, and `gitHead: 3d290d25d0b6a65852dd31aec125c97bc8ca6e2c`. See the captured [registry fields](npm-provenance-4102.json), for example the [official Breanna package metadata](https://registry.npmjs.org/%40freesewing%2Fbreanna/4.10.2). Every reported registry SHA-512 matches this experiment's lockfile, and all 14 downloaded npm tarballs matched those registry SHA-512 values at the time of the check; see [tarball results](npm-tarball-verification.json). This verifies downloaded archive bytes against registry/lock data; it does not verify npm signatures, every installed file, or the claimed source commit against the tarball.

The commit-specific upstream candidate is [Codeberg LICENSE at the registry-declared commit](https://codeberg.org/freesewing/freesewing/src/commit/3d290d25d0b6a65852dd31aec125c97bc8ca6e2c/LICENSE). Both the source page and raw/API routes were inaccessible or timed out during this research, so its content, copyright attribution and existence at that exact commit **remain unverified**. The project's [archived GitHub repository](https://github.com/freesewing/freesewing) says the project moved to Codeberg; its older license cannot establish the newer commit's text. Do not substitute a generic MIT template or an older project license.

## hooks 0.3.2

The [npm package](https://www.npmjs.com/package/hooks/v/0.3.2) and local package metadata identify the publisher repository and say MIT in the legacy `licenses` field; the package README likewise says MIT. The [publisher's v0.3.2 tag](https://github.com/bnoguchi/hooks-js/tree/v0.3.2) resolves through the GitHub API to commit `77f3b390e0cd21889e01c4c743c827fc3dca2be5`. At that commit, the publisher's `package.json`, `README.md`, and `hooks.js` have SHA-256 values equal to the three corresponding installed files; see [comparison](hooks-source-comparison.json). The downloaded npm 0.3.2 tarball matched both registry and lockfile SHA-512 `TqeFzUf12rSzcbm5lUls81jimUC8TmXZ4ANPxxeeMou09hrjBcHYhAQ0WgyN5YqNCXOzz7L6xVNl/+ctFuSeOw==`. Registry metadata has no `gitHead`; three matching files do not prove the whole archive was built from that commit.

The [publisher's complete tag tree](https://api.github.com/repos/bnoguchi/hooks-js/git/trees/77f3b390e0cd21889e01c4c743c827fc3dca2be5?recursive=1) returned `truncated:false` and no file named LICENSE, LICENCE, COPYING or NOTICE. The [tagged README](https://github.com/bnoguchi/hooks-js/blob/77f3b390e0cd21889e01c4c743c827fc3dca2be5/README.md) says MIT but does not provide the full license text or a verified copyright line. Do not fabricate a notice; the exact full attribution remains open.

## Node.js 24.22.1

The [official Node.js v24.22.1 LICENSE](https://github.com/nodejs/node/blob/v24.22.1/LICENSE) contains the project's license and extensive notices for bundled components. A research-only byte copy is retained as [NODE-v24.22.1-LICENSE.txt](license-research/NODE-v24.22.1-LICENSE.txt), SHA-256 `5888dbb9a1d2b18f2c3e6c5f6af1b39de658372b402a0577b002777f14c62ace`. The official GitHub tag is annotated tag object `d01285110044365f0ad4fec24482a585646640e2`, pointing to commit `ce7aaf1cee78b8cf0ce17364260535c418d3ed26`; see [tag reference](https://api.github.com/repos/nodejs/node/git/ref/tags/v24.22.1) and [tag object](https://api.github.com/repos/nodejs/node/git/tags/d01285110044365f0ad4fec24482a585646640e2). Tag signatures were not verified.

The local executable reports v24.22.1, but its SHA-256 is `b59feb2088f80e1fa431c8f9c316861e660e5936f968fd97b802e550fed356c5`; the [official checksums](https://nodejs.org/dist/v24.22.1/SHASUMS256.txt) list `d702daa815eabbff763c7d3808e235cf031cf2ca70211b56847bc72f51e240e5` for `win-x64/node.exe`. These are **different**. The reason and provenance of the local executable are unknown. The checksum list was fetched over HTTPS; its detached signature was not verified. Accordingly, the official version-tag license is useful reference material but is not established as the complete notice for this exact binary. Determine the binary's actual origin and obtain its matching notices before any distribution.

## Remaining work

- Obtain the exact FreeSewing commit's actual license and its source/notice provenance from the publisher; resolve `hooks` complete notice with its publisher or another authoritative version-specific source.
- Establish the precise local Node binary provenance and all applicable component notices; do not equate a version string with binary identity.
- Verify actual redistribution scope, attribution wording, integrity/signatures and compatibility with qualified review. The preview embeds third-party glyph definitions and has not been cleared for redistribution.
