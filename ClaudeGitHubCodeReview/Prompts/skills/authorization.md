# Authorization

Use for: who may do what to which resource.

- Secure by default: a global fallback policy (`RequireAuthenticatedUser`) so a new controller is not
  accidentally public; explicit `[AllowAnonymous]` only where intended.
- Role checks expressed as named policies / constants, not magic strings scattered across
  controllers and views.
- Resource-based authorization: does the current user own / may act on *this* entity (this order,
  this invoice)? Look for IDOR: actions that take an id and never check ownership or state.
- Authorization decisions belong in the application layer (or an `IAuthorizationHandler`), not
  only in the UI (hidden buttons are not authorization).
- Over-posting / mass assignment: entities bound directly from the request (`Create(Entity model)`),
  fields like `Id`, `IsDeleted`, `IsActive`, `UserId` settable by the client.
- Antiforgery on every state-changing endpoint; no state changes on GET.
