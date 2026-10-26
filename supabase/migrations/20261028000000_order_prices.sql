-- Manual ordering, sort mode per folder, raw-material folder for merchandise, and price change notifications.
-- Nothing existing is changed or removed: every list keeps sorting by code until a manual order is chosen.

-- ---------------------------------------------------------------------------
-- Sorting: each folder (and the top level of each list) sorts by code or manually.
-- ---------------------------------------------------------------------------

alter table public.folders add column sort_mode text not null default 'code' check (sort_mode in ('code', 'manual'));

-- the top level of each list is not a folder, so its sort mode lives here
create table public.list_settings (
  scope text primary key check (scope in ('product', 'merchandise', 'raw_material', 'supplier')),
  sort_mode text not null default 'code' check (sort_mode in ('code', 'manual'))
);
insert into public.list_settings (scope) values ('product'), ('merchandise'), ('raw_material'), ('supplier');

-- Manual position of an item, supplier or folder inside a list. Kept apart from the records so that
-- re-arranging does not count as editing them (no version bump, no "someone changed this" warnings).
create table public.manual_order (
  scope text not null check (scope in ('product', 'merchandise', 'raw_material', 'supplier')),
  id uuid not null,
  position int not null,
  primary key (scope, id)
);

create or replace function public.set_manual_order(p_scope text, p_ids uuid[])
returns void
language sql
security invoker
set search_path = public
as $$
  insert into public.manual_order (scope, id, position)
  select p_scope, x.id, x.ord::int from unnest(p_ids) with ordinality as x(id, ord)
  on conflict (scope, id) do update set position = excluded.position
$$;

-- ---------------------------------------------------------------------------
-- Merchandise used as raw material gets its own place in the raw-materials folder tree.
-- ---------------------------------------------------------------------------

alter table public.items add column material_folder_id uuid references public.folders (id);
create index items_material_folder_idx on public.items (material_folder_id);

create or replace function public.save_item(p_item jsonb, p_lines jsonb, p_labor jsonb, p_expected_version int)
returns public.items
language plpgsql
security invoker
set search_path = public
as $$
begin
  return public.save_item(p_item, p_lines, p_labor, p_expected_version, null);
end;
$$;

-- p_price: the item's price after this save, so the change is not reported as caused by something else
create or replace function public.save_item(p_item jsonb, p_lines jsonb, p_labor jsonb, p_expected_version int, p_price numeric)
returns public.items
language plpgsql
security invoker
set search_path = public
as $$
declare
  r public.items;
  v_item public.items;
  v_id uuid := nullif(p_item ->> 'id', '')::uuid;
  v_folder uuid;
  v_material_folder uuid;
begin
  r := jsonb_populate_record(null::public.items, p_item);

  -- a folder belongs to one list; drop it if it does not match the item type
  select f.id into v_folder from public.folders f where f.id = r.folder_id and f.scope = r.kind::text;
  if r.kind = 'merchandise' and coalesce(r.usable_as_material, false) then
    select f.id into v_material_folder from public.folders f where f.id = r.material_folder_id and f.scope = 'raw_material';
  end if;

  if v_id is null then
    insert into public.items (
      code, name, kind, usable_as_material, folder_id, material_folder_id, supplier_id, supplier_code, purchase_price,
      purchase_qty, unit, piece_length_cm, piece_area_cm2, discount_pct, shipping_pct, markup_pct, rounding_step, notes
    ) values (
      trim(r.code), coalesce(r.name, ''), r.kind, coalesce(r.usable_as_material, false), v_folder, v_material_folder,
      r.supplier_id, r.supplier_code, coalesce(r.purchase_price, 0), coalesce(r.purchase_qty, 1), coalesce(r.unit, 'pcs'),
      r.piece_length_cm, r.piece_area_cm2, r.discount_pct, r.shipping_pct, r.markup_pct, r.rounding_step, r.notes
    )
    returning * into v_item;
  else
    update public.items set
      code = trim(r.code),
      name = coalesce(r.name, ''),
      kind = r.kind,
      usable_as_material = coalesce(r.usable_as_material, false),
      folder_id = v_folder,
      material_folder_id = v_material_folder,
      supplier_id = r.supplier_id,
      supplier_code = r.supplier_code,
      purchase_price = coalesce(r.purchase_price, 0),
      purchase_qty = coalesce(r.purchase_qty, 1),
      unit = coalesce(r.unit, 'pcs'),
      piece_length_cm = r.piece_length_cm,
      piece_area_cm2 = r.piece_area_cm2,
      discount_pct = r.discount_pct,
      shipping_pct = r.shipping_pct,
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
  delete from public.labor_entries where product_id = v_item.id;

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

  if v_item.kind = 'product' and p_labor is not null and jsonb_array_length(p_labor) > 0 then
    insert into public.labor_entries (product_id, seconds, comment, position)
    select v_item.id, round((e.val ->> 'seconds')::numeric)::int, nullif(trim(e.val ->> 'comment'), ''), e.ord - 1
    from jsonb_array_elements(p_labor) with ordinality as e(val, ord);
  end if;

  if p_price is not null then
    insert into public.item_prices (item_id, price) values (v_item.id, round(p_price, 4))
    on conflict (item_id) do update set price = excluded.price, updated_at = now();
  end if;

  return v_item;
