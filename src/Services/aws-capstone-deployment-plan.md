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

## Daily Cost-Effective EC2 Workflow

Use this workflow for normal development and testing. It keeps the EC2 compute cost limited to working hours while preserving the EBS volume, Docker images and SQL Server data.

### Start

1. Start the EC2 instance from the EC2 console.
2. Wait until both instance status checks pass.
3. SSH using the Elastic IP.
4. Start the backend without rebuilding:

```bash
cd /opt/stealdeal/stealdeal-backend/src/Services
docker compose up -d
docker compose ps
```

5. Start the frontend and Nginx without rebuilding:

```bash
cd /opt/stealdeal/steal-deals-web
docker compose up -d
docker compose ps
```

6. Open `http://<ELASTIC_IP>/` and check the required API flows.

Do not use `--build` during the normal start workflow. Rebuild only after source or Dockerfile changes.

### Finish

1. Export or upload a database backup if the session produced data that must be retained.
2. Remove application containers and their accumulated Docker logs while preserving volumes:

```bash
cd /opt/stealdeal/steal-deals-web
docker compose down

cd /opt/stealdeal/stealdeal-backend/src/Services
docker compose down
```

`docker compose down` does not remove the external `stealdeal-network`, named SQL Server volumes, repository files or the EBS volume.

3. In the EC2 console, choose `Instance state -> Stop instance`.
4. Confirm the instance state is `stopped`.

Never use `docker compose down -v` unless database destruction is explicitly intended. Never choose `Terminate instance` for the lab instance.

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
   - Direct service-prefix routing:
     - `/api/identity/...` -> `http://identity-api:5158`
     - `/api/store/...` -> `http://store-api:5169`
     - `/api/cart/...` -> `http://cart-api:5185`
     - `/api/order/...` -> `http://order-api:5165`
     - `/api/payment/...` -> `http://payment-api:5155`
     - `/api/notification/...` -> `http://notification-api:5053`
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

- [x] Use `m7i-flex.large` (2 vCPU, 8 GiB RAM) for the first functional lab. This instance type is selected because it is eligible for the current Free Plan account configuration and provides enough memory for the complete Docker Compose stack.
- [ ] Consider a larger instance only if all six APIs, Next.js, SQL Server, RabbitMQ and Redis show sustained memory or CPU pressure.
- [ ] Compare `t3a.medium`/`t3a.large` regional pricing where available.
- [ ] Do not use `t3.micro` for the complete stack; 1 GiB is impractical with SQL Server and all containers.
- [x] Use Ubuntu 24.04 LTS 64-bit (`x86_64`).
- [x] Use a `50 GiB gp3` root EBS volume as the practical starting point.
- [ ] Treat `30 GiB` as a tight minimum; use `50 GiB` if retaining image versions, build cache, logs, backups or larger SQL data.
- [ ] Monitor `free -h`, `df -h`, Docker disk usage and container memory before downsizing.
- [ ] Optionally configure small swap for lab resilience; swap is not a substitute for RAM.

