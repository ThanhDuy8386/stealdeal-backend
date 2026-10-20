# StealDeal AWS Deployment Context

Short operational reference for the current architecture, AWS setup and CI/CD flow.

## 1. Current architecture

Two independent repositories run on one EC2 instance:

~~~text
Backend:  stealdeal-backend
Frontend: steal-deals-web
~~~

Backend Compose services:

~~~text
Identity       5158
Notification   5053
Store          5169
Cart           5185
Order          5165
Payment        5155
SQL Server     1433
Redis          6379
RabbitMQ       5672
DatabaseMigrator (one-shot container)
~~~

Frontend Nginx is the public entrypoint:

~~~text
Internet:80 -> Nginx -> Next.js frontend
                    -> backend APIs through stealdeal-network
~~~

The two Compose projects share this external network:

~~~bash
docker network create stealdeal-network
~~~

Only ports 22 and 80 should be public in the lab. Database, broker, cache and API ports remain private.

## 2. AWS and EC2 context

~~~text
AWS account:       735394850167
Region:            us-east-1
EC2 instance:      i-012543d83b62f9bff
ECR registry:      735394850167.dkr.ecr.us-east-1.amazonaws.com

Backend Compose:   /opt/stealdeal/stealdeal-backend/src/Services
Frontend Compose:  /opt/stealdeal/steal-deals-web
~~~

Identities are separate:

~~~text
GitHub Actions IAM user:  github-actions-stealdeal-lab
  - CI pushes images to ECR
  - CD sends commands through SSM

EC2 IAM role:             stealdeal-ec2-ssm-role
  - SSM Agent communication
  - Docker pulls images from ECR
~~~

GitHub secrets in both repositories:

~~~text
AWS_ACCESS_KEY_ID
AWS_SECRET_ACCESS_KEY
AWS_ACCOUNT_ID
AWS_REGION
AWS_EC2_INSTANCE_ID
~~~

The access-key setup is acceptable for this lab. Use GitHub Actions OIDC before production.

## 3. ECR and image tags

Repositories:

~~~text
stealdeal-frontend
stealdeal-identity
stealdeal-notification
stealdeal-store
stealdeal-cart
stealdeal-order
stealdeal-payment
stealdeal-database-migrator
~~~

Images use the triggering commit SHA as an immutable tag:

~~~text
stealdeal-identity:<commit-sha>
stealdeal-frontend:<commit-sha>
~~~

CI builds and pushes images on GitHub-hosted runners. CD does not build images; it logs in to ECR on EC2, pulls the required tag and recreates containers.

Compose supports local and ECR images. The backend pattern is:

~~~yaml
image: ${IDENTITY_IMAGE:-stealdeal-identity:local}
~~~

The actual Compose file uses the IDENTITY_IMAGE environment variable with a local fallback. The same pattern is used for every backend image and the frontend uses FRONTEND_IMAGE.

## 4. What EC2 needs

EC2 needs:

- Docker and Docker Compose;
- AWS CLI and a working SSM Agent;
- the external stealdeal-network;
- backend/frontend Compose files;
- frontend nginx.conf;
- runtime .env files containing secrets and image variables;
- the persistent SQL Server volume.

EC2 does not need application source code or Dockerfiles for CD. CD uses --no-build.

Changes to docker-compose.yml and nginx.conf are outside the application image and must be synchronized to EC2 separately. Backend CD verifies that Compose resolves the expected ECR image before pulling it.

## 5. CI on main

Workflows:

~~~text
Backend:  .github/workflows/ci.yml
Frontend: .github/workflows/ci.yml
~~~

Automatic trigger:

~~~yaml
push:
  branches:
    - main
~~~

Both workflows build from the exact commit that triggered the run.

### Backend CI sequence

~~~text
push/merge main
    -> checkout exact commit
    -> detect changed services
    -> build/test changed .NET APIs
    -> build/push changed API images
    -> build/push database-migrator when required
    -> upload deployment-manifest.json
~~~

Backend CI also supports manual dispatch. full_build=true is used for initial ECR bootstrap or a complete image refresh.

### Change detection

Service filters match the complete service directory:

~~~yaml
identity:
  - 'src/Services/Identity/**'
~~~

Migration filters only match migration folders:

~~~yaml
identity_migrator:
  - 'src/Services/Identity/**/Migrations/**'
~~~

The same migration rule exists for Notification, Store, Order and Payment. The shared migrator has:

~~~yaml
database_migrator:
  - 'src/Services/database/DatabaseMigrator/**'
~~~

Behavior:

~~~text
Infrastructure logic only:
  API image: yes
  Migrator image: no
  Run migrator: no

Migration file:
  API image: yes
  Migrator image: yes
  Run migrator: yes

DatabaseMigrator code:
  API image: no
  Migrator image: yes
  Run migrator: yes
~~~

The decision is path-based. A model change without a committed migration does not update the database; the generated migration must be committed.

### Matrices and manifest

api_matrix contains APIs requiring dotnet validation. docker_matrix contains APIs requiring images plus database-migrator when migration-related paths changed.

full_build=true marks all six APIs and the migrator as changed, and sets run_migrator=true.

The CI artifact contains:

~~~json
{
  "commit_sha": "<sha>",
  "api_services": ["identity"],
  "docker_services": ["identity", "database-migrator"],
  "run_migrator": true
}
~~~

CD uses this manifest instead of detecting changes again.

### .NET validation

