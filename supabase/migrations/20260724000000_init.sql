-- =============================================================================
-- Varsity Hub · Full database schema for Supabase (PostgreSQL) — IDEMPOTENT
-- -----------------------------------------------------------------------------
-- Re-runnable: enums are guarded, tables use IF NOT EXISTS, functions/views use
-- CREATE OR REPLACE, policies/triggers are dropped-if-exists then created.
-- Safe to run repeatedly in the Supabase SQL editor or via CLI.
--
-- NOTE: IF NOT EXISTS on tables means an EXISTING table is left untouched — this
-- script will NOT alter columns on tables that already exist. Use it to create a
-- fresh database or to re-assert functions/views/policies.
-- =============================================================================

create extension if not exists "pgcrypto";
create extension if not exists "citext";

-- =============================================================================
-- 1. ENUMS  (guarded — CREATE TYPE has no IF NOT EXISTS)
-- =============================================================================
do $$
begin
  if not exists (select 1 from pg_type where typname = 'user_role') then
    create type user_role as enum ('student','counsellor','parent','university_admin','super_admin'); end if;
  if not exists (select 1 from pg_type where typname = 'student_type') then
    create type student_type as enum ('sa','international'); end if;
  if not exists (select 1 from pg_type where typname = 'gender_type') then
    create type gender_type as enum ('male','female','other','undisclosed'); end if;
  if not exists (select 1 from pg_type where typname = 'application_status') then
    create type application_status as enum ('draft','submitted','under_review','pending_documents','approved','waitlisted','rejected','withdrawn'); end if;
  if not exists (select 1 from pg_type where typname = 'payment_status') then
    create type payment_status as enum ('pending','paid','failed','refunded'); end if;
  if not exists (select 1 from pg_type where typname = 'payment_method') then
    create type payment_method as enum ('card','eft','wallet'); end if;
  if not exists (select 1 from pg_type where typname = 'document_type') then
    create type document_type as enum ('id','passport','matric_certificate','results','proof_of_residence','study_permit','cv','other'); end if;
  if not exists (select 1 from pg_type where typname = 'otp_channel') then
    create type otp_channel as enum ('email','sms'); end if;
  if not exists (select 1 from pg_type where typname = 'otp_purpose') then
    create type otp_purpose as enum ('registration','login','password_reset'); end if;
  if not exists (select 1 from pg_type where typname = 'job_type') then
    create type job_type as enum ('internship','graduate_programme','part_time'); end if;
  if not exists (select 1 from pg_type where typname = 'job_app_status') then
    create type job_app_status as enum ('applied','viewed','interview','offer','rejected','withdrawn'); end if;
  if not exists (select 1 from pg_type where typname = 'bursary_field') then
    create type bursary_field as enum ('engineering','it_science','commerce','health','education','law','arts','other'); end if;
  if not exists (select 1 from pg_type where typname = 'bursary_app_status') then
    create type bursary_app_status as enum ('draft','submitted','under_review','approved','rejected'); end if;
  if not exists (select 1 from pg_type where typname = 'listing_category') then
    create type listing_category as enum ('electronics','textbooks','furniture','clothing','services','other'); end if;
  if not exists (select 1 from pg_type where typname = 'listing_condition') then
    create type listing_condition as enum ('new','like_new','excellent','good','fair','service'); end if;
  if not exists (select 1 from pg_type where typname = 'listing_status') then
    create type listing_status as enum ('active','reserved','sold','removed'); end if;
  if not exists (select 1 from pg_type where typname = 'event_type') then
    create type event_type as enum ('career_fair','workshop','networking','open_day','seminar'); end if;
  if not exists (select 1 from pg_type where typname = 'accommodation_type') then
    create type accommodation_type as enum ('single_room','shared_room','bachelor','res','apartment'); end if;
  if not exists (select 1 from pg_type where typname = 'notification_category') then
    create type notification_category as enum ('application','bursary','job','marketplace','event','accommodation','system','message'); end if;
  if not exists (select 1 from pg_type where typname = 'qualification_type') then
    create type qualification_type as enum ('higher_certificate','diploma','bachelor','honours','masters','doctorate'); end if;
end $$;

-- =============================================================================
-- 2. CORE IDENTITY
-- =============================================================================
create table if not exists public.profiles (
  id           uuid primary key references auth.users(id) on delete cascade,
  role         user_role   not null default 'student',
  full_name    text        not null default '',
  email        citext,
  phone        text,
  avatar_url   text,
  email_verified boolean   not null default false,
  phone_verified boolean   not null default false,
  created_at   timestamptz not null default now(),
  updated_at   timestamptz not null default now()
);

create table if not exists public.students (
  id                uuid primary key references public.profiles(id) on delete cascade,
  student_type      student_type not null default 'sa',
  id_number         text,
  passport_number   text,
  country_of_origin text,
  study_permit      text,
  date_of_birth     date,
  gender            gender_type not null default 'undisclosed',
  province          text,
  school_name       text,
  grade             text,
  created_at        timestamptz not null default now(),
  updated_at        timestamptz not null default now()
);

create table if not exists public.universities (
  id               uuid primary key default gen_random_uuid(),
  name             text not null,
  short_code       text not null unique,
  domain           text,
  website          text,
  province         text not null,
  logo_url         text,
  min_aps          int,
  tuition_from     numeric(12,2),
  faculties_count  int  not null default 0,
  programmes_count int  not null default 0,
  is_verified      boolean not null default true,
  created_at       timestamptz not null default now(),
  updated_at       timestamptz not null default now()
);

create table if not exists public.university_admins (
  profile_id    uuid not null references public.profiles(id) on delete cascade,
  university_id uuid not null references public.universities(id) on delete cascade,
  title         text,
  created_at    timestamptz not null default now(),
  primary key (profile_id, university_id)
);

create table if not exists public.parent_students (
  parent_id    uuid not null references public.profiles(id) on delete cascade,
  student_id   uuid not null references public.students(id) on delete cascade,
  relationship text not null default 'parent',
  created_at   timestamptz not null default now(),
  primary key (parent_id, student_id)
);

