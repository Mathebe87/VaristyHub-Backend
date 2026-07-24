-- Role-dashboard schema additions (companion to the role-based backend spec).
-- Additive and idempotent; safe to run against an existing database.

-- 7a. Platform settings for /admin-settings (key/value)
create table if not exists public.app_settings (
  key         text primary key,
  value       jsonb not null default '{}'::jsonb,
  updated_by  uuid references public.profiles(id) on delete set null,
  updated_at  timestamptz not null default now()
);

alter table public.app_settings enable row level security;

drop policy if exists "app_settings: admin read" on public.app_settings;
create policy "app_settings: admin read" on public.app_settings
  for select using (public.is_super_admin());

drop policy if exists "app_settings: admin write" on public.app_settings;
create policy "app_settings: admin write" on public.app_settings
  for all using (public.is_super_admin()) with check (public.is_super_admin());

-- 7b. Application funnel view for report aggregates (security_invoker → respects caller RLS)
create or replace view public.application_funnel with (security_invoker = true) as
  select university_id, status, count(*) as n
  from public.applications
  group by university_id, status;
