-- Product Costing — initial schema
-- Tables: app_settings (singleton), suppliers, items (products / merchandise / raw materials),
-- recipe_lines (bill of materials for products), backups (JSON snapshots).
--
-- Concurrency: every editable row has a `version` column that a trigger increments on each
-- update. Clients update with `where version = <version they loaded>`; zero affected rows
-- means somebody else saved first and the client shows a conflict dialog.

create extension if not exists pgcrypto;

-- ---------------------------------------------------------------------------
-- Helpers
-- ---------------------------------------------------------------------------

create or replace function public.current_user_email()
returns text
language sql
stable
as $$
  select nullif(current_setting('request.jwt.claims', true), '')::jsonb ->> 'email'
$$;

-- Bumps version/updated_at and records who made the change.
create or replace function public.touch_row()
returns trigger
language plpgsql
as $$
begin
  if tg_op = 'UPDATE' then
    new.version := old.version + 1;
  end if;
  new.updated_at := now();
  new.updated_by_email := coalesce(public.current_user_email(), new.updated_by_email);
  return new;
end;
$$;

-- ---------------------------------------------------------------------------
-- Settings (single row, id = 1)
-- ---------------------------------------------------------------------------

create table public.app_settings (
  id int primary key default 1 check (id = 1),
  labor_rate_per_hour numeric(12, 4) not null default 0 check (labor_rate_per_hour >= 0),
  overhead_rate_per_hour numeric(12, 4) not null default 0 check (overhead_rate_per_hour >= 0),
  default_markup_pct numeric(10, 2) not null default 100,
  rounding_step numeric(10, 4) not null default 0.05 check (rounding_step >= 0),
  rounding_mode text not null default 'nearest' check (rounding_mode in ('nearest', 'up', 'down')),
  currency text not null default '€',
  backup_retention_days int not null default 30 check (backup_retention_days > 0),
  version int not null default 1,
  updated_at timestamptz not null default now(),
  updated_by_email text
);

insert into public.app_settings (id) values (1);

create trigger app_settings_touch before insert or update on public.app_settings
  for each row execute function public.touch_row();

-- ---------------------------------------------------------------------------
-- Suppliers
-- ---------------------------------------------------------------------------

create table public.suppliers (
  id uuid primary key default gen_random_uuid(),
  name text not null check (length(trim(name)) > 0),
  country text,
  default_discount_pct numeric(6, 2) not null default 0 check (default_discount_pct between 0 and 100),
  shipping_pct numeric(8, 2) not null default 0 check (shipping_pct >= 0),
  email text,
  phone text,
  notes text,
  version int not null default 1,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  updated_by_email text
);

create unique index suppliers_name_key on public.suppliers (lower(name));

create trigger suppliers_touch before insert or update on public.suppliers
  for each row execute function public.touch_row();

-- ---------------------------------------------------------------------------
-- Items
--   product      = made in-house, has a recipe (recipe_lines) + labor minutes
--   merchandise  = bought and resold; may also be used as a raw material
--   raw_material = bought, used only to make products
-- ---------------------------------------------------------------------------

create type public.item_kind as enum ('product', 'merchandise', 'raw_material');
create type public.qty_unit as enum ('pcs', 'cm', 'cm2');

create table public.items (
  id uuid primary key default gen_random_uuid(),
  code text not null check (length(trim(code)) > 0),
  name text not null default '',
  kind public.item_kind not null,
  usable_as_material boolean not null default false,

  -- purchasing (merchandise / raw materials)
  supplier_id uuid references public.suppliers (id) on delete set null,
  supplier_code text,
  purchase_price numeric(14, 4) not null default 0 check (purchase_price >= 0),
  purchase_qty numeric(14, 4) not null default 1 check (purchase_qty > 0), -- units contained in one purchase
  unit public.qty_unit not null default 'pcs',                            -- default unit when used in recipes
  piece_length_cm numeric(14, 4) check (piece_length_cm > 0),             -- optional: converts pcs <-> cm
  piece_area_cm2 numeric(14, 4) check (piece_area_cm2 > 0),               -- optional: converts pcs <-> cm²
  discount_pct numeric(6, 2) check (discount_pct between 0 and 100),      -- null = supplier default
  shipping_pct numeric(8, 2) check (shipping_pct >= 0),                   -- null = supplier default

  -- manufacturing (products)
  labor_minutes numeric(10, 2) not null default 0 check (labor_minutes >= 0),

  -- pricing (products / merchandise)
  markup_pct numeric(10, 2),     -- null = settings default
  rounding_step numeric(10, 4) check (rounding_step >= 0), -- null = settings default

  notes text,
  version int not null default 1,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  updated_by_email text,

  constraint items_product_not_material check (kind <> 'product' or not usable_as_material),
  constraint items_raw_is_material check (kind <> 'raw_material' or usable_as_material)
);

create unique index items_code_key on public.items (lower(code));
create index items_kind_idx on public.items (kind);
create index items_supplier_idx on public.items (supplier_id);

