# AWS StealDeal Microservices Deployment Plan

> Stack: Next.js frontend, six ASP.NET Core microservices, SQL Server, Redis, RabbitMQ, Docker Compose and Nginx.
> Goal: manually deploy the complete microservice stack to one EC2 instance for validation. Domain, HTTPS, ECR and CI/CD are later phases.
> Cost rule: use EC2 only when testing, stop it when finished, and keep only the EBS volume and resources that are needed.

## Current Application Topology

Backend services:

- `Cart` - port `5185`
- `Identity` - port `5158`
- `Notification` - port `5053`
- `Order` - port `5165`
- `Payment` - port `5155`
- `Store` - port `5169`
- `database/DatabaseMigrator` - one-shot migration container
- SQL Server 2022, Redis and RabbitMQ

Frontend:

- Repository: `C:\\Users\\ADMIN\\Desktop\\Capstone-FE\\steal-deals-web`
- Framework: Next.js 16, React and TypeScript
- Initial deployment assumption: dynamic Next.js server, not a static export

The existing backend `docker-compose.yml` already builds the six APIs and migrator and runs SQL Server, Redis and RabbitMQ. This plan adds the frontend image and Nginx public entrypoint.

---

## Phase 0 - Cost Safety

- [ ] Pick one AWS Region for the whole lab.
- [ ] Open AWS Billing dashboard.
- [ ] Check Free Tier / credit eligibility.
- [ ] Create AWS Budget.
- [ ] Add billing alert email.
- [ ] Decide max lab spend, for example USD 5-20.
- [ ] Review cleanup checklist before creating resources.