end;
$$;

-- ---------------------------------------------------------------------------
-- Price change notifications.
-- Prices are computed in the app. Each app reports the prices it computes; when a stored price differs,
-- a notification is recorded (old → new). An item's own save stores its new price directly (save_item),
-- so only changes caused by something else (labor rate, a material, a supplier discount…) are reported.
-- ---------------------------------------------------------------------------

create table public.item_prices (
  item_id uuid primary key references public.items (id) on delete cascade,
  price numeric(14, 4) not null,
  updated_at timestamptz not null default now()
);

create table public.price_notifications (
  id bigint generated always as identity primary key,
  item_id uuid not null references public.items (id) on delete cascade,
  old_price numeric(14, 4) not null,
  new_price numeric(14, 4) not null,
  created_at timestamptz not null default now(),
  seen_by text[] not null default '{}'
);

create index price_notifications_created_idx on public.price_notifications (created_at desc);
create index price_notifications_item_idx on public.price_notifications (item_id);

-- p_prices: [{ "id": "<item id>", "price": 12.5 }, …]
create or replace function public.record_prices(p_prices jsonb)
returns void
language plpgsql
security invoker
set search_path = public
as $$
begin
  with input as (
    select (e ->> 'id')::uuid as id, round((e ->> 'price')::numeric, 4) as price
    from jsonb_array_elements(p_prices) as e
    where exists (select 1 from public.items i where i.id = (e ->> 'id')::uuid)
  ),
  changed as (
    update public.item_prices ip
    set price = i.price, updated_at = now()
    from input i, public.item_prices old
    where ip.item_id = i.id and old.item_id = ip.item_id and ip.price <> i.price
    returning ip.item_id, old.price as old_price, i.price as new_price
  )
  insert into public.price_notifications (item_id, old_price, new_price)
  select item_id, old_price, new_price from changed;

  -- first time an item is seen: remember its price without a notification
  insert into public.item_prices (item_id, price)
  select (e ->> 'id')::uuid, round((e ->> 'price')::numeric, 4)
  from jsonb_array_elements(p_prices) as e
  where exists (select 1 from public.items i where i.id = (e ->> 'id')::uuid)
  on conflict (item_id) do nothing;

  delete from public.price_notifications where created_at < now() - interval '90 days';
end;
$$;

create or replace function public.mark_notifications_seen(p_ids bigint[])
returns void
language sql
security invoker
set search_path = public
as $$
  update public.price_notifications
  set seen_by = array_append(seen_by, public.current_user_email())
  where id = any(p_ids)
    and public.current_user_email() is not null
    and not (public.current_user_email() = any(seen_by))
$$;

-- ---------------------------------------------------------------------------
-- Backups: format 3 adds the sort settings and manual order.
-- (Stored prices and notifications are not backed up; they are rebuilt after a restore.)
-- ---------------------------------------------------------------------------

create or replace function public.backup_snapshot()
returns jsonb
language sql
stable
security definer
set search_path = public
as $$
  select jsonb_build_object(
    'format', 3,
    'app', 'product-costing',
    'created_at', now(),
    'app_settings', (select coalesce(jsonb_agg(to_jsonb(s)), '[]'::jsonb) from public.app_settings s),
    'list_settings', (select coalesce(jsonb_agg(to_jsonb(s)), '[]'::jsonb) from public.list_settings s),
    'folders', (select coalesce(jsonb_agg(to_jsonb(f)), '[]'::jsonb) from public.folders f),
    'manual_order', (select coalesce(jsonb_agg(to_jsonb(o)), '[]'::jsonb) from public.manual_order o),
    'suppliers', (select coalesce(jsonb_agg(to_jsonb(s)), '[]'::jsonb) from public.suppliers s),
    'items', (select coalesce(jsonb_agg(to_jsonb(i)), '[]'::jsonb) from public.items i),
    'recipe_lines', (select coalesce(jsonb_agg(to_jsonb(l)), '[]'::jsonb) from public.recipe_lines l),
    'labor_entries', (select coalesce(jsonb_agg(to_jsonb(l)), '[]'::jsonb) from public.labor_entries l)
  )
