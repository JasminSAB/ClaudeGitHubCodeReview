# Async and concurrency

Use for: async/await, cancellation, background work, shared state, races.

- `async void` outside event handlers: exceptions crash the process or vanish, and the caller cannot
  await the work. Return `Task`.
- Sync over async: `.Result`, `.Wait()`, `.GetAwaiter().GetResult()` on a request path blocks a
  thread-pool thread and can deadlock or starve the pool under load.
- Cancellation: endpoints and services that do I/O accept a `CancellationToken` and pass it on
  (`HttpContext.RequestAborted` in ASP.NET Core). A request the client abandoned should not keep
  querying the database.
- Fire and forget: `Task.Run` / `_ = SomethingAsync()` inside a request that uses scoped services
  (`DbContext`, the current user) after the response is sent; the scope is disposed under it. Use a
  queue and a `BackgroundService` that creates its own scope.
- Shared mutable state: `static` collections, fields on singletons, or caches read and written by
  concurrent requests without synchronization (`Dictionary`, `List`, counters). Use
  `ConcurrentDictionary`, `Interlocked`, immutable snapshots, or a lock around the whole
  read-modify-write.
- Captive dependencies: a singleton that holds a scoped or transient service (`DbContext`,
  `IHttpContextAccessor` state) keeps it alive across requests and users.
- Check-then-act races: "if not exists, insert", "if balance >= amount, subtract" done in two steps
  without a transaction, a unique constraint or a concurrency token.
- `lock` cannot contain `await`; a `SemaphoreSlim` used as an async lock must be released in
  `finally`.