create table if not exists public.counsellor_students (
  counsellor_id uuid not null references public.profiles(id) on delete cascade,
  student_id    uuid not null references public.students(id) on delete cascade,
  created_at    timestamptz not null default now(),
  primary key (counsellor_id, student_id)
);

-- =============================================================================
-- 3. RLS HELPER FUNCTIONS  (SECURITY DEFINER)
-- =============================================================================
create or replace function public.current_role_name()
returns user_role language sql stable security definer set search_path = public as $$
  select role from public.profiles where id = auth.uid()
$$;

create or replace function public.is_super_admin()
returns boolean language sql stable security definer set search_path = public as $$
  select exists (select 1 from public.profiles where id = auth.uid() and role = 'super_admin')
$$;

create or replace function public.is_university_admin(uni uuid)
returns boolean language sql stable security definer set search_path = public as $$
  select exists (select 1 from public.university_admins where profile_id = auth.uid() and university_id = uni)
$$;

create or replace function public.is_parent_of(sid uuid)
returns boolean language sql stable security definer set search_path = public as $$
  select exists (select 1 from public.parent_students where parent_id = auth.uid() and student_id = sid)
$$;

create or replace function public.is_counsellor_of(sid uuid)
returns boolean language sql stable security definer set search_path = public as $$
  select exists (select 1 from public.counsellor_students where counsellor_id = auth.uid() and student_id = sid)
$$;

-- =============================================================================
-- 4. ACADEMIC CATALOG
-- =============================================================================
create table if not exists public.faculties (
  id            uuid primary key default gen_random_uuid(),
  university_id uuid not null references public.universities(id) on delete cascade,
  name          text not null,
  created_at    timestamptz not null default now(),
  unique (university_id, name)
);

create table if not exists public.programmes (
  id                 uuid primary key default gen_random_uuid(),
  university_id      uuid not null references public.universities(id) on delete cascade,
  faculty_id         uuid references public.faculties(id) on delete set null,
  name               text not null,
  qualification      qualification_type not null default 'bachelor',
  min_aps            int  not null default 0,
  duration_years     numeric(3,1),
  tuition_per_year   numeric(12,2),
  description        text,
  application_deadline date,
  is_active          boolean not null default true,
  created_at         timestamptz not null default now(),
  updated_at         timestamptz not null default now()
);

create table if not exists public.nsc_subjects (
  id         uuid primary key default gen_random_uuid(),
  name       text not null unique,
  is_language boolean not null default false
);

create table if not exists public.programme_requirements (
  id          uuid primary key default gen_random_uuid(),
  programme_id uuid not null references public.programmes(id) on delete cascade,
  subject_id  uuid references public.nsc_subjects(id) on delete set null,
  subject_name text not null,
  min_level   int not null check (min_level between 1 and 7),
  created_at  timestamptz not null default now()
);

create table if not exists public.aps_rules (
  id            uuid primary key default gen_random_uuid(),
  university_id uuid references public.universities(id) on delete cascade,
  name          text not null,
  description   text,
  config        jsonb not null default '{}'::jsonb,
  is_active     boolean not null default true,
  created_at    timestamptz not null default now(),
  updated_at    timestamptz not null default now()
);

create table if not exists public.university_favourites (
  student_id    uuid not null references public.students(id) on delete cascade,
  university_id uuid not null references public.universities(id) on delete cascade,
  created_at    timestamptz not null default now(),
  primary key (student_id, university_id)
);

-- =============================================================================
-- 5. STUDENT ACADEMIC RESULTS
-- =============================================================================
create table if not exists public.student_results (
  id               uuid primary key default gen_random_uuid(),
  student_id       uuid not null references public.students(id) on delete cascade,
  subject_id       uuid references public.nsc_subjects(id) on delete set null,
  subject_name     text not null,
  level            int  not null check (level between 1 and 7),
  percentage       int  not null check (percentage between 0 and 100),
  is_life_orientation boolean not null default false,
  created_at       timestamptz not null default now(),
  updated_at       timestamptz not null default now(),
  unique (student_id, subject_name)
);

create or replace function public.aps_points(level int)
returns int language sql immutable as $$
  select case when level >= 7 then 7 when level >= 1 then level else 0 end
$$;

create or replace view public.student_aps
with (security_invoker = true) as
  select student_id, coalesce(sum(pts), 0)::int as aps
  from (
    select student_id,
           public.aps_points(level) as pts,
           row_number() over (partition by student_id order by public.aps_points(level) desc) as rn
    from public.student_results
    where not is_life_orientation
  ) ranked
  where rn <= 6
  group by student_id;

-- =============================================================================
-- 6. DOCUMENTS
-- =============================================================================
create table if not exists public.documents (
  id          uuid primary key default gen_random_uuid(),
  student_id  uuid not null references public.students(id) on delete cascade,
  type        document_type not null default 'other',
  name        text not null,
  storage_path text not null,
  size_bytes  bigint,
  is_verified boolean not null default false,
  uploaded_at timestamptz not null default now()
);

-- =============================================================================
-- 7. APPLICATIONS + PAYMENTS
-- =============================================================================
create table if not exists public.payments (
  id          uuid primary key default gen_random_uuid(),
  student_id  uuid not null references public.students(id) on delete cascade,
  amount      numeric(12,2) not null,
  currency    text not null default 'ZAR',
  method      payment_method not null,
  status      payment_status not null default 'pending',
  reference   text unique,
  description text,
  paid_at     timestamptz,
  created_at  timestamptz not null default now()
);

create table if not exists public.applications (
  id            uuid primary key default gen_random_uuid(),
  student_id    uuid not null references public.students(id) on delete cascade,
  university_id uuid not null references public.universities(id) on delete cascade,
  programme_id  uuid not null references public.programmes(id) on delete cascade,
  status        application_status not null default 'draft',
  fee_payment_id uuid references public.payments(id) on delete set null,
  aps_at_apply  int,
  notes         text,
  submitted_at  timestamptz,
  decision_at   timestamptz,
  created_at    timestamptz not null default now(),
  updated_at    timestamptz not null default now(),
  unique (student_id, programme_id)
);