For every changed API:

~~~bash
dotnet restore <solution>
dotnet build <solution> --configuration Release --no-restore
dotnet test <solution> --configuration Release --no-build
~~~

If tests fail, Docker images are not published. If only the migrator changes, the API test job is skipped and the migrator image can still be built.

### Docker build and push

API example:

~~~bash
docker build -f src/Services/Identity/Dockerfile \
  -t 735394850167.dkr.ecr.us-east-1.amazonaws.com/stealdeal-identity:<sha> \
  src/Services/Identity

docker push 735394850167.dkr.ecr.us-east-1.amazonaws.com/stealdeal-identity:<sha>
~~~

The migrator uses src/Services as its build context because its Dockerfile copies multiple service projects.

CI checks whether an immutable tag already exists before pushing, so a rerun does not fail only because part of the same commit was already pushed.

## 6. CD on main

The CD files must exist on the default branch main for workflow_run to work.

Automatic flow:

~~~text
successful CI run on main
    -> workflow_run triggers CD
    -> GitHub runner creates an SSM command
    -> SSM Agent executes it on EC2
    -> EC2 logs in to ECR
    -> Compose pulls immutable image tags
    -> only required containers are recreated
~~~

Backend CD also supports manual deployment:

~~~text
image_sha:    image tag to deploy
services:     all or comma-separated API names
run_migrator: true/false
~~~

Use services=all only when all services were built with that SHA. For selective CI, use the exact service list from the manifest.

### Backend CD sequence

1. Download the CI manifest, or create one from manual inputs.
2. Construct ECR image URIs from the commit SHA.
3. Send one JSON-encoded command list through SSM.
4. Change to the backend Compose directory.
5. Export image variables and persist them in backend .env.
6. Log in to ECR using the EC2 IAM role.
7. If run_migrator=true, verify the image, pull it, start SQL Server/RabbitMQ/Redis and run the one-shot migrator with --no-build.
8. Verify each changed API resolves to the expected ECR image.
9. Pull only changed APIs.
10. Recreate them with --no-build --no-deps --force-recreate.
11. Verify changed containers are running.
12. Restart Nginx because the current configuration can retain old backend container IPs.
13. Retry the port 80 health check.
14. Print SSM output and fail the workflow if the remote command failed.

Unchanged APIs, databases, broker, cache and frontend containers are not recreated.

### Frontend CD sequence

1. Get the image tag from successful Frontend CI.
2. Configure AWS credentials on the GitHub runner.
3. Send an SSM command to EC2.
4. Set FRONTEND_IMAGE.
5. Log in to ECR on EC2.
6. Pull the frontend image.
7. Recreate frontend and nginx without building.
8. Print resolved/running image information.
9. Run the port 80 health check.

Frontend CD does not recreate backend containers.

## 7. EC2 stopped/offline behavior

CI can finish while EC2 is stopped because CI runs on GitHub-hosted runners and pushes to ECR.

CD/SSM fails while EC2 or SSM Agent is offline. The commit and ECR images are not lost.

After starting EC2:

1. Wait for EC2 status checks to pass.
2. Confirm the instance is Online in Systems Manager.
3. Re-run the failed CD workflow, or use Backend CD manual dispatch with the original SHA.
4. Deploy the exact affected services.
5. Set run_migrator=true if the deployment included migration changes.

Manual CD can create a manifest later as long as the image tags remain in ECR.

## 8. Local versus EC2 deployment

Local development may build from source:

~~~bash
cd src/Services
docker compose up -d --build
~~~

EC2 CD must pull from ECR:

~~~bash
cd /opt/stealdeal/stealdeal-backend/src/Services
docker compose pull <service>
docker compose up -d --no-build --no-deps --force-recreate <service>
~~~

Do not use docker compose up -d --build on EC2 for CI/CD.

These messages indicate a stale Compose file or an unapplied image variable:

~~~text
No image to be pulled
stealdeal-services-identity-api
~~~

Expected image format:

~~~text
735394850167.dkr.ecr.us-east-1.amazonaws.com/stealdeal-identity:<sha>
~~~

## 9. Rollback

Application rollback uses a previous immutable image SHA:

~~~text
Backend CD manual dispatch
  image_sha: previous successful SHA
  services: affected service(s)
  run_migrator: false unless explicitly required
~~~

Database rollback is not automated. EF Core migrations are forward-oriented; use a SQL Server backup and a planned recovery procedure for destructive schema changes.

## 10. Daily lab operation

Start EC2, wait for SSM Online, then check both Compose projects:

~~~bash
cd /opt/stealdeal/stealdeal-backend/src/Services
docker compose ps

cd /opt/stealdeal/steal-deals-web
docker compose ps
~~~

When testing is complete:

- stop the EC2 instance;
- keep the EBS volume if database data is needed;
- never use docker compose down -v unless database destruction is intended;
- do not terminate the instance without a database backup.

Stopping EC2 removes compute usage charges, but EBS, Elastic IP and other resources may still incur charges.

## 11. Known follow-ups

- Add dedicated health endpoints for all APIs and use them in CD.
- Replace the Nginx restart workaround with Docker DNS re-resolution.
- Replace long-lived IAM keys with GitHub Actions OIDC.
- Add PR validation if CI should run before merge without deploying.
- Use a managed production database strategy such as RDS for SQL Server, backups and high availability.
- Consider ECS or another orchestrator when the Compose-on-EC2 lab is stable.
