# Event-Driven E-Commerce Boilerplate (.NET 8, Kafka, Postgres, Outbox, React)

Overview
- UserService and OrderService store domain entities and OutboxEntry in PostgreSQL.
- Each service writes domain entity + OutboxEntry in same DB transaction (transactional outbox).
- Each service runs an internal OutboxDispatcher BackgroundService to publish to Kafka.
- EventBridge consumes Kafka and forwards to React frontend via SignalR.
- JWT authentication, Serilog logging, correlation-id middleware, OpenTelemetry + Jaeger.
- Docker Compose brings up Postgres, Kafka, Zookeeper, Jaeger, services, and frontend.

Run locally
1. Build and run:
   docker-compose up --build

2. JWT secret and other config are provided by docker-compose envs.

3. Create a user:
   POST http://localhost:5001/api/users
   body: { "name": "Alice", "email": "alice@example.com" }
   Use /api/auth/login to obtain a token (see README endpoints).

4. Create an order (authenticated):
   POST http://localhost:5002/api/orders
   Authorization: Bearer <token>

Frontends
- React frontend at http://localhost:3000 shows live events (users.created, orders.created).

