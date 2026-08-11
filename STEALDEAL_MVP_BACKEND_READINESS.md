# StealDeal Backend MVP Readiness

> Reviewed: 2026-08-11
>
> Scope: presentation-demo flows only: customer registration/login, browsing and
> finding stores/surprise bags, seller application, and Admin approval/rejection.

## Overall verdict

The backend is ready enough to connect registration, login, and basic public
store/bag browsing to a client. It is **not yet ready for the complete requested
demo** because seller application and approval/rejection are not implemented as
an end-to-end flow.

Search and filtering do not need to block this MVP. The client can load the
small demo lists and filter them locally. Server-side pagination/search can wait
until the data set grows.

## Flow status

| Flow | Status | Current state |
|---|---|---|
| Register | Usable | `POST /api/auth/register` creates a Customer and returns a verification-required response. Roles are seeded by the Identity migration. |
| Login/current user | Usable | `POST /api/auth/login` returns an access token and refresh cookie; `GET /api/auth/me` returns identity and roles. Login currently does not require verified email. |
| List/view stores | Usable with client filtering | `GET /api/stores` and `GET /api/stores/{id}` exist. The list includes inactive and unverified stores, so the buyer UI should show only `isActive && isVerify`. |
| List/view surprise bags | Usable with client filtering | `GET /api/bags` and `GET /api/bags/{id}` return the useful card fields. For now, load the full list and filter by `storeId`; the store-specific endpoint does not load store/category details correctly. |
| Bag/store search and filters | MVP workaround available | There are no backend search/filter query parameters. Client-side filtering is enough for a small seeded presentation data set. |
| Buyer applies to become Seller | Missing | Registration always creates a Customer. There is no seller-application model, status, or endpoint. Store creation is public and uses a fixed user ID instead of the logged-in applicant. |
| Admin approves/rejects application | Missing | Admin can generically edit a user's roles, and a store can be verified/toggled, but these are separate operations and Store authorization is commented out. There is no pending request list or explicit rejection state/reason. |

## Demo blockers to fix

1. Remove the fixed user ID from store creation and derive the applicant from
   the access token.
2. Restore authorization on Store write/admin endpoints. Public browsing can
   remain anonymous.
3. Add a minimal seller-application state with `Pending`, `Approved`, and
   `Rejected`, plus customer apply and Admin list/decision endpoints. Reusing an
   unverified `StoreProfile` as the application is sufficient for this MVP.
4. On approval, grant the applicant the `Seller` role. The user must then
   refresh or log in again so the new JWT contains that role.
5. Make browser routing work through either a frontend development proxy/API
   gateway or CORS on the Store service. Currently only Identity configures
   CORS.

## Demo setup still required

- Apply the Identity and Store migrations to a clean SQL Server database.
- Prepare one Admin account; there is no first-Admin bootstrap endpoint.
- Seed a few categories, verified stores, and available bags. Surprise bags
  have no image field yet, so use client placeholders for the presentation.
- Run one end-to-end smoke test from the actual client origin. There are no
  automated test projects in the repository.

## Build check

The Identity and Store solutions build successfully on .NET 10 with no errors.
Store has two warnings: an uninitialized bag `Status` property and a known
high-severity advisory for `Microsoft.OpenApi` 2.4.1. Neither blocks the demo,
but the package should be updated before treating the service as release-ready.

## Recommended MVP boundary

Implement only the seller-application blockers above. Keep search/filtering on
the client, use placeholder bag images, and defer pagination, media management,
addresses, payments, vouchers, delivery, payouts, and production-grade workflow
coordination until after the capstone demo.
