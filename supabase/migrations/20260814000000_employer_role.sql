-- =============================================================================
-- Employer role + employers table + job-application RLS for employers
-- -----------------------------------------------------------------------------
-- STEP 1 and STEP 2 below can be run together in the Supabase SQL editor (STEP 2
-- does not reference the new enum literal), but if you hit "unsafe use of new
-- value 'employer'", run STEP 1 on its own first, then STEP 2.
-- =============================================================================

-- ---- STEP 1: add the enum value (idempotent) --------------------------------
alter type public.user_role add value if not exists 'employer';

-- ---- STEP 2: employers table + policies -------------------------------------
create table if not exists public.employers (
  id           uuid primary key references public.profiles(id) on delete cascade,
  company_name text not null default '',
  website      text,
  logo_url     text,
  is_verified  boolean not null default false,
  created_at   timestamptz not null default now(),
  updated_at   timestamptz not null default now()
);

alter table public.employers enable row level security;

drop trigger if exists trg_employers_updated on public.employers;
create trigger trg_employers_updated before update on public.employers
  for each row execute function public.set_updated_at();

drop policy if exists "employers: self read" on public.employers;
create policy "employers: self read" on public.employers
  for select using (id = auth.uid() or public.is_super_admin());

drop policy if exists "employers: self write" on public.employers;
create policy "employers: self write" on public.employers
  for all using (id = auth.uid() or public.is_super_admin())
  with check (id = auth.uid() or public.is_super_admin());

-- Employers can read + update applications to jobs they posted.
-- (jobs: admin write already permits posted_by = auth.uid() for the jobs themselves.)
drop policy if exists "job_apps: employer view" on public.job_applications;
create policy "job_apps: employer view" on public.job_applications
  for select using (exists (
    select 1 from public.jobs j where j.id = job_id and j.posted_by = auth.uid()));

drop policy if exists "job_apps: employer update" on public.job_applications;
create policy "job_apps: employer update" on public.job_applications
  for update using (exists (
    select 1 from public.jobs j where j.id = job_id and j.posted_by = auth.uid()))
  with check (exists (
    select 1 from public.jobs j where j.id = job_id and j.posted_by = auth.uid()));
