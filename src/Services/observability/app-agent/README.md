# Alloy on the application EC2

This is a separate Compose project from the backend APIs. Alloy collects Docker stdout/stderr from the six APIs and RabbitMQ, then pushes to Loki over the VPC private network. node_exporter exposes host CPU, RAM and filesystem metrics for Prometheus to scrape. No application container is recreated by this Compose project.

## Configuration

Deploy this directory to `/opt/stealdeal/app-observability` on the application EC2. Create a local `.env` from `.env.example`: `LOKI_PUSH_URL` uses the observability EC2 private IPv4, while `APP_PRIVATE_IP` uses the application EC2 private IPv4. Do not commit `.env`.

node_exporter uses host network/PID namespaces and a read-only host-root mount to report host metrics, not container metrics. It listens only on `APP_PRIVATE_IP:9100`; the application EC2 security group must allow TCP 9100 **only from the observability EC2 security group**. There is no authentication on this endpoint, so never add a public inbound rule.

cAdvisor reports per-container CPU and memory metrics. It requires privileged access, the Docker runtime directory and read-only host filesystem mounts; deploy only this trusted image and keep its HTTP endpoint private. It publishes `APP_PRIVATE_IP:8080`, and the application EC2 security group must allow TCP 8080 **only from the observability EC2 security group**. `--store_container_labels=false` avoids exporting all Docker labels/environment metadata as Prometheus labels; `--docker_only=true` avoids unrelated raw cgroups. Its container filesystem metrics, if available, are **not** a reliable measurement of each Docker JSON log file; check Docker log file sizes separately when needed.

Alloy uses the Docker socket to discover containers and read their logs. The socket is mounted `:ro`, but this does **not** restrict Docker API operations: treat the Alloy container as highly privileged and do not expose its HTTP server. Its storage volume keeps Docker log read positions across restarts. Run only one Alloy agent with this configuration on the Docker host to avoid duplicate ingestion.

The `service`, `environment`, and `host` Loki labels have bounded values. `OrderId` and `MessageId` stay in the log text for search; they are not labels. This first pass does not drop any log lines. Docker log rotation remains configured separately on the API containers.

## Validation and rollout

From `/opt/stealdeal/app-observability`:

```bash
sudo docker compose config --quiet
sudo docker compose pull alloy
sudo docker compose run --rm --no-deps alloy validate /etc/alloy/config.alloy
sudo docker compose up -d --no-build alloy
sudo docker compose ps
sudo docker compose logs --since 5m alloy
```

Check Grafana Explore (Loki) for `{service="order-api"}` and `{service="rabbitmq"}`. A service that emits no new log lines may not appear until its next event. Roll back by running `sudo docker compose stop alloy`; do not remove the volume if you want to preserve read positions.

For the host-metrics step, first add `APP_PRIVATE_IP` to the existing `.env` on the application EC2. Validate the new Compose configuration, then deploy only the new service:

```bash
sudo docker compose config --quiet
sudo docker compose pull node-exporter
sudo docker compose up -d --no-build --no-deps node-exporter
curl -fsS "http://${APP_PRIVATE_IP}:9100/metrics" | grep '^node_memory_MemAvailable_bytes'
```

Load `APP_PRIVATE_IP` into the shell separately from Compose's `.env`, or replace it with the literal private IPv4 in the curl command. Run `sudo docker compose up` with the service name shown to avoid touching Alloy while validating this step. Prometheus target configuration is added on the observability EC2 only after the exporter responds locally.

For the per-container metrics step, confirm `/dev/kmsg`, `/dev/disk` and the Docker root path on the application EC2 before starting cAdvisor. Then validate Compose and deploy only cAdvisor:

```bash
sudo docker compose config --quiet
sudo docker compose pull cadvisor
sudo docker compose up -d --no-build --no-deps cadvisor
sudo docker compose ps cadvisor
curl -fsS "http://${APP_PRIVATE_IP}:8080/metrics" | grep '^container_memory_working_set_bytes'
```

The Prometheus `application-containers` job is added only after the local endpoint and private network path have been verified. If cAdvisor cannot collect Docker metrics, inspect its logs before changing mount/privilege flags or disabling any metric family.