$$;

create or replace function public.restore_backup_data(p_data jsonb)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
  v_format int := (p_data ->> 'format')::int;
begin
  if p_data ->> 'app' is distinct from 'product-costing' or v_format is null or v_format not in (1, 2, 3) then
    raise exception 'invalid_backup';
  end if;

  perform public.create_backup('pre_restore');

  delete from public.labor_entries where true;
  delete from public.recipe_lines where true;
  delete from public.items where true;
  delete from public.suppliers where true;
  delete from public.folders where true;
  delete from public.manual_order where true;

  insert into public.folders
  select * from jsonb_populate_recordset(null::public.folders, coalesce(p_data -> 'folders', '[]'::jsonb));

  insert into public.manual_order
  select * from jsonb_populate_recordset(null::public.manual_order, coalesce(p_data -> 'manual_order', '[]'::jsonb));

  update public.list_settings ls set sort_mode = s.sort_mode
  from jsonb_populate_recordset(null::public.list_settings, coalesce(p_data -> 'list_settings', '[]'::jsonb)) s
  where ls.scope = s.scope;

  insert into public.suppliers
  select * from jsonb_populate_recordset(null::public.suppliers, p_data -> 'suppliers');

  insert into public.items
  select * from jsonb_populate_recordset(null::public.items, p_data -> 'items');

  insert into public.recipe_lines
  select * from jsonb_populate_recordset(null::public.recipe_lines, p_data -> 'recipe_lines');

  if v_format = 1 then
    insert into public.labor_entries (product_id, seconds, position)
    select (e ->> 'id')::uuid, round((e ->> 'labor_minutes')::numeric * 60)::int, 0
    from jsonb_array_elements(p_data -> 'items') as e
    where e ->> 'kind' = 'product' and coalesce((e ->> 'labor_minutes')::numeric, 0) > 0;
  else
    insert into public.labor_entries
    select * from jsonb_populate_recordset(null::public.labor_entries, coalesce(p_data -> 'labor_entries', '[]'::jsonb));
  end if;

  if jsonb_array_length(coalesce(p_data -> 'app_settings', '[]'::jsonb)) > 0 then
    delete from public.app_settings where true;
    insert into public.app_settings
    select * from jsonb_populate_recordset(null::public.app_settings, p_data -> 'app_settings');
  end if;
end;
$$;

-- ---------------------------------------------------------------------------
-- Security and realtime for the new tables.
-- ---------------------------------------------------------------------------

alter table public.list_settings enable row level security;
alter table public.manual_order enable row level security;
alter table public.item_prices enable row level security;
alter table public.price_notifications enable row level security;

create policy "authenticated all" on public.list_settings for all to authenticated using (true) with check (true);
create policy "authenticated all" on public.manual_order for all to authenticated using (true) with check (true);
create policy "authenticated all" on public.item_prices for all to authenticated using (true) with check (true);
create policy "authenticated all" on public.price_notifications for all to authenticated using (true) with check (true);

revoke execute on function public.save_item(jsonb, jsonb, jsonb, int, numeric) from public, anon;
revoke execute on function public.set_manual_order(text, uuid[]) from public, anon;
revoke execute on function public.record_prices(jsonb) from public, anon;
revoke execute on function public.mark_notifications_seen(bigint[]) from public, anon;
grant execute on function public.save_item(jsonb, jsonb, jsonb, int, numeric) to authenticated;
grant execute on function public.set_manual_order(text, uuid[]) to authenticated;
grant execute on function public.record_prices(jsonb) to authenticated;
grant execute on function public.mark_notifications_seen(bigint[]) to authenticated;

do $$
begin
  alter publication supabase_realtime add table public.list_settings, public.manual_order, public.price_notifications;
exception when others then
  raise notice 'realtime publication not configured: %', sqlerrm;
end;
$$;
