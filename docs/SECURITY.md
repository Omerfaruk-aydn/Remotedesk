# Security

## Threat Model

Primary risks are unauthorized remote access, pairing-code brute force, token theft, sensitive clipboard/file leakage and support-session abuse.

## Required Controls

- Host approval is mandatory for every remote session.
- Active host banner is mandatory and must not be hideable.
- Remote input is accepted only for an approved active session with `remoteControl=true`.
- File transfer, clipboard and audio default to disabled.
- Pairing codes expire quickly and are stored as hashes.
- Refresh tokens are stored as hashes and should rotate.
- API uses JWT authentication, role-based authorization and structured audit logs.
- Production requires TLS and strong secrets.

## Prohibited Behavior

- Stealth mode.
- User-hidden connection.
- Keylogging or credential harvesting.
- Persistence bypass, firewall bypass or antivirus bypass.
- Backdoor behavior.

## Production Checklist

- Replace all development secrets.
- Enforce HTTPS at the reverse proxy.
- Restrict CORS to approved origins.
- Enable rate limits for login, pairing and session requests.
- Keep audit logging non-optional.
- Review admin role assignment manually.
- Configure Coturn with long-term credentials and TLS.
