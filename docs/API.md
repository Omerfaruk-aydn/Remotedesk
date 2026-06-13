# API

All protected endpoints require:

```http
Authorization: Bearer <access-token>
```

Error format:

```json
{
  "error": {
    "code": "string",
    "message": "string",
    "details": {}
  }
}
```

## Auth

- `POST /api/auth/register`
- `POST /api/auth/login`
- `POST /api/auth/refresh`
- `POST /api/auth/logout`
- `POST /api/auth/password-reset/request`
- `POST /api/auth/password-reset/confirm`

## Devices

- `POST /api/devices/register`
- `GET /api/devices`
- `POST /api/devices/{deviceId}/pairing-code`
- `POST /api/devices/pair`
- `POST /api/devices/{id}/revoke`

## Sessions

- `POST /api/sessions/request`
- `POST /api/sessions/{id}/approve`
- `POST /api/sessions/{id}/reject`
- `POST /api/sessions/{id}/start`
- `POST /api/sessions/{id}/end`
- `GET /api/sessions`

## Signaling

SignalR hub: `/hubs/signaling`

Allowed MVP relay messages: `session.request`, `session.approve`, `session.reject`, `session.end`, `webrtc.offer`, `webrtc.answer`, `webrtc.iceCandidate`.