create trigger items_touch before insert or update on public.items
  for each row execute function public.touch_row();

create table public.recipe_lines (
  id uuid primary key default gen_random_uuid(),
  product_id uuid not null references public.items (id) on delete cascade,
  material_id uuid not null references public.items (id) on delete restrict,
  quantity numeric(14, 4) not null check (quantity > 0),
  unit public.qty_unit not null,
  comment text,
  position int not null default 0
);

create index recipe_lines_product_idx on public.recipe_lines (product_id);
create index recipe_lines_material_idx on public.recipe_lines (material_id);

-- ---------------------------------------------------------------------------
-- save_item: insert/update an item and (for products) replace its recipe atomically,
-- with optimistic locking. Raises 'conflict' if p_expected_version is stale.
-- ---------------------------------------------------------------------------

create or replace function public.save_item(p_item jsonb, p_lines jsonb, p_expected_version int)
returns public.items
language plpgsql
security invoker
set search_path = public
as $$
declare
  r public.items;
  v_item public.items;
  v_id uuid := nullif(p_item ->> 'id', '')::uuid;
begin
  r := jsonb_populate_record(null::public.items, p_item);

  if v_id is null then
    insert into public.items (
      code, name, kind, usable_as_material, supplier_id, supplier_code, purchase_price, purchase_qty,
      unit, piece_length_cm, piece_area_cm2, discount_pct, shipping_pct, labor_minutes, markup_pct,
      rounding_step, notes
    ) values (
      trim(r.code), coalesce(r.name, ''), r.kind, coalesce(r.usable_as_material, false), r.supplier_id,
      r.supplier_code, coalesce(r.purchase_price, 0), coalesce(r.purchase_qty, 1), coalesce(r.unit, 'pcs'),
      r.piece_length_cm, r.piece_area_cm2, r.discount_pct, r.shipping_pct, coalesce(r.labor_minutes, 0),
      r.markup_pct, r.rounding_step, r.notes
    )
    returning * into v_item;
  else
    update public.items set
      code = trim(r.code),
      name = coalesce(r.name, ''),
      kind = r.kind,
      usable_as_material = coalesce(r.usable_as_material, false),
      supplier_id = r.supplier_id,
      supplier_code = r.supplier_code,
      purchase_price = coalesce(r.purchase_price, 0),
      purchase_qty = coalesce(r.purchase_qty, 1),
      unit = coalesce(r.unit, 'pcs'),
      piece_length_cm = r.piece_length_cm,
      piece_area_cm2 = r.piece_area_cm2,
      discount_pct = r.discount_pct,
      shipping_pct = r.shipping_pct,
      labor_minutes = coalesce(r.labor_minutes, 0),
      markup_pct = r.markup_pct,
      rounding_step = r.rounding_step,
      notes = r.notes
    where id = v_id and version = p_expected_version
    returning * into v_item;

    if not found then
      if exists (select 1 from public.items where id = v_id) then
        raise exception 'conflict';
      else
        raise exception 'not_found';
      end if;
    end if;

    -- an item that is no longer usable as a material must not be referenced by recipes
    if (v_item.kind = 'product' or not v_item.usable_as_material)
       and exists (select 1 from public.recipe_lines where material_id = v_item.id) then
      raise exception 'in_use';
    end if;
  end if;

  delete from public.recipe_lines where product_id = v_item.id;

  if v_item.kind = 'product' and p_lines is not null and jsonb_array_length(p_lines) > 0 then
    if exists (
      select 1
      from jsonb_to_recordset(p_lines) as l(material_id uuid)
      left join public.items m on m.id = l.material_id
      where m.id is null or m.kind = 'product' or not m.usable_as_material
    ) then
      raise exception 'invalid_material';
    end if;

    insert into public.recipe_lines (product_id, material_id, quantity, unit, comment, position)
    select v_item.id, (e.val ->> 'material_id')::uuid, (e.val ->> 'quantity')::numeric,
           (e.val ->> 'unit')::public.qty_unit, nullif(trim(e.val ->> 'comment'), ''), e.ord - 1
    from jsonb_array_elements(p_lines) with ordinality as e(val, ord);
  end if;

  return v_item;
end;
$$;

-- ---------------------------------------------------------------------------
-- Backups: JSON snapshots stored in the database, created daily by pg_cron
-- and on demand from the app. Restoring takes a 'pre_restore' snapshot first.
-- ---------------------------------------------------------------------------

create table public.backups (
  id bigint generated always as identity primary key,
  created_at timestamptz not null default now(),
  kind text not null check (kind in ('auto', 'manual', 'pre_restore')),
  created_by_email text,
  size_bytes int,
  data jsonb not null
);

create index backups_created_idx on public.backups (created_at desc);

