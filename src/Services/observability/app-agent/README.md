# Alloy on the application EC2

This is a separate Compose project from the backend APIs. It collects Docker stdout/stderr from the six APIs and RabbitMQ, then pushes to Loki over the VPC private network. No application container is recreated by this Compose project.

## Configuration

Deploy this directory to `/opt/stealdeal/app-observability` on the application EC2. Create a local `.env` from `.env.example` and replace the IP with the observability EC2 private IPv4. Do not commit `.env`.

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
