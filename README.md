# SSO with Azure Entra External ID — BFF Pattern (.NET 10 + React)

A reference implementation of authentication using **Azure Entra External ID (CIAM)** with:

- **Sign-in**: Browser-delegated OIDC (redirect to Entra hosted UI)
- **Sign-up**: Native Authentication (custom UI, no redirect — Email OTP → Password → auto sign-in)
- **BFF (Backend-for-Frontend)**: ASP.NET Core 10, YARP reverse proxy, session cookie
- **Protected API**: Downstream .NET 10 Web API, validates Bearer tokens

---

## Architecture Overview

```
Browser (React)
    │
    │  cookie (bff.session)
    ▼
BFF — SsoEntraId.Bff  (:5001)          Azure Entra External ID
    │  OIDC sign-in ──────────────────► ciamlogin.com (hosted UI)
    │  Native auth sign-up ───────────► signup/v1.0/* + oauth2/v2.0/token
    │
    │  Bearer token (YARP inject)
    ▼
API — SsoEntraId.Api  (:5002)
    │  validates JWT (AddMicrosoftIdentityWebApi)
    └─► /api/weatherforecast
```

### Two App Registrations

| | BFF App | API App |
|---|---|---|
| Role | OIDC client + native auth client | Resource server (token audience) |
| Client type | Public client (PKCE, no secret needed for native auth) | No client secret needed |
| Issues tokens | Requests tokens scoped to API App | — |
| Validates tokens | Session cookie (user auth) | Bearer JWT (`aud = api://API_CLIENT_ID`) |

> **Why two registrations?**
> If BFF requests a scope from itself (self-referential), Azure Entra External ID's hosted sign-in UI
> displays an "account not found" error. Separating the API into its own registration avoids this.

---

## Prerequisites