Image size is not runtime memory. Docker layers, OS space, writable layers, SQL data, logs, frontend layers and rollback images add overhead. This is why 40 GiB is safer than 20 GiB for this stack. AWS documents the T3 sizes as t3.medium = 2 vCPU/4 GiB and t3.large = 2 vCPU/8 GiB: [T3 instances](https://aws.amazon.com/ec2/instance-types/t3/).

- [x] Attach the dedicated Security Group.
- [x] Allocate and associate one Elastic IP with the EC2 instance.
- [x] SSH into EC2.
- [x] Install Docker and the Docker Compose plugin.
- [x] Enable Docker on boot.
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

- [x] Clone both backend and frontend repositories to EC2.
- [x] Create the shared Docker network:
```bash
docker network create stealdeal-network
```
- [x] Setup deployment `.env` for backend and frontend (never commit `.env`).
- [x] Build and start the Backend stack (microservices, databases, broker):
```bash
cd /opt/stealdeal/stealdeal-backend/src/Services
docker compose up -d --build
# Run migrations
docker compose --profile database up database-migrator
docker compose ps
```
- [ ] Restore development SQL Server data from uploaded `.bak` files when required. The initial schema migration is complete; backup restore remains an optional follow-up and the `.bak` files must remain outside Git.
- [x] Start Frontend & Nginx stack:
```bash
cd /opt/stealdeal/steal-deals-web
docker compose up -d --build
docker compose ps
docker compose logs -f nginx
```
- [x] Test `http://<EC2_PUBLIC_IP>/` (Frontend loaded).
- [x] Test required API flows through Nginx at `http://<EC2_PUBLIC_IP>/api/...`.
- [x] Check logs and resource usage (`docker stats`, `free -h`, `df -h`).
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
- [x] Added workflow at repository-root path `.github/workflows/ci.yml`; currently triggers on push to validation branch `duy-dev`.
- [x] Setup Node.js 20 with cache.
- [x] Run `npm ci`.
- [x] Run linter: `npm run lint`.
- [ ] Run tests: `npm run test:run` (Vitest) — temporarily skipped because `jsdom@30`/`undici@8` requires a newer Node runtime than the current Node 20 CI setup; revisit with Node 24.
- [x] Run `npm run build` and test the frontend Docker build; CI images are not pushed to ECR in Phase 9.

### 2. Backend CI (`stealdeal-backend/.github/workflows/ci.yml`)
- [x] Added workflow at repository-root path `.github/workflows/ci.yml`; service source remains under `src/Services`.
- [x] Currently triggers on push to validation branch `duy-dev`.
- [x] Setup .NET SDK 10.x.
- [x] Run restore and Release build for all six service solutions.
- [x] Run `dotnet test --no-build` for all six solutions; dedicated test projects are not present yet.
- [x] Build Docker images for all six APIs and the database migrator as CI validation; images are not pushed to ECR in Phase 9.

### Phase 9 status

- [x] Backend and frontend CI workflows are implemented and validated on `duy-dev`.
- [x] CI validates source restore/build, frontend lint/build, and Docker image builds on GitHub-hosted runners.
- [ ] Frontend Vitest execution and substantive backend test projects remain follow-up work.
- [ ] ECR push and EC2 deployment remain Phase 10/11 work; Phase 9 does not publish images.

---

## Phase 10 - GitHub Actions To AWS With IAM User Access Keys

Phase 10 is complete for the current lab. It establishes authentication from GitHub Actions to AWS and a secure command channel from GitHub Actions to EC2. It does not build/push production images or deploy the application; those actions belong to Phase 11.

### AWS Lab Context

```text
AWS Account ID:   735394850167
AWS Region:       us-east-1
EC2 Instance ID:  i-012543d83b62f9bff
```

The AWS region/account values in this section apply only to this deployment plan. Application `appsettings.json` files belonging to another AWS account remain unchanged.

### A. GitHub Actions IAM User

Completed configuration:

- [x] Created dedicated IAM user: `github-actions-stealdeal-lab`.
- [x] User is for GitHub Actions programmatic authentication only; no Console login is required.
- [x] Created an IAM access key and stored it in GitHub encrypted secrets.
- [x] Did not use a personal or root access key.
- [x] Did not attach `AdministratorAccess`, `PowerUserAccess`, or unrestricted EC2 permissions.

The access key is used only by GitHub Actions through `aws-actions/configure-aws-credentials`. It must never be committed to either repository or printed in workflow logs.

### B. IAM Policy For GitHub Actions

The IAM user has a restricted policy for:

- [x] ECR authentication: `ecr:GetAuthorizationToken`.
- [x] ECR image push/pull operations.
- [x] ECR repository access limited to:
  - `stealdeal-frontend`
  - `stealdeal-identity`
  - `stealdeal-store`
  - `stealdeal-cart`
  - `stealdeal-order`
  - `stealdeal-payment`
  - `stealdeal-notification`
  - `stealdeal-database-migrator`
- [x] SSM `SendCommand` limited to EC2 instance `i-012543d83b62f9bff`.
- [x] SSM command status and managed-instance read operations.
- [x] Unrelated IAM/EC2 administration permissions remain unavailable.

`ec2:DescribeInstances` is intentionally not required for the authentication test. SSM status should be checked with `ssm:DescribeInstanceInformation` instead, preserving least privilege.

### C. GitHub Repository Secrets

The following secrets are configured in both repositories under `Settings -> Secrets and variables -> Actions`:

- [x] `AWS_ACCESS_KEY_ID`
- [x] `AWS_SECRET_ACCESS_KEY`
- [x] `AWS_ACCOUNT_ID` = `735394850167`
- [x] `AWS_REGION` = `us-east-1`
- [x] `AWS_EC2_INSTANCE_ID` = `i-012543d83b62f9bff`

`AWS_ACCESS_KEY_ID` and `AWS_SECRET_ACCESS_KEY` are sensitive. The account ID, region and instance ID are configuration values, but keeping them in the same GitHub secret set is acceptable for this lab.

### D. EC2 IAM Role And Instance Profile For SSM

The EC2 instance has a separate IAM role/instance profile. This identity is different from the GitHub Actions IAM user.

```text
GitHub Actions IAM user:
github-actions-stealdeal-lab

EC2 IAM role/instance profile:
stealdeal-ec2-ssm-role
stealdeal-ec2-ssm-profile
```

Completed configuration:

- [x] Created an EC2-trusted IAM role.
- [x] Attached AWS managed policy `AmazonSSMManagedInstanceCore`.
- [x] Installed AWS CLI on the EC2 host so SSM commands can authenticate Docker to ECR.
- [x] Added least-privilege ECR pull permissions to `stealdeal-ec2-ssm-role`:
  - `ecr:GetAuthorizationToken` on `*`.
  - `ecr:BatchCheckLayerAvailability`, `ecr:BatchGetImage` and `ecr:GetDownloadUrlForLayer` on required ECR repositories.
- [x] Created/used an EC2 instance profile containing that role.
- [x] Attached the instance profile to `i-012543d83b62f9bff`.

The ECR pull policy added to the EC2 role is equivalent to:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "AuthenticateToEcr",
      "Effect": "Allow",
      "Action": "ecr:GetAuthorizationToken",
      "Resource": "*"
    },
    {
      "Sid": "PullFrontendImage",
      "Effect": "Allow",
      "Action": [
        "ecr:BatchCheckLayerAvailability",
        "ecr:BatchGetImage",
        "ecr:GetDownloadUrlForLayer"
      ],
      "Resource": "arn:aws:ecr:us-east-1:735394850167:repository/stealdeal-frontend"
    }
  ]
}
```

Initially the EC2 role had only `AmazonSSMManagedInstanceCore`. The first frontend CD attempt reached EC2 but failed at `aws ecr get-login-password` with `AccessDeniedException`; adding the ECR pull policy fixed the deployment. When implementing Backend CD, add the same read actions for each backend ECR repository, including `stealdeal-database-migrator` if the migrator image is pulled on EC2.

The EC2 role allows the EC2-side SSM Agent to communicate with Systems Manager and allows Docker to pull private ECR images. It does not grant GitHub Actions access; GitHub Actions uses its own IAM user.

The ECR login is performed on EC2 for every deployment because GitHub's AWS credentials configure the GitHub runner, not the Docker daemon on EC2:

```bash
aws ecr get-login-password --region us-east-1 |
  docker login --username AWS --password-stdin 735394850167.dkr.ecr.us-east-1.amazonaws.com
