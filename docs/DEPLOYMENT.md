# Deployment

## Docker Compose

```powershell
cd SecureRemoteDesk
copy .env.example .env
docker compose up --build
```

## TLS

Production must terminate TLS at Nginx, a cloud load balancer or another reverse proxy. Do not expose JWT-authenticated APIs over plain HTTP.

## PostgreSQL

Use managed PostgreSQL or persistent Docker volumes. Back up the database daily and test restore procedures.

## Redis

Redis is planned for rate limiting, online presence and distributed SignalR scale-out.

## Coturn

Use long-term credentials, restrict open relay behavior, configure TLS certificates and monitor traffic volume.