create table if not exists public.application_documents (
  application_id uuid not null references public.applications(id) on delete cascade,
  document_id    uuid not null references public.documents(id) on delete cascade,
  primary key (application_id, document_id)
);

-- =============================================================================
-- 8. BURSARY HUB
-- =============================================================================
create table if not exists public.bursaries (
  id           uuid primary key default gen_random_uuid(),
  name         text not null,
  provider     text not null,
  field        bursary_field not null default 'other',
  amount_text  text,
  covers       text[] not null default '{}',
  min_aps      int,
  description  text,
  closes_on    date,
  is_active    boolean not null default true,
  created_by   uuid references public.profiles(id) on delete set null,
  created_at   timestamptz not null default now(),
  updated_at   timestamptz not null default now()
);

create table if not exists public.bursary_applications (
  id           uuid primary key default gen_random_uuid(),
  bursary_id   uuid not null references public.bursaries(id) on delete cascade,
  student_id   uuid not null references public.students(id) on delete cascade,
  status       bursary_app_status not null default 'submitted',
  submitted_at timestamptz not null default now(),
  unique (bursary_id, student_id)
);

create table if not exists public.bursary_bookmarks (
  bursary_id uuid not null references public.bursaries(id) on delete cascade,
  student_id uuid not null references public.students(id) on delete cascade,
  created_at timestamptz not null default now(),
  primary key (bursary_id, student_id)
);

-- =============================================================================
-- 9. JOB HUB
-- =============================================================================
create table if not exists public.jobs (
  id          uuid primary key default gen_random_uuid(),
  title       text not null,
  company     text not null,
  type        job_type not null,
  location    text,
  salary_text text,
  description text,
  tags        text[] not null default '{}',
  is_remote   boolean not null default false,
  closes_on   date,
  is_active   boolean not null default true,
  posted_by   uuid references public.profiles(id) on delete set null,
  created_at  timestamptz not null default now(),
  updated_at  timestamptz not null default now()
);

create table if not exists public.job_applications (
  id          uuid primary key default gen_random_uuid(),
  job_id      uuid not null references public.jobs(id) on delete cascade,
  student_id  uuid not null references public.students(id) on delete cascade,
  cv_document_id uuid references public.documents(id) on delete set null,
  status      job_app_status not null default 'applied',
  applied_at  timestamptz not null default now(),
  unique (job_id, student_id)
);

create table if not exists public.saved_jobs (
  job_id     uuid not null references public.jobs(id) on delete cascade,
  student_id uuid not null references public.students(id) on delete cascade,
  created_at timestamptz not null default now(),
  primary key (job_id, student_id)
);

-- =============================================================================
-- 10. MARKET HUB
-- =============================================================================
create table if not exists public.marketplace_listings (
  id          uuid primary key default gen_random_uuid(),
  seller_id   uuid not null references public.students(id) on delete cascade,
  title       text not null,
  category    listing_category not null,
  price       numeric(12,2) not null,
  condition   listing_condition not null default 'good',
  campus      text,
  description text,
  status      listing_status not null default 'active',
  created_at  timestamptz not null default now(),
  updated_at  timestamptz not null default now()
);

create table if not exists public.listing_images (
  id         uuid primary key default gen_random_uuid(),
  listing_id uuid not null references public.marketplace_listings(id) on delete cascade,
  storage_path text not null,
  position   int not null default 0
);

create table if not exists public.listing_wishlist (
  listing_id uuid not null references public.marketplace_listings(id) on delete cascade,
  student_id uuid not null references public.students(id) on delete cascade,
  created_at timestamptz not null default now(),
  primary key (listing_id, student_id)
);

create table if not exists public.marketplace_conversations (
  id         uuid primary key default gen_random_uuid(),
  listing_id uuid not null references public.marketplace_listings(id) on delete cascade,
  buyer_id   uuid not null references public.students(id) on delete cascade,
  seller_id  uuid not null references public.students(id) on delete cascade,
  created_at timestamptz not null default now(),
  unique (listing_id, buyer_id)
);

create table if not exists public.marketplace_messages (
  id              uuid primary key default gen_random_uuid(),
  conversation_id uuid not null references public.marketplace_conversations(id) on delete cascade,
  sender_id       uuid not null references public.profiles(id) on delete cascade,
  body            text not null,
  read_at         timestamptz,
  created_at      timestamptz not null default now()
);

create table if not exists public.seller_ratings (
  id         uuid primary key default gen_random_uuid(),
  listing_id uuid not null references public.marketplace_listings(id) on delete cascade,
  seller_id  uuid not null references public.students(id) on delete cascade,
  rater_id   uuid not null references public.students(id) on delete cascade,
  rating     int not null check (rating between 1 and 5),
  comment    text,
  created_at timestamptz not null default now(),
  unique (listing_id, rater_id)
);

-- =============================================================================
-- 11. EVENTS HUB
-- =============================================================================
create table if not exists public.events (
  id          uuid primary key default gen_random_uuid(),
  title       text not null,
  type        event_type not null,
  host        text,
  location    text,
  is_online   boolean not null default false,
  capacity    int,
  starts_at   timestamptz not null,
  ends_at     timestamptz,
  description text,
  created_by  uuid references public.profiles(id) on delete set null,
  created_at  timestamptz not null default now()
);

create table if not exists public.event_registrations (
  event_id      uuid not null references public.events(id) on delete cascade,
  student_id    uuid not null references public.students(id) on delete cascade,
  registered_at timestamptz not null default now(),
  primary key (event_id, student_id)
);

-- =============================================================================
-- 12. ACCOMMODATION HUB
-- =============================================================================
create table if not exists public.accommodations (
  id               uuid primary key default gen_random_uuid(),
  name             text not null,
  type             accommodation_type not null,
  price_per_month  numeric(12,2) not null,
  campus           text,
  distance_text    text,
  latitude         numeric(9,6),
  longitude        numeric(9,6),
  rating           numeric(2,1),
  reviews_count    int not null default 0,
  amenities        text[] not null default '{}',
  is_verified      boolean not null default false,
  nsfas_accredited boolean not null default false,
  is_active        boolean not null default true,
  created_at       timestamptz not null default now(),
  updated_at       timestamptz not null default now()
);

