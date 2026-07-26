---
name: dotnet-standards
description: Official .NET 10 Development Standards and Best Practices
version: 1.0
---

# .NET Development Standards

## Objective

You are an expert .NET 10 software engineer.

All generated code MUST follow Microsoft's official coding conventions, Framework Design Guidelines and ASP.NET Core best practices.

Prefer code that could reasonably be accepted in the dotnet/runtime or aspnetcore repositories.

Prioritize:

- Readability
- Maintainability
- Simplicity
- Performance
- Testability
- Extensibility

Never sacrifice architecture for convenience.

---

# Supported Version

Always target:

- .NET 10

Never generate legacy code.

Avoid APIs marked obsolete.

Prefer the newest language features available in C# 14 when applicable.

---

# Solution Structure

Every solution MUST follow this structure.

```
/
│
├── src/
├── tests/
├── docs/
├── samples/
├── tools/
├── eng/
│
├── Directory.Build.props
├── Directory.Packages.props
├── global.json
├── .editorconfig
├── README.md
└── LICENSE
```

Never create projects outside `/src` or `/tests`.

---

# Project Organization

Each project should contain only what belongs to its responsibility.

Recommended folders:

```
Abstractions/
Authentication/
Authorization/
Configurations/
Constants/
Contracts/
Endpoints/
Entities/
Enums/
Events/
Exceptions/
Extensions/
Handlers/
Interfaces/
Mappings/
Middlewares/
Models/
Options/
Pipelines/
Repositories/
Requests/
Responses/
Services/
Validators/
ValueObjects/
```

Do not create miscellaneous folders like:

Helpers

Utils

Common

Misc

Stuff

Manager

Processor

Engine

Unless there is a clear architectural reason.

---

# File Organization

One public type per file.

File name MUST match the type name.

Prefer file-scoped namespaces.

Correct:

namespace ButecoFacil.Reservations;

Never use multiple public classes in one file.

---

# Naming Conventions

## Classes

PascalCase

Examples

ReservationService

CustomerRepository

MenuItem

ReservationHandler

---

## Interfaces

Always prefix with I.

Examples

IReservationRepository

IAgentClient

IChatModel

---

## Methods

PascalCase.

Async methods MUST end with Async.

Correct

CreateReservationAsync()

SearchReservationAsync()

SaveChangesAsync()

Incorrect

CreateReservation()

Load()

Run()

when asynchronous.

---

## Variables

camelCase

Correct

reservation

customer

menuItem

Never use abbreviations.

Incorrect

res

cust

obj

tmp

---

## Fields

Private fields MUST begin with "_"

```
private readonly ILogger<ReservationService> _logger;
private readonly IReservationRepository _repository;
```

---

## Constants

PascalCase.

```
DefaultTimeout

MaximumRetries

CacheDuration
```

---

## Enums

PascalCase

Members also PascalCase.

---

## Generic Types

Use descriptive names.

Prefer

TRequest

TResponse

TEntity

Avoid

T

T1

T2

---

# Language Features

Enable:

Nullable

ImplicitUsings

FileScopedNamespaces

GlobalUsings only when justified

Required Members

Collection Expressions

Primary Constructors where appropriate

Target Typed new

Raw String Literals

Pattern Matching

Switch Expressions

---

# var Usage

Use var ONLY when the type is obvious.

Correct

var reservation = new Reservation();

var items = new List<MenuItem>();

Incorrect

var result = Execute();

Use explicit types when readability improves.

---

# Records

Prefer records for:

DTOs

Requests

Responses

Commands

Queries

Events

Notifications

Never use records for entities.

---

# Classes

Prefer classes for:

Entities

Services

Repositories

Factories

Builders

Infrastructure

---

# Structs

Only when:

small

immutable

performance critical

Otherwise use classes.

---

# Dependency Injection

Always use constructor injection.

Never use Service Locator.

Never inject IServiceProvider unless absolutely required.

Register dependencies using extension methods.

Example

builder.Services.AddApplication();

builder.Services.AddInfrastructure();

builder.Services.AddPresentation();

Program.cs must remain minimal.

---

# Program.cs

Program.cs should contain only:

Host configuration

Dependency Injection

Middleware

Endpoint Mapping

No business logic.

---

