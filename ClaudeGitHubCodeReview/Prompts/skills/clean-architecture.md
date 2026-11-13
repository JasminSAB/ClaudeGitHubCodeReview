# Clean Architecture

Use for: layers, dependency direction, thin controllers, testability.

- Dependency direction: Web -> Application -> Domain; Infrastructure implements interfaces
  defined inward. Flag inward layers referencing EF Core, ASP.NET Core or Identity types.
- Thin controllers: no business logic, query composition, report building or file generation in
  controllers. Flag controllers that bypass the application layer and talk to repositories directly
  when the rest of the code goes through services.
- Use cases with explicit input/output models (commands/queries/DTOs) instead of binding entities
  and returning entities to views.
- Cross-cutting concerns (time, current user, transactions, logging) behind abstractions.
- Testability: can the business rules be tested without a database or HTTP?
- Composition root (Program.cs): one-off settings done per request that belong at startup, seeding,
  migrations strategy.