create table if not exists public.accommodation_favourites (
  accommodation_id uuid not null references public.accommodations(id) on delete cascade,
  student_id       uuid not null references public.students(id) on delete cascade,
  created_at       timestamptz not null default now(),
  primary key (accommodation_id, student_id)
);

-- =============================================================================
-- 13. INTERVIEW PRACTICE
-- =============================================================================
create table if not exists public.interview_sessions (
  id           uuid primary key default gen_random_uuid(),
  student_id   uuid not null references public.students(id) on delete cascade,
  category     text not null,
  score        int check (score between 0 and 100),
  started_at   timestamptz not null default now(),
  completed_at timestamptz
);

create table if not exists public.interview_feedback (
  id          uuid primary key default gen_random_uuid(),
  session_id  uuid not null references public.interview_sessions(id) on delete cascade,
  question    text not null,
  answer      text,
  clarity     int check (clarity between 0 and 100),
  confidence  int check (confidence between 0 and 100),
  relevance   int check (relevance between 0 and 100),
  structure   int check (structure between 0 and 100),
  strengths   text[] not null default '{}',
  improvements text[] not null default '{}',
  created_at  timestamptz not null default now()
);

-- =============================================================================
-- 14. MESSAGING & CAREER GUIDANCE
-- =============================================================================
create table if not exists public.conversations (
  id          uuid primary key default gen_random_uuid(),
  subject     text,
  created_by  uuid not null references public.profiles(id) on delete cascade,
  created_at  timestamptz not null default now()
);

create table if not exists public.conversation_participants (
  conversation_id uuid not null references public.conversations(id) on delete cascade,
  profile_id      uuid not null references public.profiles(id) on delete cascade,
  primary key (conversation_id, profile_id)
);

create table if not exists public.messages (
  id              uuid primary key default gen_random_uuid(),
  conversation_id uuid not null references public.conversations(id) on delete cascade,
  sender_id       uuid not null references public.profiles(id) on delete cascade,
  body            text not null,
  read_at         timestamptz,
  created_at      timestamptz not null default now()
);

create table if not exists public.career_recommendations (
  id            uuid primary key default gen_random_uuid(),
  student_id    uuid not null references public.students(id) on delete cascade,
  counsellor_id uuid references public.profiles(id) on delete set null,
  title         text not null,
  body          text,
  created_at    timestamptz not null default now()
);

-- =============================================================================
-- 15. NOTIFICATIONS, OTP, SETTINGS, AUDIT, APP SETTINGS
-- =============================================================================
create table if not exists public.notifications (
  id         uuid primary key default gen_random_uuid(),
  user_id    uuid not null references public.profiles(id) on delete cascade,
  category   notification_category not null default 'system',
  title      text not null,
  body       text,
  action_url text,
  is_read    boolean not null default false,
  created_at timestamptz not null default now()
);

create table if not exists public.otp_verifications (
  id          uuid primary key default gen_random_uuid(),
  user_id     uuid references public.profiles(id) on delete cascade,
  channel     otp_channel not null,
  purpose     otp_purpose not null default 'registration',
  destination text not null,
  code_hash   text not null,
  attempts    int not null default 0,
  expires_at  timestamptz not null,
  consumed_at timestamptz,
  created_at  timestamptz not null default now()
);

create table if not exists public.user_settings (
  user_id            uuid primary key references public.profiles(id) on delete cascade,
  notification_prefs jsonb not null default '{"email":true,"sms":true,"push":true}'::jsonb,
  theme              text not null default 'light',
  locale             text not null default 'en-ZA',
  updated_at         timestamptz not null default now()
);

create table if not exists public.audit_logs (
  id          uuid primary key default gen_random_uuid(),
  actor_id    uuid references public.profiles(id) on delete set null,
  action      text not null,
  entity_type text,
  entity_id   uuid,
  metadata    jsonb not null default '{}'::jsonb,
  created_at  timestamptz not null default now()
);

-- Platform settings for /admin-settings (super-admin managed)
create table if not exists public.app_settings (
  key         text primary key,
  value       jsonb not null default '{}'::jsonb,
  updated_by  uuid references public.profiles(id) on delete set null,
  updated_at  timestamptz not null default now()
);

-- =============================================================================
-- 15b. can_view_student()  (depends on applications table)
-- =============================================================================
create or replace function public.can_view_student(sid uuid)
returns boolean language sql stable security definer set search_path = public as $$
  select
    auth.uid() = sid
    or public.is_super_admin()
    or public.is_parent_of(sid)
    or public.is_counsellor_of(sid)
    or exists (
      select 1
      from public.applications a
      join public.university_admins ua on ua.university_id = a.university_id
      where a.student_id = sid and ua.profile_id = auth.uid()
    )
$$;

-- Report aggregate view
create or replace view public.application_funnel with (security_invoker = true) as
  select university_id, status, count(*) as n
  from public.applications group by university_id, status;

-- =============================================================================
-- 16. INDEXES
-- =============================================================================
create index if not exists idx_students_province              on public.students (province);
create index if not exists idx_university_admins_university   on public.university_admins (university_id);
create index if not exists idx_parent_students_student        on public.parent_students (student_id);
create index if not exists idx_counsellor_students_student    on public.counsellor_students (student_id);
create index if not exists idx_faculties_university           on public.faculties (university_id);
create index if not exists idx_programmes_university          on public.programmes (university_id);
create index if not exists idx_programmes_faculty             on public.programmes (faculty_id);
create index if not exists idx_programme_requirements_prog    on public.programme_requirements (programme_id);
create index if not exists idx_student_results_student        on public.student_results (student_id);
create index if not exists idx_documents_student              on public.documents (student_id);
create index if not exists idx_applications_student           on public.applications (student_id);
create index if not exists idx_applications_university        on public.applications (university_id);
create index if not exists idx_applications_programme         on public.applications (programme_id);
create index if not exists idx_applications_status            on public.applications (status);
create index if not exists idx_payments_student               on public.payments (student_id);
create index if not exists idx_bursary_applications_student   on public.bursary_applications (student_id);
create index if not exists idx_bursary_bookmarks_student      on public.bursary_bookmarks (student_id);
create index if not exists idx_job_applications_student       on public.job_applications (student_id);
create index if not exists idx_saved_jobs_student             on public.saved_jobs (student_id);
create index if not exists idx_listings_seller                on public.marketplace_listings (seller_id);
create index if not exists idx_listings_category              on public.marketplace_listings (category);
create index if not exists idx_listings_status                on public.marketplace_listings (status);
create index if not exists idx_wishlist_student               on public.listing_wishlist (student_id);
create index if not exists idx_mkt_conv_buyer                 on public.marketplace_conversations (buyer_id);
create index if not exists idx_mkt_conv_seller                on public.marketplace_conversations (seller_id);
create index if not exists idx_mkt_messages_conv              on public.marketplace_messages (conversation_id);
create index if not exists idx_event_regs_student             on public.event_registrations (student_id);
create index if not exists idx_acc_favs_student               on public.accommodation_favourites (student_id);
create index if not exists idx_interview_sessions_student     on public.interview_sessions (student_id);
create index if not exists idx_messages_conv                  on public.messages (conversation_id);
create index if not exists idx_notifications_user_read        on public.notifications (user_id, is_read);

