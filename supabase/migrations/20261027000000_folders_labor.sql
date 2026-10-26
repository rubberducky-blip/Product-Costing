-- Folders for items and suppliers, and labor time as several entries (in seconds, with a comment).
--
-- Existing data is kept: each product's labor_minutes becomes one labor entry of labor_minutes × 60
-- seconds, then the labor_minutes column is dropped. Existing items and suppliers stay at the top
-- level (no folder).

-- ---------------------------------------------------------------------------
-- Folders: one tree per list (products, merchandise, raw materials, suppliers).
-- ---------------------------------------------------------------------------

create table public.folders (
  id uuid primary key default gen_random_uuid(),
  scope text not null check (scope in ('product', 'merchandise', 'raw_material', 'supplier')),
  parent_id uuid references public.folders (id),
  name text not null check (length(trim(name)) > 0),
  version int not null default 1,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  updated_by_email text,
  constraint folders_not_own_parent check (parent_id is distinct from id)
);

create unique index folders_name_key
  on public.folders (scope, coalesce(parent_id, '00000000-0000-0000-0000-000000000000'::uuid), lower(name));
create index folders_parent_idx on public.folders (parent_id);

create trigger folders_touch before insert or update on public.folders
  for each row execute function public.touch_row();

alter table public.items add column folder_id uuid references public.folders (id);
alter table public.suppliers add column folder_id uuid references public.folders (id);
create index items_folder_idx on public.items (folder_id);
create index suppliers_folder_idx on public.suppliers (folder_id);

-- ---------------------------------------------------------------------------
-- Labor entries: a product's labor time is the sum of its entries.
-- ---------------------------------------------------------------------------

create table public.labor_entries (
  id uuid primary key default gen_random_uuid(),
  product_id uuid not null references public.items (id) on delete cascade,
  seconds int not null check (seconds >= 0),
  comment text,
  position int not null default 0
);

create index labor_entries_product_idx on public.labor_entries (product_id);

insert into public.labor_entries (product_id, seconds, position)
select id, round(labor_minutes * 60)::int, 0
from public.items
where kind = 'product' and labor_minutes > 0;

drop function if exists public.save_item(jsonb, jsonb, int);
alter table public.items drop column labor_minutes;

-- ---------------------------------------------------------------------------
-- save_item: now also saves the folder and replaces the labor entries.
-- ---------------------------------------------------------------------------

create or replace function public.save_item(p_item jsonb, p_lines jsonb, p_labor jsonb, p_expected_version int)
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
begin
  r := jsonb_populate_record(null::public.items, p_item);

  -- a folder belongs to one list; drop it if it does not match the item type
  select f.id into v_folder from public.folders f where f.id = r.folder_id and f.scope = r.kind::text;

  if v_id is null then
    insert into public.items (
      code, name, kind, usable_as_material, folder_id, supplier_id, supplier_code, purchase_price, purchase_qty,
      unit, piece_length_cm, piece_area_cm2, discount_pct, shipping_pct, markup_pct, rounding_step, notes
    ) values (
      trim(r.code), coalesce(r.name, ''), r.kind, coalesce(r.usable_as_material, false), v_folder, r.supplier_id,
      r.supplier_code, coalesce(r.purchase_price, 0), coalesce(r.purchase_qty, 1), coalesce(r.unit, 'pcs'),
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

  return v_item;
end;
$$;

-- ---------------------------------------------------------------------------
-- Backups: format 2 adds folders and labor entries. Format 1 files still restore:
-- their labor_minutes become one labor entry per product.
-- ---------------------------------------------------------------------------

create or replace function public.backup_snapshot()
returns jsonb
language sql
stable
security definer
set search_path = public
as $$
  select jsonb_build_object(
    'format', 2,
    'app', 'product-costing',
    'created_at', now(),
    'app_settings', (select coalesce(jsonb_agg(to_jsonb(s)), '[]'::jsonb) from public.app_settings s),
    'folders', (select coalesce(jsonb_agg(to_jsonb(f)), '[]'::jsonb) from public.folders f),
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
  if p_data ->> 'app' is distinct from 'product-costing' or v_format is null or v_format not in (1, 2) then
    raise exception 'invalid_backup';
  end if;

  perform public.create_backup('pre_restore');

  delete from public.labor_entries where true;
  delete from public.recipe_lines where true;
  delete from public.items where true;
  delete from public.suppliers where true;
  delete from public.folders where true;

  insert into public.folders
  select * from jsonb_populate_recordset(null::public.folders, coalesce(p_data -> 'folders', '[]'::jsonb));

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

alter table public.folders enable row level security;
alter table public.labor_entries enable row level security;

create policy "authenticated all" on public.folders for all to authenticated using (true) with check (true);
create policy "authenticated all" on public.labor_entries for all to authenticated using (true) with check (true);

revoke execute on function public.save_item(jsonb, jsonb, jsonb, int) from public, anon;
grant execute on function public.save_item(jsonb, jsonb, jsonb, int) to authenticated;

do $$
begin
  alter publication supabase_realtime add table public.folders, public.labor_entries;
exception when others then
  raise notice 'realtime publication not configured: %', sqlerrm;
end;
$$;