# Minimal APIs

Prefer Minimal APIs.

Each endpoint should delegate immediately to the Application layer.

Endpoints should never contain business rules.

---

# Controllers

Only use Controllers when required by framework constraints.

---

# Async

Always use async/await.

Never use:

.Result

.Wait()

Task.Run() inside ASP.NET requests

Every async method must accept:

CancellationToken cancellationToken

as the last parameter.

---

# Exceptions

Never use exceptions for business flow.

Use Result Pattern.

Throw exceptions only for exceptional conditions.

Never catch Exception without logging.

---

# Logging

Always use ILogger<T>.

Never use Console.WriteLine.

Prefer structured logging.

Correct

_logger.LogInformation(
    "Reservation {ReservationId} created",
    reservation.Id);

Incorrect

_logger.LogInformation($"Reservation {reservation.Id}");

---

# Validation

Validation belongs in Application.

Never validate inside repositories.

Never validate inside EF entities.

Use dedicated validators.

---

# Dependency Direction

Always

Presentation

↓

Application

↓

Domain

↓

Infrastructure

Domain must never depend on Infrastructure.

---

# Domain

Entities should protect invariants.

Avoid public setters.

Prefer methods.

Incorrect

reservation.Status = Confirmed;

Correct

reservation.Confirm();

---

# Collections

Never expose List<T>.

Prefer

IReadOnlyCollection<T>

IReadOnlyList<T>

IEnumerable<T>

---

# Nullability

Nullable Reference Types MUST remain enabled.

Avoid null whenever possible.

Prefer:

required

Option

Result

Null Object Pattern

---

# XML Documentation

All public APIs must contain XML documentation.

---

# Comments

Prefer self-documenting code.

Avoid unnecessary comments.

Comment WHY.

Never comment WHAT.

---

# Method Size

Maximum recommended:

40 lines.

Extract private methods.

---

# Class Size

Maximum recommended:

300 lines.

Split responsibilities when exceeded.

---

# Constructor Size

Maximum recommended:

5 injected dependencies.

If exceeded:

Reevaluate responsibilities.

---

# SOLID

Always follow SOLID.

Especially:

Single Responsibility

Dependency Inversion

Open Closed Principle

---

# DRY

Never duplicate logic.

Extract reusable components.

---

# KISS

Prefer the simplest correct solution.

Avoid unnecessary abstractions.

---

# YAGNI

Do not implement future features.

Prepare extension points.

Do not implement unused behavior.

---

# Performance

Prefer:

readonly

sealed where appropriate

ArrayPool

Memory<T>

Span<T>

ValueTask when justified

Avoid:

Reflection

Boxing

Allocations in hot paths

LINQ inside performance critical loops

---

# LINQ

Prefer readability.

Avoid multiple enumerations.

Avoid nested LINQ chains.

Prefer loops when significantly clearer.

---

# Configuration

Use Options Pattern.

Never access IConfiguration throughout the application.

Bind once.

Inject strongly typed options.

---

# Testing

Every feature should be testable.

Preferred libraries:

xUnit

FluentAssertions

Moq

Testcontainers

Architecture tests are recommended.

---

# Logging Context

Every log should include:

CorrelationId

RequestId

ConversationId (when applicable)

UserId (when available)

ElapsedMilliseconds

---

# Observability

Every application should be prepared for:

Health Checks

OpenTelemetry

Metrics

Tracing

Structured Logging

---

# Security

Never hardcode:

Connection Strings

Passwords

Secrets

Tokens

Use configuration providers.

---

# Analyzer Rules

Always enable:

Nullable

ImplicitUsings

TreatWarningsAsErrors

.NET Analyzers

EditorConfig

---

# Code Generation Principles

Whenever generating code:

- Produce production-ready code.
- Avoid placeholders.
- Avoid TODO comments.
- Follow Microsoft naming conventions.
- Prefer composition over inheritance.
- Keep dependencies explicit.
- Write deterministic code.
- Make code easy to test.
- Minimize allocations.
- Prefer immutable models.
- Prefer explicit architecture over convenience.

---

# Final Rule

Every generated file should look as if it was reviewed and accepted by the .NET engineering team.

When in doubt:

Follow Microsoft's official Framework Design Guidelines instead of inventing a custom convention.