- .NET 10 SDK
- Node.js 20+
- An **Azure Entra External ID (CIAM)** tenant
  (create free at [entra.microsoft.com](https://entra.microsoft.com) → **External Identities**)

---

## Azure Portal Setup

### 1. Create BFF App Registration

1. **Microsoft Entra admin center** → **App registrations** → **New registration**
   - Name: `SsoEntraId.Bff` (or any name)
   - Supported account types: **Accounts in this organizational directory only**
   - Redirect URI: **Web** → `https://localhost:5001/signin-oidc`
2. After creation, note the **Application (client) ID** → this is `BFF_CLIENT_ID`
3. Go to **Authentication**:
   - Under **Implicit grant and hybrid flows**: leave unchecked
   - Under **Advanced settings** → **Allow public client flows**: **Yes** (required for native auth)
   - Under **Advanced settings** → **Enable the following mobile and desktop flows**: **Yes** ← also required for native auth
   - Click **Save**
4. Go to **Authentication** → scroll to **Native Authentication**:
   - **Enable native authentication**: **Yes**
   - Click **Save**

### 2. Create API App Registration

1. **App registrations** → **New registration**
   - Name: `SsoEntraId.Api`
   - Supported account types: same as BFF
   - No Redirect URI needed
2. Note the **Application (client) ID** → this is `API_CLIENT_ID`
3. Go to **Expose an API**:
   - **Application ID URI** → **Add** → accept the default `api://API_CLIENT_ID` → **Save**
   - **Add a scope**:
     - Scope name: `access_as_user`
     - Who can consent: **Admins and users**
     - Admin consent display name: `Access SsoEntraId.Api as user`
     - State: **Enabled**
     - Click **Add scope**

### 3. Grant API Permission to BFF App

1. Go back to the **BFF app registration**
2. **API permissions** → **Add a permission** → **My APIs** → select `SsoEntraId.Api`
3. **Delegated permissions** → check `access_as_user` → **Add permissions**
4. Click **Grant admin consent for `<your-tenant>`** → **Yes**
   - The status column must show a green checkmark **"Granted for \<tenant\>"**

### 4. Create a User Flow (Sign up and sign in)

1. **External Identities** → **User flows** → **New user flow**
   - Flow type: **Sign up and sign in**
   - Name: e.g. `signupsignin`
   - Identity providers: **Email with password**
   - User attributes to collect: **Email Address**, **Display Name** (or as needed)
   - Click **Create**
2. Open the new user flow → **Properties** → **Enable native authentication**: **Yes** → **Save**
3. **Applications** → **Add application** → select the **BFF app** → **Select**

---

## Configuration

Update the following files with your actual values.

### `backend/SsoEntraId.Bff/appsettings.json`

```json
{
  "AzureAd": {
    "Instance": "https://<tenant-subdomain>.ciamlogin.com/",
    "TenantId": "<TENANT_ID>",
    "ClientId": "<BFF_CLIENT_ID>",
    "ClientSecret": "",
    "CallbackPath": "/signin-oidc"
  },
  "Entra": {
    "TenantDomain": "<tenant-subdomain>.onmicrosoft.com"
  },
  "ApiScopes": "api://<API_CLIENT_ID>/access_as_user",
  "FrontendUrl": "http://localhost:5173"
}
```

| Key | Where to find |
|---|---|
| `Instance` | `https://<tenant-subdomain>.ciamlogin.com/` — visible in Entra admin center overview |
| `TenantId` | Directory (tenant) ID on the app overview page |
| `BFF_CLIENT_ID` | Application (client) ID of the BFF app registration |
| `Entra:TenantDomain` | `<tenant-subdomain>.onmicrosoft.com` — visible in tenant overview |
| `API_CLIENT_ID` | Application (client) ID of the API app registration |

### `backend/SsoEntraId.Api/appsettings.json`

```json
{
  "AzureAd": {
    "Instance": "https://<tenant-subdomain>.ciamlogin.com/",
    "TenantId": "<TENANT_ID>",
    "ClientId": "<API_CLIENT_ID>",
    "Audience": "api://<API_CLIENT_ID>"
  }
}
```

---

## Running the Project

Open three terminals:

```bash
# Terminal 1 – API
cd backend/SsoEntraId.Api
dotnet run

# Terminal 2 – BFF
cd backend/SsoEntraId.Bff
dotnet run

# Terminal 3 – Frontend (dev server with Vite proxy)
cd frontend
npm install
npm run dev
```

Open `http://localhost:5173`.

> The Vite dev server proxies all requests to the BFF at `https://localhost:5001`.
> In production, serve the React build as static files from the BFF directly.

---

## Authentication Flows

### Sign-in (Browser-delegated OIDC)

```
User clicks Login
    → GET /auth/login
    → BFF redirects to Entra hosted UI (ciamlogin.com)
    → User enters credentials
    → Entra redirects back to /signin-oidc with authorization code
    → BFF exchanges code for tokens (PKCE, no client_secret)
    → BFF creates session cookie (bff.session)
    → BFF redirects to frontend
    → Frontend calls GET /auth/me → receives user info
```

PKCE (Proof Key for Code Exchange) is used instead of a client secret. The BFF generates a
`code_verifier` / `code_challenge` pair per login attempt. No secret is stored in config.

### Sign-up (Native Authentication)

Sign-up happens entirely via custom UI — no redirect to Entra hosted pages.

```
Step 1 – Email
    POST /auth/native/signup/start  { email }
    → BFF calls Entra signup/v1.0/start + signup/v1.0/challenge
    → Entra sends OTP email
    ← { continuationToken, step: "otp-required", codeLength, maskedEmail }

Step 2 – OTP Verification
    POST /auth/native/signup/verify-otp  { continuationToken, otp }
    → BFF calls Entra signup/v1.0/continue (grant_type=oob)
    ← { continuationToken, step: "password-required" }

Step 3 – Password + Auto Sign-in
    POST /auth/native/signup/complete  { continuationToken, password }
    → BFF calls Entra signup/v1.0/continue (grant_type=password)
    → BFF calls Entra oauth2/v2.0/token (grant_type=continuation_token)
    → BFF decodes id_token, builds ClaimsPrincipal
    → BFF calls SignInAsync → sets bff.session cookie
    ← { step: "complete" }
    → Frontend reloads → useAuth() picks up new session
```

The `continuation_token` is a short-lived, Entra-signed token passed between steps.
It is safe to send to the frontend: it is bound to the `client_id` and expires quickly.

### Logout

```
Browser-delegated session:
    POST /auth/logout (with XSRF token)
    → BFF signs out of cookie + OIDC (end-session at Entra)
    → Redirect to frontend /

Native auth session:
    POST /auth/logout (with XSRF token)
    → BFF signs out of cookie only (no Entra browser session exists)
    → Redirect to frontend /
```

The BFF detects native auth sessions via `auth_type = "native"` stored in auth properties.

---

## BFF Endpoints

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/auth/login?returnUrl=/` | — | Initiates OIDC sign-in |
| POST | `/auth/logout` | Required + XSRF | Signs out |
| GET | `/auth/me` | Required | Returns `{ name, email, roles }` |
| POST | `/auth/native/signup/start` | — | Starts native sign-up, sends OTP |
| POST | `/auth/native/signup/verify-otp` | — | Verifies OTP |
| POST | `/auth/native/signup/complete` | — | Sets password, creates session |
| ANY | `/api/**` | Required | YARP proxy → downstream API |

---

## CSRF Protection

All state-mutating BFF endpoints (logout, native sign-up) require an XSRF token.

- After authentication, BFF sets a JavaScript-readable cookie `XSRF-TOKEN`
- The frontend must read this cookie and send the value in the `X-XSRF-TOKEN` request header
  (or as form field `__RequestVerificationToken` for form POSTs)
- BFF validates the token via `IAntiforgery`

---

## YARP Token Injection

When the frontend calls `/api/**`, YARP:

1. Tries to acquire a fresh access token via `ITokenAcquisition` (MSAL cache — works for OIDC sessions)
2. Falls back to reading `access_token` stored in the cookie auth properties (works for native auth sessions)
3. Injects the token as `Authorization: Bearer <token>` on the proxied request

The downstream API validates this Bearer token. Its audience must match `api://<API_CLIENT_ID>`.

---

## Project Structure

```
backend/
├── SsoEntraId.Bff/
│   ├── Controllers/
│   │   └── AuthController.cs          # /auth/login, /auth/logout, /auth/me
│   ├── NativeAuth/
│   │   ├── EntraNativeAuthService.cs  # Typed HttpClient for Entra native auth API
│   │   └── NativeSignUpController.cs  # /auth/native/signup/*
│   ├── Program.cs                     # OIDC + Cookie auth, YARP, CSRF, CORS setup
│   └── appsettings.json
└── SsoEntraId.Api/
    ├── Controllers/
    │   └── WeatherForecastController.cs   # Protected sample endpoint
    ├── Program.cs                          # AddMicrosoftIdentityWebApi
    └── appsettings.json

frontend/
└── src/
    ├── auth/
    │   └── useAuth.ts          # React hook: GET /auth/me on mount
    ├── components/
    │   └── SignUpForm.tsx       # Multi-step native sign-up form
    └── App.tsx                  # Main app shell
```

---

## Troubleshooting

### `AADSTS65001: consent required`

The BFF app has not been granted admin consent for the API scope.
→ **BFF app registration → API permissions → Grant admin consent**

### `AADSTS550022: Confidential Client is not supported`

Native authentication requires a public client flow.
→ **BFF app registration → Authentication → Allow public client flows: Yes**

### Native auth returns HTTP 404 (empty body)

The user flow is not linked to the app, or native authentication is not enabled.
→ Check: **User flow → Applications** (BFF app must be listed)
→ Check: **User flow → Properties → Enable native authentication: Yes**
→ Check: `Entra:TenantDomain` in appsettings is `<subdomain>.onmicrosoft.com` (not a GUID)

### `AADSTS55103: credential_required` after OTP

This is expected behavior — it means the OTP was **valid**. Entra returns HTTP 400 with this
error code to signal that the next step is password submission. The BFF treats it as success.

### HTTP 431 Request Header Too Large

OIDC correlation and nonce cookies accumulate across multiple login attempts.
→ Already handled: Kestrel limit raised to 64 KB; cookie `MaxAge` set to 15 minutes.
→ Clear browser cookies for `localhost` if it happens during development.

### "Account not found" on Entra hosted sign-in UI

This occurs when `ApiScopes` in the BFF points to the **BFF app's own client ID** (self-referential scope).
→ Ensure `ApiScopes` is `api://<API_CLIENT_ID>/access_as_user` where `API_CLIENT_ID` is the **API app**, not the BFF app.
