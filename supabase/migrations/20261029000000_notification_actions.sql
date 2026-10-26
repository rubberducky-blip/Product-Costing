-- Price change notifications: hiding (per user) and markup adjustment back to the previous price.
-- Only adds columns and functions, so the app that is already deployed keeps working.

alter table public.price_notifications add column hidden_by text[] not null default '{}';
-- set when the markup was adjusted so the item's price went back to old_price
alter table public.price_notifications add column adjusted_at timestamptz;

create or replace function public.hide_notifications(p_ids bigint[])
returns void
language sql
security invoker
set search_path = public
as $$
  update public.price_notifications
  set hidden_by = array_append(hidden_by, public.current_user_email()),
      seen_by = case when public.current_user_email() = any(seen_by) then seen_by
                     else array_append(seen_by, public.current_user_email()) end
  where id = any(p_ids)
    and public.current_user_email() is not null
    and not (public.current_user_email() = any(hidden_by))
$$;

-- p_changes: [{ "id": "<item id>", "markup_pct": 112.5, "price": 24.9, "notifications": [1, 2] }, …]
-- Sets each item's markup and stores its resulting price (so the change is not reported again), and marks
-- the given notifications as adjusted.
create or replace function public.adjust_markups(p_changes jsonb)
returns void
language plpgsql
security invoker
set search_path = public
as $$
declare
  c jsonb;
begin
  for c in select * from jsonb_array_elements(p_changes) loop
    update public.items set markup_pct = round((c ->> 'markup_pct')::numeric, 2)
    where id = (c ->> 'id')::uuid and kind <> 'raw_material';
    if not found then
      continue;
    end if;

    insert into public.item_prices (item_id, price) values ((c ->> 'id')::uuid, round((c ->> 'price')::numeric, 4))
    on conflict (item_id) do update set price = excluded.price, updated_at = now();

    update public.price_notifications set adjusted_at = now()
    where item_id = (c ->> 'id')::uuid
      and id in (select (x #>> '{}')::bigint from jsonb_array_elements(coalesce(c -> 'notifications', '[]'::jsonb)) as x);
  end loop;
end;
$$;

revoke execute on function public.hide_notifications(bigint[]) from public, anon;
revoke execute on function public.adjust_markups(jsonb) from public, anon;
grant execute on function public.hide_notifications(bigint[]) to authenticated;
grant execute on function public.adjust_markups(jsonb) to authenticated;
