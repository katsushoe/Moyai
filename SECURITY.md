# Security

## Supported Versions

Security fixes are provided for the latest published `1.0.x` release.

## Reporting a Vulnerability

Use GitHub's private security advisory reporting for this repository. Do not disclose secrets or exploitable details in a public issue. Receipt and remediation timing depend on severity and reproducibility.

## Security Model

The MCP server binds only to loopback. Repository and lifecycle operations are delegated to configured providers. Service tokens are scoped by audience and scope and can expire, rotate, or be revoked. Release publishing and deployment require explicit target approval.

## Secrets Handling

Do not store service tokens in the repository, documentation, command examples, or logs. Pass secrets through the client or provider's supported secret mechanism. Token issuance and rotation return the plaintext secret once.

## User Responsibilities

Protect the Windows account, database, provider credentials, and MCP client configuration. Restrict file permissions, verify release SHA-256 files, review Provider targets before mutations, and revoke credentials after suspected exposure.
