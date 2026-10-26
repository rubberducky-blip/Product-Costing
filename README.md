# Product Costing / Κοστολόγηση

Web app for calculating product cost, price and margin from raw materials, labor time and supplier terms.
It works on desktop and mobile, uses a dark theme, and is available in Greek and English. Data lives in Supabase.

## Features

- **Suppliers**: country, default discount %, shipping % (shipping as a share of each product's cost, e.g. 14%).
- **Three item types**
  - **Products (Προϊόντα)**: made in-house. Each one has a recipe of raw materials (quantity in pcs / cm / cm², with an optional comment) plus labor minutes.
  - **Merchandise (Εμπορεύματα)**: bought and resold. Each one is linked to a supplier, with an optional custom discount or shipping %. It can also be flagged *also used as raw material*.
  - **Raw materials (Πρώτες ύλες)**: bought and used in recipes. Each one has a default unit, and an optional piece length/area so recipes can use a different unit.
- **Cost**
  - purchase price × (1 − discount) × (1 + shipping) ÷ quantity per purchase
  - products: Σ materials + minutes × (labor rate + production overhead rate)
- **Pricing**: markup per item (or the default from settings) → final price, profit and margin. Prices are rounded to 0.05 by default. The rounding step can be changed per item or globally, and rounding can go to nearest, up or down.
- **Global search** across item codes, names, supplier codes and suppliers.
- **Backups**: a daily automatic snapshot (pg_cron) plus manual backups in Settings. Backups can be downloaded as JSON, and you can restore from a stored backup or from a file. A safety snapshot is always taken before a restore. An optional weekly off-site dump runs through GitHub Actions.
- **Password-only login** through Supabase Auth. Every table is protected by Row Level Security.
- **Protection against conflicting edits**
  - *Optimistic locking*: every record has a `version`. A save that was based on an outdated version is rejected, and the user chooses to **load the other version** or **overwrite** it.
  - *Live presence*: a banner shows when another user has the same record open.
  - *Live updates*: lists refresh in real time when someone saves. An open form follows the changes until you start editing; after that it warns you that a newer version exists.

## Setup

### 1. Database (Supabase)

The schema is in `supabase/migrations/`.

- With the Supabase GitHub integration, the migration is applied when the branch is merged.
- Otherwise, paste `supabase/migrations/20261026000000_init.sql` into **Supabase → SQL Editor** and run it.

Then in the Supabase dashboard:

1. **Authentication → Sign In / Providers → Email**: turn **off** "Allow new users to sign up". Only users you create can log in.
2. **Authentication → Users → Add user**: create each user with an email and password (tick *Auto confirm*).
3. **Database → Extensions**: make sure **pg_cron** is enabled. The migration enables it and schedules the daily backup when allowed. If it wasn't scheduled, run this once:
   ```sql
   select cron.schedule('product-costing-daily-backup', '0 2 * * *', 'select public.create_backup(''auto'')');
   ```
4. Optional: on paid plans Supabase also takes its own daily backups and point-in-time recovery (Database → Backups).

### 2. Front-end

```bash
cp .env.example .env    # fill in Project URL and anon key (Supabase → Project Settings → API)
npm install
npm run dev             # http://localhost:5173
npm test                # cost calculation tests
npm run build           # static site in dist/
```

### 3. Hosting

The build is a static site, so any static host works.

- **Vercel / Netlify** (free, works with private repos): import the repo, use build command `npm run build` and output folder `dist`, and add the env vars `VITE_SUPABASE_URL` and `VITE_SUPABASE_ANON_KEY`.
- **GitHub Pages**: `.github/workflows/deploy.yml` deploys on every push to `main`. Add the two values above as repository secrets and set Settings → Pages → Source = *GitHub Actions*. Private repos need a paid GitHub plan for Pages.

On a phone, open the site and use **Add to Home Screen** to get an app-like icon.

### 4. Off-site backup (optional)

Add the repository secret `SUPABASE_DB_URL` (Supabase → Connect → Session pooler URI, with your DB password). `.github/workflows/backup.yml` then dumps the database weekly and keeps each dump as a workflow artifact for 90 days.

## Project layout

```
supabase/migrations/   schema, RLS, save_item RPC (optimistic locking), backups, pg_cron
src/lib/calc.ts        all cost / price / rounding logic (+ calc.test.ts)
src/lib/data.tsx       loads data, realtime refresh
src/lib/useVersionedForm.ts, presence.ts   concurrent-edit protection
src/lib/i18n.tsx       Greek / English texts
src/pages/             lists, item editor (recipe), suppliers, settings & backups
```