```

### E. SSM Agent On EC2

Completed configuration:

- [x] Installed SSM Agent on Ubuntu using Snap.
- [x] Enabled and started service:
  `snap.amazon-ssm-agent.amazon-ssm-agent.service`.
- [x] Confirmed service state is `active (running)`.
- [x] Confirmed the instance can register with Systems Manager.

The service name `amazon-ssm-agent.service` is not expected for the Snap installation. The correct service name is:

```bash
sudo systemctl status snap.amazon-ssm-agent.amazon-ssm-agent.service
```

### F. Purpose Of AWS Systems Manager

SSM is the command bridge used by Phase 11:

```text
GitHub Actions
  -> IAM user access key
  -> AWS Systems Manager
  -> SSM Agent on EC2
  -> docker pull / compose / migration / health check
```

SSM replaces direct SSH from GitHub Actions. It is not used to build Docker images, store Docker images, or rotate IAM access keys.

### G. `aws-auth-test.yml`

The authentication test workflow validates Phase 10 configuration:

- [x] GitHub Actions can authenticate to AWS.
- [x] The workflow is operating in account `735394850167` and region `us-east-1`.
- [x] Frontend/backend ECR access can be tested.
- [x] EC2 SSM registration can be checked with `ssm:DescribeInstanceInformation`.
- [x] SSM `SendCommand` can be used to test a command such as `docker --version`.
- [x] Access outside the intended IAM policy is denied.

The workflow is a validation workflow. It does not yet perform the full application deployment.

### H. Access Key Rotation Scope

Access key rotation is intentionally excluded from the current lab scope.

- [x] No SSM-based key rotation was configured.
- [x] No Lambda, EventBridge, Secrets Manager or GitHub Secrets rotation automation was added.
- [ ] Manual rotation can be added later if the lab is kept for an extended period.

SSM does not rotate IAM access keys. If rotation is needed later, create a replacement key, update GitHub Secrets, test both workflows, deactivate the old key and delete it.

### Phase 10 Handoff To Phase 11

Phase 11 can now use:

- [x] AWS account `735394850167`.
- [x] Region `us-east-1`.
- [x] ECR registry `735394850167.dkr.ecr.us-east-1.amazonaws.com`.
- [x] EC2 instance `i-012543d83b62f9bff`.
- [x] IAM user credentials from GitHub Secrets.
- [x] SSM command execution instead of GitHub-to-EC2 SSH.

Phase 11 still needs to implement:

- [x] Frontend CI builds and pushes the frontend image tagged with commit SHA.
- [x] Update the frontend image tag on EC2 through SSM.
- [x] Deploy frontend/Nginx independently.
- [ ] Deploy backend services independently.
- [ ] Run Database Migrator through the intended deployment flow.
- [x] Execute the frontend health check.
- [ ] Implement rollback to a previous image SHA.

The IAM user/access-key approach is a lab simplification. Revisit OIDC before production because it removes long-lived AWS credentials from GitHub entirely.

---

## Phase 11 - CD To EC2 (Independent Autonomous Deployments)

Each repository can be deployed independently to EC2 without touching or disrupting the other stack.

### 1. Frontend CD Workflow (`steal-deals-web/.github/workflows/cd.yml`)
- [x] Frontend CI is triggered by a push to `duy-dev`.
- [x] Frontend CI runs lint/Next.js build, then builds and pushes the Docker image to ECR with the commit SHA tag.
- [x] Frontend CD is stored on the repository default branch `main` and listens for a successful `workflow_run` of `Frontend CI` on `duy-dev`.
- [x] Keeping `cd.yml` on `main` is required for the `workflow_run` event. The application code does not need to be merged to `main` for this test flow.
- [x] Configure AWS credentials using the dedicated IAM user access key stored in GitHub Secrets.
- [x] Build and tag the frontend Docker image on the GitHub-hosted runner, not on EC2:
  ```bash
  docker build -t <ACCOUNT_ID>.dkr.ecr.us-east-1.amazonaws.com/stealdeal-frontend:${{ github.sha }} .
  docker push <ACCOUNT_ID>.dkr.ecr.us-east-1.amazonaws.com/stealdeal-frontend:${{ github.sha }}
  ```
- [x] Deploy to EC2 through AWS SSM. The CD workflow does not build Docker images and does not require frontend source code on EC2.
- [x] The frontend Compose file keeps both local-build and ECR-image support:
  ```yaml
  frontend:
    build:
      context: .
      dockerfile: Dockerfile
    image: ${FRONTEND_IMAGE:-stealdeal-frontend:local}
  ```
- [x] The SSM deployment sets `FRONTEND_IMAGE` only for the remote command session; a deployment `.env` file is not required:
  ```bash
  cd /opt/stealdeal/steal-deals-web
  export FRONTEND_IMAGE=<ECR_IMAGE_URI>:<COMMIT_SHA>
  aws ecr get-login-password --region us-east-1 | \
    docker login --username AWS --password-stdin <ECR_REGISTRY>
  docker compose pull frontend
  docker compose up -d --force-recreate frontend nginx
  ```
- [x] SSM parameters are passed as a JSON object through `jq`; AWS CLI shorthand is avoided because commas, equals signs and spaces in shell commands can cause parsing errors.
- [x] The workflow prints `docker compose config --images` and `docker compose images frontend` to verify the resolved and running image tag.
- [x] SSM output is printed even when the remote command fails, so ECR, IAM, Compose and health-check errors are visible in Actions logs.
- [x] Health check: `curl --fail --silent --show-error --retry 12 --retry-delay 5 http://localhost:80/` on EC2.
- [x] Zero impact on running backend services.

