# Varsity Hub API

ASP.NET Core (.NET 10) Web API for Varsity Hub, backed by Supabase (Postgres + Auth + Storage).
SQL runs as the calling user so Supabase Row Level Security is enforced end-to-end; admin
operations (OTP, notifications, payment webhooks) run on a service path.

## Run locally

```bash
dotnet restore
dotnet run
```

Local config/secrets are kept in **.NET user-secrets** (never committed). Set them once:

```bash
dotnet user-secrets set "ConnectionStrings:Supabase" "Host=...pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.<ref>;Password=<pwd>;SSL Mode=Require;Trust Server Certificate=true;Pooling=true;Maximum Pool Size=20"
dotnet user-secrets set "Supabase:Url"            "https://<ref>.supabase.co"
dotnet user-secrets set "Supabase:AnonKey"        "<anon-key>"
dotnet user-secrets set "Supabase:ServiceRoleKey" "<service-role-key>"
dotnet user-secrets set "Jwt:Issuer"              "https://<ref>.supabase.co/auth/v1"
dotnet user-secrets set "Jwt:Secret"              "<jwt-secret>"   # legacy HS256 projects
```

> Use the Supabase **Session Pooler** connection string (port 5432). The direct
> `db.<ref>.supabase.co` host is IPv6-only and unreachable from IPv4-only networks.

- OpenAPI document (Development): `GET /openapi/v1.json`
- Health check: `GET /health`

## Deploy to Railway

The repo ships a `Dockerfile` and `railway.json`; Railway builds with the Dockerfile and
health-checks `/health`.

1. Push this repo to GitHub.
2. In Railway: **New Project → Deploy from GitHub repo** → pick this repo.
3. Add the environment variables below (Railway **Variables** tab). ASP.NET maps `__` to nested keys.
4. Deploy. Railway injects `PORT`; the app binds to it automatically.

### Required environment variables

| Variable | Example |
|---|---|
| `ConnectionStrings__Supabase` | `Host=aws-0-<region>.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.<ref>;Password=<pwd>;SSL Mode=Require;Trust Server Certificate=true;Pooling=true;Maximum Pool Size=20` |
| `Supabase__Url` | `https://<ref>.supabase.co` |
| `Supabase__ServiceRoleKey` | `<service-role-key>` (server-side only) |
| `Jwt__Issuer` | `https://<ref>.supabase.co/auth/v1` |
| `Jwt__Audience` | `authenticated` |
| `Jwt__Secret` | `<jwt-secret>` (legacy HS256 projects) |
| `Cors__AllowedOrigins__0` | `https://your-frontend.example` |

Optional: `Supabase__AnonKey`, `Email__SmtpHost` / `Email__SmtpPort` / `Email__SmtpUser` /
`Email__SmtpPassword`, `Payments__Provider`. `ASPNETCORE_ENVIRONMENT` defaults to `Production`
in the image.

> ⚠️ Never commit real secrets. `appsettings.json` contains placeholders only; production values
> come from Railway variables, local values from user-secrets.

## Notes

- The database schema is managed in Supabase. DbUp runs any embedded `Migrations/*.sql` at startup
  (there are none yet, so it's a no-op) — apply the initial schema in Supabase directly.
- `AsUserAsync` runs SQL as the caller (RLS enforced) or the `anon` role for public reads;
  `AsServiceAsync` bypasses RLS for OTP, notifications, payment webhooks, and admin jobs.
