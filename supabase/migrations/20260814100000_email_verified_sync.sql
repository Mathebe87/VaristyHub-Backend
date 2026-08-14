-- =============================================================================
-- Keep public.profiles.email_verified in sync with Supabase Auth confirmation.
-- Run in Supabase (SQL editor or CLI). Idempotent.
-- =============================================================================
create or replace function public.sync_email_verified()
returns trigger language plpgsql security definer set search_path = public as $$
begin
  update public.profiles
    set email_verified = (new.email_confirmed_at is not null)
    where id = new.id;
  return new;
end $$;

drop trigger if exists on_auth_email_confirmed on auth.users;
create trigger on_auth_email_confirmed
  after update of email_confirmed_at on auth.users
  for each row execute function public.sync_email_verified();