Frontend CD operational prerequisites:

- [x] The EC2 host has AWS CLI installed.
- [x] The EC2 instance profile has ECR pull permissions in addition to `AmazonSSMManagedInstanceCore`.
- [x] The EC2 host has the updated frontend `docker-compose.yml` containing `image: ${FRONTEND_IMAGE:-stealdeal-frontend:local}`.
- [x] The shared external Docker network `stealdeal-network` exists.
- [x] The backend Compose stack can remain running; frontend CD only targets the `frontend` and `nginx` services in the frontend Compose project.
- [ ] If EC2 is stopped, CI can still push the image to ECR, but CD/SSM cannot deploy until EC2 and SSM Agent are online. Re-run CD after starting EC2.

Important distinction:

```text
CI build/push:
  GitHub runner -> Dockerfile -> ECR image

CD deploy:
  GitHub runner -> SSM -> EC2 -> ECR login -> docker pull -> Compose recreate
```

EC2 does not need the frontend source code or the GitHub workflow files for CD. It does need the Compose and Nginx deployment files. Changes inside the Docker image deploy automatically; changes to `docker-compose.yml` or `nginx.conf` do not, unless those files are separately synchronized to EC2.

### 2. Backend CD Workflow (`stealdeal-backend/.github/workflows/cd.yml`)
- [x] Backend CI detects changed service paths on `duy-dev`; Backend CD listens for a successful `workflow_run`. The CD workflow file must exist on the default branch `main`, matching the frontend workflow pattern.
- [x] Configure AWS credentials using the dedicated IAM user access key stored in GitHub Secrets.
- [x] Build and push only changed microservice images to their respective ECR repositories using the commit SHA tag.
- [x] Support a manual `full_build` CI dispatch for the initial ECR bootstrap, publishing all six API images and the database migrator.
- [x] Build and run `database-migrator` only when the migrator or related service infrastructure/migration paths change.
- [x] Deploy only changed API containers to EC2 through SSM:
  ```bash
  cd /opt/stealdeal/stealdeal-backend/src/Services
  docker compose pull <changed-api-service>
  docker compose up -d --no-deps --force-recreate <changed-api-service>
  # The migrator runs first only when a migration-related path changed.
  ```