When an EBS-backed EC2 instance is `stopped`, instance usage charges stop but EBS storage charges remain. Public IPv4/Elastic IP and other attached resources may still incur charges. See [AWS stop/start billing](https://docs.aws.amazon.com/cli/latest/reference/ec2/stop-instances.html).

---

## Phase 1 - Prepare Application

Backend:

- [ ] Confirm all six services run through the existing Compose file.
- [ ] Confirm service-to-service URLs use Compose DNS names (`cart-api`, `store-api`, `order-api`, etc.), not `localhost`.
- [ ] Move deploy-specific configuration and secrets to environment variables.
- [ ] Keep `.env` and real credentials out of Git.
- [ ] Add or confirm a health endpoint for each public-facing API.
- [ ] Configure CORS for the frontend origin; for the first test allow the EC2 public IP origin as needed.
- [ ] Confirm EF Core migrations work against SQL Server.
- [ ] Confirm RabbitMQ and Redis retry/reconnect after restarts.

Frontend:

- [ ] Work in the separate frontend repository: `C:\\Users\\ADMIN\\Desktop\\Capstone-FE\\steal-deals-web`.
- [ ] Confirm `npm run build` and `npm run start` work locally.
- [ ] Identify the frontend API base URL environment variable.
- [ ] Add a frontend `Dockerfile` and `.dockerignore` to the frontend repository.
- [ ] Confirm the production container can call APIs through Nginx.

The frontend is currently treated as dynamic because it uses the Next.js production server. S3 is a later option only if the app is intentionally converted to a compatible static export (`output: export`) and does not need server-side Next.js features.

---

## Phase 2 - Dockerize Complete Application (Decoupled FE & BE)

### Architecture Decision: Decoupled Multi-Repo Deployment

Frontend and Backend repositories maintain separate Docker Compose setups to support independent CI/CD pipelines, autonomous deployments, and clear separation of concerns. Communication is achieved via a dedicated shared Docker network (`stealdeal-network`).

```text
[ Internet / Browser ]
          │ (Port 80)
          ▼
    ┌─────────── stealdeal-frontend repo ───────────┐
    │  [ stealdeal-nginx ]                          │
    │    ├── /        ──> [ stealdeal-frontend:3000 ]
    │    └── /api/... ──┐                           │
    └───────────────────┼───────────────────────────┘
                        │
       ═════════════════╪════════════════════════════════
            Docker Network: stealdeal-network (external)
       ═════════════════╪════════════════════════════════
                        │
    ┌───────────────────┼── stealdeal-backend repo ─┐
    │                   ▼                           │
    │  Identity API (5158)   Store API (5169)       │
    │  Cart API (5185)       Order API (5165)       │
    │  Payment API (5155)    Notification API (5053)│
    │  SQL Server (1433)     Redis (6379)           │
    │  RabbitMQ (5672)       DatabaseMigrator       │
    └───────────────────────────────────────────────┘
```

### Shared Docker Network Prerequisite

On any host (Local or EC2), create the external shared network once before starting services:

```bash
docker network create stealdeal-network
```

### Frontend Repository Setup (`steal-deals-web`)

1. **`Dockerfile`**: Lightweight 2-stage build (`builder` -> `runner`) using `output: "standalone"` on `node:20-alpine` (~74MB image).
2. **`nginx.conf`**: Single reverse proxy entrypoint on port 80.
   - Routes web traffic `/` to `http://frontend:3000`.
   - Routes API traffic `/api/...` to appropriate backend containers (`identity-api:5158`, `store-api:5169`, `cart-api:5185`, `order-api:5165`, `payment-api:5155`, `notification-api:5053`).
   - Enables `client_max_body_size 25M` for file/image uploads.
3. **`docker-compose.yml`**:
   - Services: `frontend`, `nginx`.
   - Network: `stealdeal-network` (external).

Run command:
```bash
docker compose up -d --build
```

### Backend Repository Setup (`stealdeal-backend/src/Services`)

1. **`docker-compose.yml`**:
   - Contains: `sqlserver`, `rabbitmq`, `redis`, `database-migrator`, and 6 ASP.NET Core APIs.
   - Network: `stealdeal-network` (external).
   - Keeps all services internal within the private Docker network (or maps ports for local debugging).

Run command:
```bash
docker compose up -d --build
```

### Advantages for CI/CD Flow
- **Independence**: Frontend changes trigger only FE CI/CD and restart FE/Nginx without redeploying backend containers.
- **Microservices Agility**: Backend services can be updated, rebuilt, or migrated without disrupting the frontend container.
- **Zero Cross-Repo Context Dependencies**: Neither repository's build relies on relative paths into the other repository's directory.

Initial routing:

```text
http://<EC2_PUBLIC_IP>/        -> Next.js frontend through Nginx
http://<EC2_PUBLIC_IP>/api/... -> selected microservice through Nginx (via stealdeal-network)
SQL Server/Redis/RabbitMQ      -> Docker network only
```

---

## Phase 3 - AWS Networking With Default VPC

Use the AWS Default VPC for this single-EC2 lab.

- [ ] Open EC2 console.
- [ ] Confirm the selected Region has a Default VPC.
- [ ] Use a default public subnet.
- [ ] Create a dedicated EC2 Security Group.

Inbound rules for the initial IP-only test:

```text
22 -> your IP only
80 -> 0.0.0.0/0
```

Do not expose:

```text
1433, 15672, 5672, 6379
5053, 5155, 5158, 5165, 5169, 5185
3000 and other container ports
```

Port 443 is added later with domain/HTTPS.

---

## Phase 4 - EC2 Setup And Sizing

Recommended starting point:

- [ ] Use `t3.medium` (2 vCPU, 4 GiB RAM) for the first light functional lab.
- [ ] Prefer `t3.large` (2 vCPU, 8 GiB RAM) if all six APIs, Next.js, SQL Server, RabbitMQ and Redis run concurrently and `t3.medium` shows memory pressure/OOM.
- [ ] Compare `t3a.medium`/`t3a.large` regional pricing where available.
- [ ] Do not use `t3.micro` for the complete stack; 1 GiB is impractical with SQL Server and all containers.
- [ ] Use Amazon Linux 2023 or Ubuntu LTS.
- [ ] Use a `40 GiB gp3` root EBS volume as the practical starting point.
- [ ] Treat `30 GiB` as a tight minimum; use `50 GiB` if retaining image versions, build cache, logs, backups or larger SQL data.
- [ ] Monitor `free -h`, `df -h`, Docker disk usage and container memory before downsizing.
- [ ] Optionally configure small swap for lab resilience; swap is not a substitute for RAM.

Image size is not runtime memory. Docker layers, OS space, writable layers, SQL data, logs, frontend layers and rollback images add overhead. This is why 40 GiB is safer than 20 GiB for this stack. AWS documents the T3 sizes as t3.medium = 2 vCPU/4 GiB and t3.large = 2 vCPU/8 GiB: [T3 instances](https://aws.amazon.com/ec2/instance-types/t3/).

- [ ] Attach the Security Group.
- [ ] SSH into EC2.
- [ ] Install Docker and the Docker Compose plugin.
- [ ] Enable Docker on boot.
- [ ] Create `/opt/stealdeal` and check out both repositories as siblings.
- [ ] Verify:

```bash
free -h
df -h
docker --version
docker compose version
```

---

## Phase 5 - Manual Deploy On EC2 (IP Only)

- [ ] Clone both backend and frontend repositories to EC2.
- [ ] Create the shared Docker network:
```bash
docker network create stealdeal-network
```
- [ ] Setup production `.env` for backend and frontend (never commit `.env`).
- [ ] Start Backend stack (microservices, databases, broker):
```bash
cd /opt/stealdeal/stealdeal-backend/src/Services
docker compose up -d --build
# Run migrations
docker compose --profile database up database-migrator
docker compose ps
```
- [ ] Start Frontend & Nginx stack:
```bash
cd /opt/stealdeal/steal-deals-web
docker compose up -d --build
docker compose ps
docker compose logs -f nginx
```
- [ ] Test `http://<EC2_PUBLIC_IP>/` (Frontend loaded).
- [ ] Test each required API flow through Nginx at `http://<EC2_PUBLIC_IP>/api/...`.
- [ ] Check logs and resource usage (`docker stats`, `free -h`).
- [ ] Reboot EC2 and verify Docker starts and all containers recover on `stealdeal-network`.
- [ ] Verify SQL Server data persists across restarts.

---

## Phase 6 - Domain And HTTPS (Later)

Skip this during the first manual validation. The initial URL is `http://<EC2_PUBLIC_IP>/`.

- [ ] Point domain/subdomain to the EC2 public IP.
- [ ] Add inbound port 443.
- [ ] Configure Nginx for the domain.
- [ ] Serve frontend through the domain.
- [ ] Route API traffic to internal microservices.
- [ ] Enable HTTPS with Certbot/Let's Encrypt or an AWS-managed edge service.
- [ ] Verify HTTP redirects to HTTPS.
- [ ] Verify raw container ports remain private.

Later routing:

```text
https://example.com         -> Next.js frontend
https://example.com/api/... -> Nginx -> internal microservice
```

---

## Phase 7 - SQL Server Strategy

Use SQL Server in Docker on the EC2 host through Compose.

- [ ] Use `mcr.microsoft.com/mssql/server:2022-latest` or pin a tested version.
- [ ] Set `ACCEPT_EULA`, `MSSQL_PID` and `MSSQL_SA_PASSWORD` through EC2 `.env`.
- [ ] Store data in persistent volume `sqlserver-data`.
- [ ] Keep port 1433 internal; do not add it to the Security Group.
- [ ] Wait for SQL Server readiness before running the migrator.
- [ ] Run the migrator and verify all service databases/tables.
- [ ] Test CRUD and cross-service flows.
- [ ] Export a backup before destroying or replacing the EC2 volume.
- [ ] Treat SQL Server Developer edition as lab/non-production usage and revisit licensing before production.

This is suitable for the requested manual test. For production, evaluate Amazon RDS for SQL Server or another managed backup/HA strategy instead of relying only on a Docker volume.

---

## Phase 8 - ECR (Decoupled Repositories)

Because frontend and backend have separate lifecycles, maintain dedicated ECR repositories:

- [ ] Create ECR repository for Frontend: `stealdeal-frontend`.
- [ ] Create ECR repositories for Backend services:
  - `stealdeal-identity`
  - `stealdeal-store`
  - `stealdeal-cart`
  - `stealdeal-order`
  - `stealdeal-payment`
  - `stealdeal-notification`
  - `stealdeal-database-migrator`
- [ ] Authenticate Docker to ECR via AWS CLI: `aws ecr get-login-password`.
- [ ] Image Tagging convention: Use Git commit SHA (`${{ github.sha }}`) and `latest`.
- [ ] Add ECR Lifecycle Policy to each repository (retain last 5 images to keep storage cost minimal).

---

## Phase 9 - CI (Independent Workflows Per Repository)

Each repository manages its own automated Continuous Integration workflow without depending on the other.

### 1. Frontend CI (`steal-deals-web/.github/workflows/ci.yml`)
- [ ] Trigger on PR and push to `main` / `develop` in `steal-deals-web`.
- [ ] Setup Node.js 20/22 with cache.
- [ ] Run `npm ci`.
- [ ] Run linter: `npm run lint`.
- [ ] Run tests: `npm run test:run` (Vitest).
- [ ] Test Docker build: `docker build -t stealdeal-frontend:test .`.

### 2. Backend CI (`stealdeal-backend/.github/workflows/ci.yml`)
- [ ] Trigger on PR and push to `main` / `develop` in `stealdeal-backend`.
- [ ] Setup .NET SDK.
- [ ] Run `dotnet restore`, `dotnet build --no-restore`.
- [ ] Run unit & integration tests: `dotnet test --no-build`.
- [ ] Test Docker build for changed service Dockerfiles.

---

## Phase 10 - GitHub Actions To AWS With OIDC

Secure authentication to AWS without long-lived credentials (`AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY`).

- [ ] Create GitHub OIDC Provider in AWS IAM (issuer: `https://token.actions.githubusercontent.com`).
- [ ] Create single deployment IAM Role (e.g. `github-actions-stealdeal-deploy`).
- [ ] Configure trust policy to permit both repositories:
  - `StringLike: { "token.actions.githubusercontent.com:sub": ["repo:TaiPhat25/steal-deals-web:*", "repo:<backend-owner>/stealdeal-backend:*"] }`
- [ ] Grant necessary permissions: ECR image push/pull, AWS Systems Manager (SSM) or EC2 SSH command execution.
- [ ] Test `aws-actions/configure-aws-credentials` action in both repositories.

---

## Phase 11 - CD To EC2 (Independent Autonomous Deployments)

Each repository can be deployed independently to EC2 without touching or disrupting the other stack.

### 1. Frontend CD Workflow (`steal-deals-web/.github/workflows/cd.yml`)
- [ ] Trigger on push/merge to `main`.
- [ ] Assume AWS Role via OIDC.
- [ ] Build & tag frontend Docker image with commit SHA:
  ```bash
  docker build -t <ACCOUNT_ID>.dkr.ecr.<REGION>.amazonaws.com/stealdeal-frontend:${{ github.sha }} .
  docker push <ACCOUNT_ID>.dkr.ecr.<REGION>.amazonaws.com/stealdeal-frontend:${{ github.sha }}
  ```
- [ ] Deploy to EC2 (via SSH or AWS SSM):
  ```bash
  cd /opt/stealdeal/steal-deals-web
  # Update image tag in compose or .env, then pull and restart frontend & nginx
  docker compose pull frontend
  docker compose up -d frontend nginx
  ```
- [ ] Health check: `curl -f http://localhost:80/` on EC2.
- [ ] Zero impact on running backend services.

### 2. Backend CD Workflow (`stealdeal-backend/.github/workflows/cd.yml`)
- [ ] Trigger on push/merge to `main`.
- [ ] Assume AWS Role via OIDC.
- [ ] Build and push updated microservice images to respective ECR repositories.
- [ ] Deploy to EC2:
  ```bash
  cd /opt/stealdeal/stealdeal-backend/src/Services
  docker compose pull
  docker compose up -d
  # Run database migrations
  docker compose --profile database up database-migrator
  ```
- [ ] Health check backend endpoints through internal network.
- [ ] Zero impact on running frontend/Nginx container.

### 3. Rollback Strategy
- [ ] Maintain previous commit SHA in deployment history.
- [ ] Rollback by re-deploying the previous known-good image tag.

---

## Phase 12 - Optional ECS With EC2 Capacity

Evaluate moving to ECS only after the decoupled EC2 + Docker Compose workflow is stable.

- [ ] Create ECS Cluster using EC2 capacity.
- [ ] Separate ECS Task Definitions:
  - Task Definition 1: `stealdeal-frontend` & `stealdeal-nginx`.
  - Task Definition 2: Microservice task definitions (Service Connect or Cloud Map for service discovery).
- [ ] Deploy independent ECS Services per component.
- [ ] Leverage ECS rolling updates and automated rollbacks.

---

## Daily Lab Workflow

Start:

- [ ] Start EC2.
- [ ] Note the new public IP if no Elastic IP is used.
- [ ] Wait for status checks.
- [ ] Check containers.
- [ ] Open `http://<EC2_PUBLIC_IP>/`.

Finish:

- [ ] Export SQL Server backup if needed.
- [ ] Stop EC2.
- [ ] Confirm EC2 state is `stopped`.
- [ ] Check Billing/Cost Explorer.

---

## Cleanup Checklist

EC2:

- [ ] Stop or terminate EC2.
- [ ] Delete unattached EBS volumes.
- [ ] Delete snapshots not needed.
- [ ] Release Elastic IP if created and no longer needed.

Networking:

- [ ] Delete Security Groups.
- [ ] Delete Route 53 hosted zone if created and unused.

Storage:

- [ ] Delete unused ECR images/repositories.
- [ ] Review Cost Explorer.

---

## Definition Of Done For The Current Manual Phase

- [ ] Frontend works through `http://<EC2_PUBLIC_IP>/`.
- [ ] Required API flows work through Nginx.
- [ ] All six services communicate with SQL Server, Redis and/or RabbitMQ as designed.
- [ ] Database data persists after container and EC2 restart.
- [ ] Only 22 and 80 are public during the IP-only test.
- [ ] SSH is restricted to your IP.
- [ ] Frontend and backend images build reproducibly from their two repositories.
- [ ] No database, broker, cache or individual API port is publicly reachable.
- [ ] Domain/HTTPS, ECR, CI/CD and rollback remain later phases.




