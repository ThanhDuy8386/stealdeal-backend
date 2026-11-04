# Observability EC2

This directory is deployed to `/opt/stealdeal/observability` on the dedicated EC2. It is separate from the application Compose project. The deployed MVP runs Grafana OSS, Loki, Prometheus and node_exporter here; Alloy, node_exporter and cAdvisor run on the application EC2. See [the observability plan and stop/start runbook](../SELF_HOSTED_OBSERVABILITY_PLAN.md) for current verification status and operating modes.

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

## Application host and container metrics

The `application-node` Prometheus job scrapes node_exporter on the application EC2 private IPv4 (`172.31.28.138:9100`). The exporter is deployed from `app-agent/` on that EC2. The application EC2 security group must allow TCP 9100 only from the observability EC2 security group; do not expose this unauthenticated endpoint publicly. If the application EC2 is replaced with a different private IP, update `prometheus/prometheus.yml` and validate it before recreating only the Prometheus container. When the application EC2 is intentionally stopped, `up=0` is expected, not necessarily an incident.

The `application-containers` job scrapes cAdvisor on the same application EC2 at `172.31.28.138:8080`. cAdvisor requires privileged host access and exposes a web UI/API as well as metrics; allow TCP 8080 only from the observability EC2 security group. Filter dashboard queries to `name!=""` to exclude the root cgroup and identify containers by their `name` label. cAdvisor filesystem metrics do not directly measure Docker JSON log file sizes.

## Grafana dashboard

`grafana/provisioning/dashboards/` provisions the **StealDeal / Infrastructure** dashboard from the repository. It shows scrape availability, both hosts' CPU/RAM/root-disk usage, and application-container CPU/RAM. Container CPU is a percentage of **one CPU core**: 100% means one fully used core, not 100% of the entire instance. The dashboard uses a one-hour range and refreshes every 30 seconds. A blank interval before the exporters were deployed is expected.

The dashboards are read-only in Grafana. Edit their JSON in the repository, deploy the files to the observability EC2, and recreate only the Grafana container to load changes. Do not remove the Grafana data volume; provisioned datasources and the existing admin account remain intact.

**StealDeal / Service Logs** has a Service multi-select (the six APIs and RabbitMQ), a regex text box (default `.*`), a five-minute log-line-count chart, and a log panel. Paste an `OrderId` or `MessageId` into the text box to find matching lines; choose `All` services to follow a flow across services. The count chart ignores the text filter so it always shows total per-service log activity. This is a query over Loki logs, not Loki's own ingestion/storage telemetry. The service list is static and must be updated when a new service is added to Alloy. Internal Loki/Prometheus ingestion and storage metrics are optional future work, not required for this MVP.

## Retention

Loki uses 72-hour retention through the compactor. Prometheus retains 3 days or 2 GB of blocks, whichever is reached first. Neither policy is a hard disk quota: WAL, compaction and delayed Loki deletion can temporarily use additional space. Check `df -h /` and Docker volume sizes regularly. All container stdout logs have Docker rotation (`10m` × 3 files).
