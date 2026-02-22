# SSO with Microsoft Entra ID — BFF Pattern

A full-stack Single Sign-On (SSO) demo using Microsoft Entra ID (Azure AD), built with the **Backend for Frontend (BFF)** pattern. The browser never touches access tokens — they are acquired and held server-side.

## Architecture

```
Browser (React + Vite)
    │
    │  Cookie (HttpOnly, bff.session)
    ▼
BFF — SsoEntraId.Bff (.NET 10)
    │  OIDC with Entra ID
    │  Issues session cookie
    │  Proxies /api/* → API (YARP)
    │  Injects Bearer token before forwarding
    ▼
API — SsoEntraId.Api (.NET 10)
    │  Validates JWT Bearer token
    ▼
Microsoft Entra ID
```

| Service  | URL                      |
|----------|--------------------------|
| Frontend | http://localhost:5173    |
| BFF      | https://localhost:5001   |
| API      | https://localhost:5002   |

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 20+](https://nodejs.org/) (or Bun)
- A Microsoft Entra ID (Azure AD) tenant with sufficient permissions to create an App Registration

---

## Step 1 — Azure App Registration

### 1.1 Create the App Registration

1. Sign in to the [Azure Portal](https://portal.azure.com)
2. Go to **Microsoft Entra ID → App registrations → New registration**
3. Fill in:
   - **Name**: `SsoEntraId` (or any name you like)
   - **Supported account types**: *Accounts in this organizational directory only*
   - **Redirect URI**: `Web` → `https://localhost:5001/signin-oidc`
4. Click **Register**

### 1.2 Note down the IDs

On the **Overview** page, copy:
- **Application (client) ID**
- **Directory (tenant) ID**

### 1.3 Create a Client Secret

1. Go to **Certificates & secrets → New client secret**
2. Set a description and expiry, then click **Add**
3. Copy the **Value** immediately — it will not be shown again

### 1.4 Configure Authentication

1. Go to **Authentication**
2. Under **Front-channel logout URL**, enter: `https://localhost:5001/signout-oidc`
3. Enable **ID tokens** under *Implicit grant and hybrid flows*
4. Click **Save**

### 1.5 Expose an API (for the downstream API scope)

1. Go to **Expose an API**
2. Set **Application ID URI** to `api://<your-client-id>`
3. Add a scope: `access_as_user` — grant it to Admins and users

### 1.6 (Optional) Restrict access to a specific group

1. Go to **Token configuration → Add groups claim**
2. Select **Security groups**, save
3. Note the **Object ID** of the group you want to allow

---

## Step 2 — Configure the BFF

### 2.1 Update `appsettings.json`

Open `backend/SsoEntraId.Bff/appsettings.json` and fill in your values:

```json
{
  "FrontendUrl": "http://localhost:5173",
  "RequiredGroupId": "<your-group-object-id>",
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "<your-tenant-id>",
    "ClientId": "<your-client-id>",
    "ClientSecret": "",
    "CallbackPath": "/signin-oidc"
  },
  "ApiScopes": "api://<your-client-id>/access_as_user"
}
```

> Leave `RequiredGroupId` empty to allow all users in the tenant.

### 2.2 Store the Client Secret (User Secrets — never commit secrets)

```bash
cd backend/SsoEntraId.Bff
dotnet user-secrets init
dotnet user-secrets set "AzureAd:ClientSecret" "<your-client-secret>"
```

### 2.3 Update the API config

Open `backend/SsoEntraId.Api/appsettings.json`:

```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "<your-tenant-id>",
    "ClientId": "<your-client-id>",
    "Audience": "api://<your-client-id>"
  }
}
```

---

## Step 3 — Install Dependencies

### Frontend

```bash
cd frontend
npm install
```

---

## Step 4 — Run the Application

Open three terminals and run each service:

**Terminal 1 — BFF**
```bash
cd backend/SsoEntraId.Bff
dotnet run
```

**Terminal 2 — API**
```bash
cd backend/SsoEntraId.Api
dotnet run
```

**Terminal 3 — Frontend**
```bash
cd frontend
npm run dev
```

Then open http://localhost:5173 in your browser.

---

## How It Works

### Login Flow

```
1. User clicks "Sign in"
2. Browser → GET /auth/login (BFF)
3. BFF → 302 redirect to Entra ID login page
4. User authenticates with Microsoft
5. Entra ID → POST /signin-oidc with auth code (BFF)
6. BFF exchanges code for tokens, validates group membership
7. BFF sets HttpOnly session cookie (bff.session)
8. Browser redirected back to frontend — logged in
```

### API Calls

```
1. React makes fetch() to /api/* (no token in browser)
2. Vite dev proxy forwards to BFF
3. BFF validates session cookie
4. YARP acquires access token from cache
5. YARP forwards request to API with Bearer token
6. API validates JWT, returns data
```

### Logout Flow

```
1. User submits logout form (POST /auth/logout + CSRF token)
2. BFF validates CSRF, clears session cookie
3. BFF redirects to Entra ID end-session endpoint
4. Entra ID redirects back to /signout-callback-oidc
5. Browser lands on frontend /
```

---

## Security Design

| Concern | Approach |
|---|---|
| Tokens in browser | Never — BFF holds all tokens server-side |
| Session cookie | HttpOnly, Secure, SameSite=Lax |
| CSRF protection | Double-submit cookie pattern (`XSRF-TOKEN` + `X-XSRF-TOKEN` header) |
| Open redirect | `returnUrl` must start with `/` and not `//` |
| Group restriction | Checked in `OnTokenValidated` before session cookie is issued |
| Client secret | Stored in .NET User Secrets, never in source control |

---

## Project Structure

```
sso-entraid/
├── backend/
│   ├── SsoEntraId.Bff/           # BFF — OIDC, session, YARP proxy
│   │   ├── Controllers/
│   │   │   └── AuthController.cs # /auth/login, /auth/logout, /auth/me
│   │   ├── Program.cs
│   │   └── appsettings.json
│   └── SsoEntraId.Api/           # Protected API — JWT validation
│       ├── Controllers/
│       │   └── WeatherForecastController.cs
│       ├── Program.cs
│       └── appsettings.json
└── frontend/                     # React + Vite SPA
    ├── src/
    │   ├── auth/
    │   │   └── useAuth.ts        # Auth state hook
    │   ├── App.tsx
    │   └── index.css
    └── vite.config.ts            # Dev proxy → BFF
```

---

## Key Libraries

| Library | Purpose |
|---|---|
| `Microsoft.Identity.Web` | OIDC + JWT validation integration with Entra ID |
| `Yarp.ReverseProxy` | Proxy `/api/*` from BFF to API |
| `Microsoft.AspNetCore.Authentication.OpenIdConnect` | OIDC protocol handler |
| `React` + `Vite` | Frontend SPA |

---

## Troubleshooting

**Login redirects back to BFF instead of frontend**
- Ensure `FrontendUrl` in `appsettings.json` is set to `http://localhost:5173`

**CSRF token error on logout**
- Make sure you are authenticated first — the `XSRF-TOKEN` cookie is only set for logged-in users

**403 after login**
- The user's account is not in the required Entra ID group. Check `RequiredGroupId` in config, or leave it empty to allow all users.

**SSL certificate error**
- Trust the .NET dev certificate: `dotnet dev-certs https --trust`
