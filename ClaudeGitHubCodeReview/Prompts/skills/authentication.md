# Authentication

Use for: sign-in, cookies and tokens, passwords, lockout, sessions.

- Cookie / token settings: `HttpOnly`, `SecurePolicy = Always`, `SameSite`, sensible expiry and sliding
  expiration; HTTPS enforced end-to-end (HSTS, no plain-HTTP production URLs).
- Password policy (length >= 8, or passphrases), lockout enabled on failed attempts, lockout
  duration, and how lockout interacts with deactivated accounts.
- No user / account-state enumeration (different messages or timing for "unknown user",
  "deactivated" and "wrong password" before the password is verified).
- Account deactivation must invalidate existing sessions (security stamp, revalidation interval)
  and be enforced centrally (e.g. a custom `SignInManager.CanSignInAsync` / `IUserClaimsPrincipalFactory`),
  not only in the login action.
- Seeded / default credentials, secrets in source control, secrets printed to logs or console.
- Open redirects (`returnUrl` validation), logout via POST + antiforgery.
