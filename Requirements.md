# Senior .NET Developer Take-Home Test

Welcome! This take-home test is designed to evaluate your skills in building microservices with .NET Core, event-driven architecture, and modern development practices.

## 🎯 What You'll Build

You'll create a **microservices-based e-commerce system** with two services:
- **User Service**: Manages user registration and information
- **Order Service**: Handles order creation and management

The services will communicate via **Kafka events** and be fully **containerized with Docker**.

## 📋 Detailed Requirements

### 1. User Service

**What it does**: Manages user accounts and publishes user creation events.

**API Endpoints**:
```
POST /users          - Create a new user
GET /users/{id}      - Get user by ID
```

**User Model**:
```csharp
{
    id: Guid,           // Unique identifier
    name: string,       // User's full name
    email: string       // User's email address
}
```

**Key Requirements**:
- ✅ Use Entity Framework Core with **in-memory database**
- ✅ Publish a `UserCreated` event to Kafka when a user is created
- ✅ Implement appropriate validation and error handling

### 2. Order Service

**What it does**: Manages orders and publishes order creation events.

**API Endpoints**:
```
POST /orders         - Create a new order
GET /orders/{id}     - Get order by ID
```

**Order Model**:
```csharp
{
    id: Guid,           // Unique identifier
    userId: Guid,       // Reference to the user who placed the order
    product: string,    // Product name
    quantity: int,      // Number of items
    price: decimal      // Total price
}
```

**Key Requirements**:
- ✅ Use Entity Framework Core with **in-memory database**
- ✅ Publish an `OrderCreated` event to Kafka when an order is created
- ✅ Implement appropriate validation and error handling

### 3. Event-Driven Communication

**Kafka Integration**:
- Both services should **publish events** when entities are created
- Both services should **consume events** from the other service (demonstrate cross-service communication)
- Design your event structure and topics as you see fit

### 4. Infrastructure Requirements

**Docker Configuration**:
- ✅ Create `Dockerfile` for each service
- ✅ Create `docker-compose.yml` to run both services + Kafka
- ✅ Services should be accessible and properly networked

**API Documentation**:
- ✅ Ensure APIs are testable and well-documented

## 🤖 AI Tools & Development Approach (if you choose to use them)

**Use Any Tools You Want!**
- Feel free to use **GitHub Copilot, ChatGPT, Claude**, or any AI coding assistants
- We're interested in your problem-solving approach, not memorized syntax
- **Document your process**: Include the prompts/instructions you used with AI tools in your README

**What We Want to See**:
- How you break down the problem
- Your architectural decisions and reasoning
- How you leverage AI tools effectively

## 🚀 Getting Started

### Prerequisites
Make sure you have these installed:
- **.NET 8 SDK** (or latest LTS version)
- **Docker Desktop**
- **Git**

### Running the Application

1. **Clone and navigate to your project**:
   ```cmd
   git clone <your-repo-url>
   cd TakeHomeTest
   ```

2. **Start everything with Docker Compose**:
   ```cmd
   docker-compose up --build
   ```

3. **Verify services are running and test the functionality**

### Testing Your Implementation

**Demonstrate the system working**:
1. Create a user via User Service
2. Create an order for that user via Order Service  
3. Show that services communicate via events
4. Include any automated tests you create

## ✅ What We're Looking For

### Architecture & Design (40%)
- **System Design**: How you structure the microservices
- **Event-Driven Patterns**: Effective use of Kafka for service communication
- **Code Organization**: Clean, maintainable code structure
- **Problem-Solving**: How you handle ambiguity and make decisions

### Technical Implementation (40%)
- **Modern .NET Practices**: Effective use of .NET Core, EF Core, and ecosystem
- **Infrastructure**: Docker setup and service orchestration
- **API Design**: Well-designed service interfaces
- **Event Handling**: Robust event publishing and consumption

### Communication & Documentation (20%)
- **Code Clarity**: Self-documenting code and meaningful comments
- **README Quality**: Clear setup instructions and architectural decisions
- **AI Usage Documentation**: How you leveraged AI tools (if applicable)
- **Testing Strategy**: Your approach to ensuring code quality

## 💡 Bonus Considerations (Optional)

Areas where you can demonstrate additional expertise:
- **Observability**: Health checks, logging, monitoring
- **Resilience**: Retry policies, circuit breakers, graceful degradation  
- **Configuration Management**: Environment-specific settings
- **Testing**: Comprehensive test coverage and strategies

## 📝 Deliverables

1. **GitHub Repository** with complete source code
2. **Working docker-compose.yml** that starts the entire system
3. **README.md** that includes:
   - Setup and usage instructions
   - Architecture overview and key decisions
   - AI tools usage (prompts/approach if applicable)
   - Any assumptions or trade-offs made

## ❓ Questions or Clarifications?

We intentionally left some details open-ended to see how you approach ambiguous requirements. Feel free to:
- Make reasonable assumptions and document them in your README
- Reach out if you need critical clarifications

## ⏱️ Time Expectation

This test is designed to take approximately **4-6 hours** for an experienced .NET developer. Focus on:
1. **Core functionality first** (working APIs + Kafka integration)
2. **Then add robustness** (error handling, testing, documentation)
3. **Finally, demonstrate expertise** (architectural choices, bonus features if time permits)

**Remember**: We value working software and clear thinking over perfect implementation.

---

**Good luck! We're excited to see your approach and solution! 🚀**