## 🧠 Momentum – Habit Tracking & Analytics Platform

A full-stack habit tracking application that helps users build consistency through daily habit tracking, calendar-based logging, and visual analytics.
Built with React (Material UI) on the frontend and ASP.NET Core Web API (.NET 8) on the backend.

> See [ARCHITECTURE_AND_ROADMAP.md](ARCHITECTURE_AND_ROADMAP.md) for the canonical architecture, fix log, and future roadmap.

## 🖥️ Tech Stack
### 🖼️ Frontend (Client UI)
| Category           | Technology                |
| ------------------ | ------------------------- |
| Language           | JavaScript (JSX)          |
| Framework          | React 19                  |
| UI Library         | Material UI (MUI 7)       |
| Charting           | ApexCharts                |
| Routing            | React Router 7            |
| State Management   | React Context + Hooks     |
| Auth Storage       | Access token in memory; refresh token in HttpOnly cookie |
| Build Tool         | Vite                      |
| Environment Config | `.env`, `.env.production` |

### 🧠 Backend (Server API)
| Category         | Technology                                  |
| ---------------- | ------------------------------------------- |
| Language         | C#                                          |
| Framework        | ASP.NET Core Web API                        |
| Runtime          | .NET 8                                      |
| Architecture     | Controller → Service → Repository           |
| API Style        | REST (uniform `ApiResponse` envelope)       |
| Auth             | JWT Bearer (1 h) + opaque refresh token (7 d, SHA-256 hashed, HttpOnly cookie) |
| Validation       | FluentValidation                            |
| Persistence      | Entity Framework Core 9 (SQL Server)        |
| Configuration    | `appsettings.json` + `IOptions<>` binding   |
| Cross-cutting    | Global exception middleware                 |
| Containerization | Docker (planned)                            |
| CI/CD            | Jenkins / GitHub Actions (planned)          |

## 📁 Project Structure

### 🖥 Frontend (`client-ui/`)
```
client-ui/
├── public/
├── src/
│   ├── api/                # axios instance with interceptors
│   ├── assets/             # images, icons, static assets
│   ├── components/         # reusable UI components, ProtectedRoute, PublicRoute
│   ├── context/            # AuthContext, HabitContext, HabitTrackingContext, SnackbarContext
│   ├── layouts/            # FullLayout, BlankLayout, header, sidebar, footer
│   ├── routes/             # Router (createBrowserRouter, lazy + Suspense)
│   ├── theme/              # MUI theme overrides
│   ├── utils/              # tokenUtils (in-memory access token store), cookieUtils
│   ├── views/
│   │   ├── authentication/ # Login, Register, ForgotPassword, Error
│   │   ├── dashboard/      # KPIs, charts, heatmap
│   │   ├── habit/          # CRUD, tracking dialog, calendar
│   │   ├── stats/          # Statistics & analytics
│   │   └── settings/       # User & app settings
│   ├── App.jsx
│   └── main.jsx
├── .env
├── .env.production
└── package.json
```

### 🧠 Backend (`server/AtomicHabits/`)
```
server/AtomicHabits/
├── Config/                 # JwtOptions, AppOptions, CorsOptions
├── Controllers/            # AuthController, HabitController, HabitTrackingController, DashboardController
├── Data/                   # AppDbContext, AppDbContextFactory, ConnectionFactory, DbSeeder
├── Middleware/             # GlobalExceptionMiddleware
├── Migrations/             # EF Core migrations
├── Models/                 # User, Role, Permission, Habit, HabitTracking, Streak, RefreshToken, …
│   └── DTO/                # request / response contracts
├── Repositories/           # UserRepositories, HabitRepositories, HabitTrackingRepositories, StreakRepositories, DashboardRepositories
├── Services/               # AuthService, TokenService, HabitService, HabitTrackingService, DashboardService, EmailService
├── Utils/                  # StreakCalculator, ClaimsPrincipalExtensions
├── Validators/             # FluentValidation validators (Auth, Habit)
├── Program.cs              # composition root: DI, options, JWT, CORS, Swagger, middleware pipeline
├── appsettings.json
└── appsettings.Development.json
```

## 🔐 Configuration

The backend reads configuration from `appsettings.json` (overridable via environment variables).

```json
{
  "ConnectionStrings": { "DbConn": "..." },
  "Jwt": {
    "Issuer": "DailyHabitTracker",
    "Audience": "DailyHabitTracker",
    "AccessTokenMinutes": 60,
    "RefreshTokenDays": 7
  },
  "App": {
    "WebBaseUrl": "http://localhost:5173",
    "ResetPasswordPath": "/auth/reset-password"
  },
  "Cors": {
    "AllowedOrigins": [ "http://localhost:5173", "https://localhost:5173" ]
  }
}
```

Required environment variables:

- `JWT_SECRET` — symmetric signing key (mandatory)
- `JWT_ISSUER`, `JWT_AUDIENCE` — optional, override `Jwt:Issuer` / `Jwt:Audience`

## 🚀 Getting started

### Prerequisites
- .NET 8 SDK
- Node.js 20+
- SQL Server (local or container)

### Backend
```bash
cd server/AtomicHabits
export JWT_SECRET="<a-long-random-string>"
dotnet ef database update
dotnet run
```

### Frontend
```bash
cd client-ui
npm install
npm run dev
```

## ✨ Key Features

### 🔐 Authentication & Access Control
- User registration and login (BCrypt password hashing)
- Access token in JS memory; refresh token in `HttpOnly; Secure; SameSite=Strict` cookie
- Refresh-token rotation with revocation tracked in DB
- Forgot / reset password via email link
- RBAC tables in place (Users / Roles / Permissions / Modules) — enforcement is on the roadmap

### 📊 Dashboard
- Today / streak / weekly cards
- Monthly completion-rate trend
- Heatmap calendar (currently static — endpoint on roadmap)

### ✅ Habit Management
- Create / update / delete habits
- Goal config: value, unit, frequency (daily / weekly / monthly / yearly)
- Habit reminders schema in place (controller / scheduler on roadmap)

### 📅 Habit Tracking
- Per-day tracking with optional notes and time spent
- Calendar-based visualization
- Daily-submit endpoint with duplicate-day protection

### 📈 Statistics & Analytics
- Habit summary (today / weekly / monthly / health score) — per-habit `GoalFrequency`-aware
- Per-habit stats: current/longest streak, completion rate, monthly goal
- Distribution charts: weekly, monthly, yearly

### 🎨 UI & UX
- Material UI design system
- Lazy-loaded routes with Suspense fallback
- Snackbar notifications
- Responsive dashboard layout
- Error pages (404, fallback states)

## 🗺️ Roadmap

See [ARCHITECTURE_AND_ROADMAP.md](ARCHITECTURE_AND_ROADMAP.md) for the full roadmap — the next batch of work focuses on:
- Reminder scheduler (background service)
- Real RBAC enforcement
- 2FA (TOTP)
- Settings persistence (`UserPreferences`)
- Heatmap endpoint
- PWA + push notifications