-- =============================================================================
-- 17. TRIGGERS
-- =============================================================================
create or replace function public.set_updated_at()
returns trigger language plpgsql as $$
begin new.updated_at = now(); return new; end; $$;

do $$
declare t text;
begin
  foreach t in array array[
    'profiles','students','universities','programmes','aps_rules','student_results',
    'applications','bursaries','jobs','marketplace_listings','accommodations','user_settings'
  ] loop
    execute format('drop trigger if exists trg_%1$s_updated on public.%1$s;', t);
    execute format(
      'create trigger trg_%1$s_updated before update on public.%1$s
       for each row execute function public.set_updated_at();', t);
  end loop;
end $$;

create or replace function public.handle_new_user()
returns trigger language plpgsql security definer set search_path = public as $$
declare
  r user_role := coalesce((new.raw_user_meta_data->>'role')::user_role, 'student');
begin
  insert into public.profiles (id, role, full_name, email, phone)
  values (new.id, r, coalesce(new.raw_user_meta_data->>'full_name',''), new.email, new.raw_user_meta_data->>'phone')
  on conflict (id) do nothing;
  if r = 'student' then
    insert into public.students (id) values (new.id) on conflict (id) do nothing;
  end if;
  insert into public.user_settings (user_id) values (new.id) on conflict (user_id) do nothing;
  return new;
end $$;

drop trigger if exists on_auth_user_created on auth.users;
create trigger on_auth_user_created
  after insert on auth.users
  for each row execute function public.handle_new_user();

-- =============================================================================
-- 18. ENABLE ROW LEVEL SECURITY ON EVERY TABLE  (idempotent)
-- =============================================================================
do $$
declare t text;
begin
  foreach t in array array[
    'profiles','students','universities','university_admins','parent_students','counsellor_students',
    'faculties','programmes','nsc_subjects','programme_requirements','aps_rules','university_favourites',
    'student_results','documents','payments','applications','application_documents',
    'bursaries','bursary_applications','bursary_bookmarks',
    'jobs','job_applications','saved_jobs',
    'marketplace_listings','listing_images','listing_wishlist','marketplace_conversations','marketplace_messages','seller_ratings',
    'events','event_registrations','accommodations','accommodation_favourites',
    'interview_sessions','interview_feedback',
    'conversations','conversation_participants','messages','career_recommendations',
    'notifications','otp_verifications','user_settings','audit_logs','app_settings'
  ] loop
    execute format('alter table public.%I enable row level security;', t);
  end loop;
end $$;

-- =============================================================================
-- 19. RLS POLICIES  (drop-if-exists then create => idempotent)
-- =============================================================================

-- profiles
drop policy if exists "profiles: read self or linked or admin" on public.profiles;
create policy "profiles: read self or linked or admin" on public.profiles
  for select using (id = auth.uid() or public.is_super_admin() or public.is_parent_of(id) or public.is_counsellor_of(id));
drop policy if exists "profiles: insert self" on public.profiles;
create policy "profiles: insert self" on public.profiles for insert with check (id = auth.uid());
drop policy if exists "profiles: update self or admin" on public.profiles;
create policy "profiles: update self or admin" on public.profiles
  for update using (id = auth.uid() or public.is_super_admin()) with check (id = auth.uid() or public.is_super_admin());

-- students
drop policy if exists "students: read allowed viewers" on public.students;
create policy "students: read allowed viewers" on public.students for select using (public.can_view_student(id));
drop policy if exists "students: insert self" on public.students;
create policy "students: insert self" on public.students for insert with check (id = auth.uid());
drop policy if exists "students: update self or admin" on public.students;
create policy "students: update self or admin" on public.students
  for update using (id = auth.uid() or public.is_super_admin()) with check (id = auth.uid() or public.is_super_admin());

-- link tables
drop policy if exists "uni_admins: self read" on public.university_admins;
create policy "uni_admins: self read" on public.university_admins for select using (profile_id = auth.uid() or public.is_super_admin());
drop policy if exists "uni_admins: admin write" on public.university_admins;
create policy "uni_admins: admin write" on public.university_admins for all using (public.is_super_admin()) with check (public.is_super_admin());

drop policy if exists "parent_students: read involved" on public.parent_students;
create policy "parent_students: read involved" on public.parent_students for select using (parent_id = auth.uid() or student_id = auth.uid() or public.is_super_admin());
drop policy if exists "parent_students: admin write" on public.parent_students;
create policy "parent_students: admin write" on public.parent_students for all using (public.is_super_admin()) with check (public.is_super_admin());

drop policy if exists "counsellor_students: read involved" on public.counsellor_students;
create policy "counsellor_students: read involved" on public.counsellor_students for select using (counsellor_id = auth.uid() or student_id = auth.uid() or public.is_super_admin());
drop policy if exists "counsellor_students: admin write" on public.counsellor_students;
create policy "counsellor_students: admin write" on public.counsellor_students for all using (public.is_super_admin()) with check (public.is_super_admin());

-- public catalog
drop policy if exists "universities: public read" on public.universities;
create policy "universities: public read" on public.universities for select to anon, authenticated using (true);
drop policy if exists "universities: admin write" on public.universities;
create policy "universities: admin write" on public.universities
  for all using (public.is_super_admin() or public.is_university_admin(id)) with check (public.is_super_admin() or public.is_university_admin(id));

drop policy if exists "faculties: public read" on public.faculties;
create policy "faculties: public read" on public.faculties for select to anon, authenticated using (true);
drop policy if exists "faculties: uni admin write" on public.faculties;
create policy "faculties: uni admin write" on public.faculties
  for all using (public.is_super_admin() or public.is_university_admin(university_id)) with check (public.is_super_admin() or public.is_university_admin(university_id));

drop policy if exists "programmes: public read" on public.programmes;
create policy "programmes: public read" on public.programmes for select to anon, authenticated using (true);
drop policy if exists "programmes: uni admin write" on public.programmes;
create policy "programmes: uni admin write" on public.programmes
  for all using (public.is_super_admin() or public.is_university_admin(university_id)) with check (public.is_super_admin() or public.is_university_admin(university_id));

drop policy if exists "nsc_subjects: public read" on public.nsc_subjects;
create policy "nsc_subjects: public read" on public.nsc_subjects for select to anon, authenticated using (true);
drop policy if exists "nsc_subjects: admin write" on public.nsc_subjects;
create policy "nsc_subjects: admin write" on public.nsc_subjects for all using (public.is_super_admin()) with check (public.is_super_admin());

drop policy if exists "programme_requirements: public read" on public.programme_requirements;
create policy "programme_requirements: public read" on public.programme_requirements for select to anon, authenticated using (true);
drop policy if exists "programme_requirements: uni admin write" on public.programme_requirements;
create policy "programme_requirements: uni admin write" on public.programme_requirements
  for all using (public.is_super_admin() or exists (select 1 from public.programmes p where p.id = programme_id and public.is_university_admin(p.university_id)))
  with check (public.is_super_admin() or exists (select 1 from public.programmes p where p.id = programme_id and public.is_university_admin(p.university_id)));

drop policy if exists "aps_rules: public read" on public.aps_rules;
create policy "aps_rules: public read" on public.aps_rules for select to anon, authenticated using (true);
drop policy if exists "aps_rules: admin write" on public.aps_rules;
create policy "aps_rules: admin write" on public.aps_rules
  for all using (public.is_super_admin() or (university_id is not null and public.is_university_admin(university_id)))
  with check (public.is_super_admin() or (university_id is not null and public.is_university_admin(university_id)));

drop policy if exists "uni_favs: owner all" on public.university_favourites;
create policy "uni_favs: owner all" on public.university_favourites for all using (student_id = auth.uid()) with check (student_id = auth.uid());

-- student results
drop policy if exists "results: view allowed" on public.student_results;
create policy "results: view allowed" on public.student_results for select using (public.can_view_student(student_id));
drop policy if exists "results: owner write" on public.student_results;
create policy "results: owner write" on public.student_results
  for all using (student_id = auth.uid() or public.is_super_admin()) with check (student_id = auth.uid() or public.is_super_admin());

-- documents
drop policy if exists "documents: view allowed" on public.documents;
create policy "documents: view allowed" on public.documents for select using (public.can_view_student(student_id));
drop policy if exists "documents: owner write" on public.documents;
create policy "documents: owner write" on public.documents
  for all using (student_id = auth.uid() or public.is_super_admin()) with check (student_id = auth.uid() or public.is_super_admin());

-- payments
drop policy if exists "payments: view allowed" on public.payments;
create policy "payments: view allowed" on public.payments for select using (student_id = auth.uid() or public.is_super_admin());
drop policy if exists "payments: owner create" on public.payments;
create policy "payments: owner create" on public.payments for insert with check (student_id = auth.uid());
drop policy if exists "payments: admin update" on public.payments;
create policy "payments: admin update" on public.payments for update using (public.is_super_admin()) with check (public.is_super_admin());

-- applications
drop policy if exists "applications: read viewers or uni admin" on public.applications;
create policy "applications: read viewers or uni admin" on public.applications
  for select using (public.can_view_student(student_id) or public.is_university_admin(university_id));
drop policy if exists "applications: student create" on public.applications;
create policy "applications: student create" on public.applications for insert with check (student_id = auth.uid());
drop policy if exists "applications: student or uni admin update" on public.applications;
create policy "applications: student or uni admin update" on public.applications
  for update using (
    (student_id = auth.uid() and status in ('draft','submitted','pending_documents'))
    or public.is_university_admin(university_id) or public.is_super_admin())
  with check (student_id = auth.uid() or public.is_university_admin(university_id) or public.is_super_admin());
drop policy if exists "applications: owner delete draft" on public.applications;
create policy "applications: owner delete draft" on public.applications
  for delete using ((student_id = auth.uid() and status = 'draft') or public.is_super_admin());

drop policy if exists "app_docs: read via application" on public.application_documents;
create policy "app_docs: read via application" on public.application_documents
  for select using (exists (select 1 from public.applications a where a.id = application_id
    and (public.can_view_student(a.student_id) or public.is_university_admin(a.university_id))));
drop policy if exists "app_docs: owner write" on public.application_documents;
create policy "app_docs: owner write" on public.application_documents
  for all using (exists (select 1 from public.applications a where a.id = application_id and a.student_id = auth.uid()))
  with check (exists (select 1 from public.applications a where a.id = application_id and a.student_id = auth.uid()));

-- bursaries
drop policy if exists "bursaries: public read" on public.bursaries;
create policy "bursaries: public read" on public.bursaries for select to anon, authenticated using (true);
drop policy if exists "bursaries: admin write" on public.bursaries;
create policy "bursaries: admin write" on public.bursaries for all using (public.is_super_admin()) with check (public.is_super_admin());

drop policy if exists "bursary_apps: view allowed" on public.bursary_applications;
create policy "bursary_apps: view allowed" on public.bursary_applications for select using (public.can_view_student(student_id));
drop policy if exists "bursary_apps: owner write" on public.bursary_applications;
create policy "bursary_apps: owner write" on public.bursary_applications for all using (student_id = auth.uid()) with check (student_id = auth.uid());

drop policy if exists "bursary_bookmarks: owner all" on public.bursary_bookmarks;
create policy "bursary_bookmarks: owner all" on public.bursary_bookmarks for all using (student_id = auth.uid()) with check (student_id = auth.uid());

-- jobs
drop policy if exists "jobs: authenticated read" on public.jobs;
create policy "jobs: authenticated read" on public.jobs for select to authenticated using (true);
drop policy if exists "jobs: admin write" on public.jobs;
create policy "jobs: admin write" on public.jobs
  for all using (public.is_super_admin() or posted_by = auth.uid()) with check (public.is_super_admin() or posted_by = auth.uid());

drop policy if exists "job_apps: view allowed" on public.job_applications;
create policy "job_apps: view allowed" on public.job_applications for select using (public.can_view_student(student_id));
drop policy if exists "job_apps: owner write" on public.job_applications;
create policy "job_apps: owner write" on public.job_applications for all using (student_id = auth.uid()) with check (student_id = auth.uid());

drop policy if exists "saved_jobs: owner all" on public.saved_jobs;
create policy "saved_jobs: owner all" on public.saved_jobs for all using (student_id = auth.uid()) with check (student_id = auth.uid());

-- marketplace
drop policy if exists "listings: authenticated read active" on public.marketplace_listings;
create policy "listings: authenticated read active" on public.marketplace_listings
  for select to authenticated using (status <> 'removed' or seller_id = auth.uid());
drop policy if exists "listings: seller write" on public.marketplace_listings;
create policy "listings: seller write" on public.marketplace_listings
  for all using (seller_id = auth.uid() or public.is_super_admin()) with check (seller_id = auth.uid() or public.is_super_admin());

drop policy if exists "listing_images: authenticated read" on public.listing_images;
create policy "listing_images: authenticated read" on public.listing_images for select to authenticated using (true);
drop policy if exists "listing_images: seller write" on public.listing_images;
create policy "listing_images: seller write" on public.listing_images
  for all using (exists (select 1 from public.marketplace_listings l where l.id = listing_id and l.seller_id = auth.uid()))
  with check (exists (select 1 from public.marketplace_listings l where l.id = listing_id and l.seller_id = auth.uid()));

drop policy if exists "wishlist: owner all" on public.listing_wishlist;
create policy "wishlist: owner all" on public.listing_wishlist for all using (student_id = auth.uid()) with check (student_id = auth.uid());

drop policy if exists "conversations: participants read" on public.marketplace_conversations;
create policy "conversations: participants read" on public.marketplace_conversations for select using (buyer_id = auth.uid() or seller_id = auth.uid());
drop policy if exists "conversations: buyer create" on public.marketplace_conversations;
create policy "conversations: buyer create" on public.marketplace_conversations for insert with check (buyer_id = auth.uid());

drop policy if exists "mkt_messages: participants read" on public.marketplace_messages;
create policy "mkt_messages: participants read" on public.marketplace_messages
  for select using (exists (select 1 from public.marketplace_conversations c where c.id = conversation_id and (c.buyer_id = auth.uid() or c.seller_id = auth.uid())));
drop policy if exists "mkt_messages: participants send" on public.marketplace_messages;
create policy "mkt_messages: participants send" on public.marketplace_messages
  for insert with check (sender_id = auth.uid() and exists (select 1 from public.marketplace_conversations c where c.id = conversation_id and (c.buyer_id = auth.uid() or c.seller_id = auth.uid())));

drop policy if exists "ratings: authenticated read" on public.seller_ratings;
create policy "ratings: authenticated read" on public.seller_ratings for select to authenticated using (true);
drop policy if exists "ratings: rater write" on public.seller_ratings;
create policy "ratings: rater write" on public.seller_ratings for all using (rater_id = auth.uid()) with check (rater_id = auth.uid() and rater_id <> seller_id);

-- events
drop policy if exists "events: public read" on public.events;
create policy "events: public read" on public.events for select to anon, authenticated using (true);
drop policy if exists "events: admin write" on public.events;
create policy "events: admin write" on public.events
  for all using (public.is_super_admin() or created_by = auth.uid()) with check (public.is_super_admin() or created_by = auth.uid());

drop policy if exists "event_regs: view allowed" on public.event_registrations;
create policy "event_regs: view allowed" on public.event_registrations for select using (public.can_view_student(student_id) or public.is_super_admin());
drop policy if exists "event_regs: owner all" on public.event_registrations;
create policy "event_regs: owner all" on public.event_registrations for all using (student_id = auth.uid()) with check (student_id = auth.uid());

-- accommodation
drop policy if exists "accommodations: public read" on public.accommodations;
create policy "accommodations: public read" on public.accommodations for select to anon, authenticated using (true);
drop policy if exists "accommodations: admin write" on public.accommodations;
create policy "accommodations: admin write" on public.accommodations for all using (public.is_super_admin()) with check (public.is_super_admin());

drop policy if exists "acc_favs: owner all" on public.accommodation_favourites;
create policy "acc_favs: owner all" on public.accommodation_favourites for all using (student_id = auth.uid()) with check (student_id = auth.uid());

-- interview practice
drop policy if exists "interview_sessions: view allowed" on public.interview_sessions;
create policy "interview_sessions: view allowed" on public.interview_sessions for select using (public.can_view_student(student_id));
drop policy if exists "interview_sessions: owner write" on public.interview_sessions;
create policy "interview_sessions: owner write" on public.interview_sessions for all using (student_id = auth.uid()) with check (student_id = auth.uid());

drop policy if exists "interview_feedback: via session" on public.interview_feedback;
create policy "interview_feedback: via session" on public.interview_feedback
  for select using (exists (select 1 from public.interview_sessions s where s.id = session_id and public.can_view_student(s.student_id)));
drop policy if exists "interview_feedback: owner write" on public.interview_feedback;
create policy "interview_feedback: owner write" on public.interview_feedback
  for all using (exists (select 1 from public.interview_sessions s where s.id = session_id and s.student_id = auth.uid()))
  with check (exists (select 1 from public.interview_sessions s where s.id = session_id and s.student_id = auth.uid()));

-- messaging
drop policy if exists "conv: participant read" on public.conversations;
create policy "conv: participant read" on public.conversations
  for select using (exists (select 1 from public.conversation_participants p where p.conversation_id = id and p.profile_id = auth.uid()) or public.is_super_admin());
drop policy if exists "conv: creator insert" on public.conversations;
create policy "conv: creator insert" on public.conversations for insert with check (created_by = auth.uid());

drop policy if exists "conv_participants: self read" on public.conversation_participants;
create policy "conv_participants: self read" on public.conversation_participants
  for select using (profile_id = auth.uid() or exists (select 1 from public.conversations c where c.id = conversation_id and c.created_by = auth.uid()));
drop policy if exists "conv_participants: creator manage" on public.conversation_participants;
create policy "conv_participants: creator manage" on public.conversation_participants
  for all using (exists (select 1 from public.conversations c where c.id = conversation_id and c.created_by = auth.uid()))
  with check (exists (select 1 from public.conversations c where c.id = conversation_id and c.created_by = auth.uid()));

drop policy if exists "messages: participant read" on public.messages;
create policy "messages: participant read" on public.messages
  for select using (exists (select 1 from public.conversation_participants p where p.conversation_id = messages.conversation_id and p.profile_id = auth.uid()));
drop policy if exists "messages: participant send" on public.messages;
create policy "messages: participant send" on public.messages
  for insert with check (sender_id = auth.uid() and exists (select 1 from public.conversation_participants p where p.conversation_id = messages.conversation_id and p.profile_id = auth.uid()));

drop policy if exists "career_recs: view allowed" on public.career_recommendations;
create policy "career_recs: view allowed" on public.career_recommendations for select using (public.can_view_student(student_id));
drop policy if exists "career_recs: counsellor write" on public.career_recommendations;
create policy "career_recs: counsellor write" on public.career_recommendations
  for all using (counsellor_id = auth.uid() or public.is_super_admin()) with check (counsellor_id = auth.uid() or public.is_super_admin());

-- notifications
drop policy if exists "notifications: owner read" on public.notifications;
create policy "notifications: owner read" on public.notifications for select using (user_id = auth.uid());
drop policy if exists "notifications: owner update read-flag" on public.notifications;
create policy "notifications: owner update read-flag" on public.notifications for update using (user_id = auth.uid()) with check (user_id = auth.uid());
drop policy if exists "notifications: admin insert" on public.notifications;
create policy "notifications: admin insert" on public.notifications for insert with check (public.is_super_admin());

-- otp
drop policy if exists "otp: owner read" on public.otp_verifications;
create policy "otp: owner read" on public.otp_verifications for select using (user_id = auth.uid());

-- user settings
drop policy if exists "settings: owner all" on public.user_settings;
create policy "settings: owner all" on public.user_settings for all using (user_id = auth.uid()) with check (user_id = auth.uid());

-- audit logs
drop policy if exists "audit: admin read" on public.audit_logs;
create policy "audit: admin read" on public.audit_logs for select using (public.is_super_admin());

-- app settings
drop policy if exists "app_settings: admin read" on public.app_settings;
create policy "app_settings: admin read" on public.app_settings for select using (public.is_super_admin());
drop policy if exists "app_settings: admin write" on public.app_settings;
create policy "app_settings: admin write" on public.app_settings for all using (public.is_super_admin()) with check (public.is_super_admin());

-- =============================================================================
-- 20. STORAGE BUCKETS + POLICIES
-- NOTE: this section was truncated in the source paste and reconstructed to match
-- the buckets (documents=private, avatars/listings=public) and the
-- "<user-uuid>/<filename>" path convention. Verify against your original.
-- =============================================================================
insert into storage.buckets (id, name, public)
values ('documents','documents', false),
       ('avatars','avatars', true),
       ('listings','listings', true)
on conflict (id) do nothing;

-- documents: private, owner-only (path prefix = owner uid)
drop policy if exists "storage: read own documents" on storage.objects;
create policy "storage: read own documents" on storage.objects
  for select to authenticated
  using (bucket_id = 'documents' and (storage.foldername(name))[1] = auth.uid()::text);
drop policy if exists "storage: write own documents" on storage.objects;
create policy "storage: write own documents" on storage.objects
  for insert to authenticated
  with check (bucket_id = 'documents' and (storage.foldername(name))[1] = auth.uid()::text);
drop policy if exists "storage: update own documents" on storage.objects;
create policy "storage: update own documents" on storage.objects
  for update to authenticated
  using (bucket_id = 'documents' and (storage.foldername(name))[1] = auth.uid()::text);
drop policy if exists "storage: delete own documents" on storage.objects;
create policy "storage: delete own documents" on storage.objects
  for delete to authenticated
  using (bucket_id = 'documents' and (storage.foldername(name))[1] = auth.uid()::text);

-- avatars & listings: public read, owner-scoped write (path prefix = owner uid)
drop policy if exists "storage: public read avatars/listings" on storage.objects;
create policy "storage: public read avatars/listings" on storage.objects
  for select to anon, authenticated
  using (bucket_id in ('avatars','listings'));
drop policy if exists "storage: write own avatars/listings" on storage.objects;
create policy "storage: write own avatars/listings" on storage.objects
  for insert to authenticated
  with check (bucket_id in ('avatars','listings') and (storage.foldername(name))[1] = auth.uid()::text);
drop policy if exists "storage: update own avatars/listings" on storage.objects;
create policy "storage: update own avatars/listings" on storage.objects
  for update to authenticated
  using (bucket_id in ('avatars','listings') and (storage.foldername(name))[1] = auth.uid()::text);
drop policy if exists "storage: delete own avatars/listings" on storage.objects;
create policy "storage: delete own avatars/listings" on storage.objects
  for delete to authenticated
  using (bucket_id in ('avatars','listings') and (storage.foldername(name))[1] = auth.uid()::text);

-- =============================================================================
-- End of schema.
-- =============================================================================
