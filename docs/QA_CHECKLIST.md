# QA Checklist

- Register and login return JWT tokens without logging secrets.
- Pairing code expires and cannot be reused.
- Host receives incoming request before any session starts.
- Host reject notifies viewer.
- Host end session disables remote input immediately.
- Clipboard and file transfer remain disabled by default.
- Admin endpoints reject non-admin users.
- SignalR rejects unsupported control messages.
