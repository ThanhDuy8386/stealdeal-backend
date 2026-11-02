# Observability EC2

This directory is deployed to `/opt/stealdeal/observability` on the dedicated EC2. It is separate from the application Compose project. The first phase runs Grafana OSS, Loki, Prometheus and node_exporter; Alloy and application exporters are added to the application EC2 in P3.

## Before starting

- Docker Engine and the Compose plugin must be installed. Use SSM Session Manager to access the instance.
- The observability EC2 security group must allow TCP 3100 **only from the application EC2 security group**. Do not expose TCP 3000, 3100 or 9090 to the Internet.
- Copy `.env.example` to `.env` and replace `172.31.x.x` with the observability EC2's **private** IPv4. `.env` is git-ignored.
- Create `secrets/grafana_admin_password` locally on the EC2. The directory is git-ignored. Use a strong random password; do not commit or paste it into logs/chat. Ensure the Grafana container can read the file. Grafana applies this initial admin password only when its database is first created; changing the file later does not rotate an existing account password.
- Keep `secrets/` restricted to the EC2 administrator. A root-owned directory with mode `0700` and a password file with mode `0444` works with Compose's file-backed secret mount: unprivileged host users cannot traverse the directory, while Grafana can read the mounted file.

## Validation and start

From `/opt/stealdeal/observability`:

```bash
sudo docker compose config --quiet
sudo docker compose pull
sudo docker compose up -d
sudo docker compose ps
curl --fail http://127.0.0.1:3000/api/health
curl --fail "http://${OBS_PRIVATE_IP}:3100/ready"
```

`OBS_PRIVATE_IP` in the last command must be loaded into the shell separately from Compose's `.env`, or replaced with the actual private IPv4. A successful `curl` proves only local service readiness; Grafana access and Loki ingestion are verified in subsequent steps.

Grafana listens only on the EC2 loopback interface, for SSM port forwarding to port 3000. Prometheus and node_exporter have no host-published ports. Loki binds to the EC2 private IPv4 on port 3100; it has no built-in authentication, so the security-group restriction is essential.

Loki uses 72-hour retention through the compactor. Prometheus retains 3 days or 2 GB of blocks, whichever is reached first. Neither policy is a hard disk quota: WAL, compaction and delayed Loki deletion can temporarily use additional space. Check `df -h /` and Docker volume sizes regularly. All container stdout logs have Docker rotation (`10m` × 3 files).