- [ ] Add dedicated health endpoints for all six APIs and use them in CD. Current CD verifies changed containers are running and checks the frontend through Nginx.
- [x] Unchanged API, database, broker, cache and frontend containers are not recreated. Nginx is restarted after an API deployment because the current Nginx configuration caches Docker service IPs.
- [x] Persist deployed ECR image URIs in the EC2 backend `.env` so restarts and normal `docker compose up -d` use ECR images instead of the local-image fallback.

### 3. Rollback Strategy
- [x] Keep immutable commit SHA image tags in ECR and GitHub Actions run history.
- [x] Support manual rollback through Backend CD `workflow_dispatch` by selecting a previous image SHA and the affected services.
- [ ] Database schema rollback is intentionally not automated; EF Core migrations are forward-only and database backups remain the recovery mechanism.

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

---

## Future Improvement - Dynamic Docker DNS For Nginx

The current manual deployment flow intentionally keeps the existing Nginx configuration and requires a manual Nginx restart after backend containers are recreated:

```bash
cd /opt/stealdeal/steal-deals-web
docker compose restart nginx
```

This is required because Docker may assign new internal IP addresses when backend containers are recreated, while the current Nginx upstream configuration can retain the previous resolved address.

After the initial manual validation is stable, update `nginx.conf` to use Docker's embedded DNS resolver and periodically re-resolve backend service names:

```nginx
resolver 127.0.0.11 valid=10s ipv6=off;
```

Use variable-based `proxy_pass` targets for `frontend`, `identity-api`, `store-api`, `cart-api`, `order-api`, `payment-api` and `notification-api`. Then backend CI/CD can recreate and restart backend containers without manually restarting the frontend/Nginx stack.

Before adopting this improvement:

- [ ] Add Docker DNS resolver configuration to the frontend Nginx config.
- [ ] Validate all `/api/...` route paths locally.
- [ ] Recreate a backend container and verify Nginx discovers its new IP without an Nginx restart.
- [ ] Add backend readiness/health checks so Nginx does not route traffic before a service is ready.
- [ ] Update the CI/CD deployment workflow to remove the manual Nginx restart step.