create or replace function public.backup_snapshot()
returns jsonb
language sql
stable
security definer
set search_path = public
as $$
  select jsonb_build_object(
    'format', 1,
    'app', 'product-costing',
    'created_at', now(),
    'app_settings', (select coalesce(jsonb_agg(to_jsonb(s)), '[]'::jsonb) from public.app_settings s),
    'suppliers', (select coalesce(jsonb_agg(to_jsonb(s)), '[]'::jsonb) from public.suppliers s),
    'items', (select coalesce(jsonb_agg(to_jsonb(i)), '[]'::jsonb) from public.items i),
    'recipe_lines', (select coalesce(jsonb_agg(to_jsonb(l)), '[]'::jsonb) from public.recipe_lines l)
  )
$$;

create or replace function public.create_backup(p_kind text default 'manual')
returns bigint
language plpgsql
security definer
set search_path = public
as $$
declare
  v_data jsonb := public.backup_snapshot();
  v_id bigint;
  v_retention int;
begin
  insert into public.backups (kind, created_by_email, size_bytes, data)
  values (p_kind, public.current_user_email(), octet_length(v_data::text), v_data)
  returning id into v_id;

  select backup_retention_days into v_retention from public.app_settings where id = 1;
  delete from public.backups
  where kind in ('auto', 'pre_restore')
    and created_at < now() - make_interval(days => coalesce(v_retention, 30));

  return v_id;
end;
$$;

create or replace function public.restore_backup_data(p_data jsonb)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
  if p_data ->> 'app' is distinct from 'product-costing' or (p_data ->> 'format')::int is distinct from 1 then
    raise exception 'invalid_backup';
  end if;

  perform public.create_backup('pre_restore');

  delete from public.recipe_lines where true;
  delete from public.items where true;
  delete from public.suppliers where true;

  insert into public.suppliers
  select * from jsonb_populate_recordset(null::public.suppliers, p_data -> 'suppliers');

  insert into public.items
  select * from jsonb_populate_recordset(null::public.items, p_data -> 'items');

  insert into public.recipe_lines
  select * from jsonb_populate_recordset(null::public.recipe_lines, p_data -> 'recipe_lines');

  if jsonb_array_length(coalesce(p_data -> 'app_settings', '[]'::jsonb)) > 0 then
    delete from public.app_settings where true;
    insert into public.app_settings
    select * from jsonb_populate_recordset(null::public.app_settings, p_data -> 'app_settings');
  end if;
end;
$$;

create or replace function public.restore_backup(p_id bigint)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
  v_data jsonb;
begin
  select data into v_data from public.backups where id = p_id;
  if v_data is null then
    raise exception 'not_found';
  end if;
  perform public.restore_backup_data(v_data);
end;
$$;

-- ---------------------------------------------------------------------------
-- Row level security: only signed-in users can read or write anything.
-- (Disable public sign-ups in Supabase → Authentication → Providers → Email.)
-- ---------------------------------------------------------------------------

alter table public.app_settings enable row level security;
alter table public.suppliers enable row level security;
alter table public.items enable row level security;
alter table public.recipe_lines enable row level security;
alter table public.backups enable row level security;

create policy "authenticated read" on public.app_settings for select to authenticated using (true);
create policy "authenticated update" on public.app_settings for update to authenticated using (true) with check (true);

create policy "authenticated all" on public.suppliers for all to authenticated using (true) with check (true);
create policy "authenticated all" on public.items for all to authenticated using (true) with check (true);
create policy "authenticated all" on public.recipe_lines for all to authenticated using (true) with check (true);

create policy "authenticated read" on public.backups for select to authenticated using (true);
create policy "authenticated delete" on public.backups for delete to authenticated using (true);

revoke execute on function public.backup_snapshot() from public, anon;
revoke execute on function public.create_backup(text) from public, anon;
revoke execute on function public.restore_backup_data(jsonb) from public, anon;
revoke execute on function public.restore_backup(bigint) from public, anon;
revoke execute on function public.save_item(jsonb, jsonb, int) from public, anon;
grant execute on function public.backup_snapshot() to authenticated;
grant execute on function public.create_backup(text) to authenticated;
grant execute on function public.restore_backup_data(jsonb) to authenticated;
grant execute on function public.restore_backup(bigint) to authenticated;
grant execute on function public.save_item(jsonb, jsonb, int) to authenticated;

-- ---------------------------------------------------------------------------
-- Realtime: clients refresh live when another user saves.
-- ---------------------------------------------------------------------------

do $$
begin
  alter publication supabase_realtime add table public.app_settings, public.suppliers, public.items, public.recipe_lines;
exception when others then
  raise notice 'realtime publication not configured: %', sqlerrm;
end;
$$;

-- ---------------------------------------------------------------------------
-- Daily automatic backup at 02:00 UTC (pg_cron).
-- ---------------------------------------------------------------------------

do $$
begin
  create extension if not exists pg_cron;
  perform cron.schedule('product-costing-daily-backup', '0 2 * * *', 'select public.create_backup(''auto'')');
exception when others then
  raise notice 'pg_cron not available, automatic backups not scheduled: %', sqlerrm;
end;
$$;
