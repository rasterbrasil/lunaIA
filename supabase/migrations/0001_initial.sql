-- LUNA IA — initial data model
-- Apply through the Supabase migration workflow when the application layer is ready.

create table if not exists public.devices (
  id uuid primary key default gen_random_uuid(),
  user_id uuid not null references auth.users(id) on delete cascade,
  name text not null,
  platform text not null check (platform in ('windows', 'android', 'ios', 'web')),
  status text not null default 'offline' check (status in ('online', 'offline', 'pending')),
  last_seen_at timestamptz,
  created_at timestamptz not null default now()
);

create table if not exists public.device_permissions (
  id uuid primary key default gen_random_uuid(),
  device_id uuid not null references public.devices(id) on delete cascade,
  permission_key text not null,
  enabled boolean not null default false,
  created_at timestamptz not null default now(),
  unique (device_id, permission_key)
);

create table if not exists public.tasks (
  id uuid primary key default gen_random_uuid(),
  user_id uuid not null references auth.users(id) on delete cascade,
  device_id uuid references public.devices(id) on delete set null,
  instruction text not null,
  status text not null default 'pending' check (status in ('pending', 'running', 'awaiting_confirmation', 'completed', 'failed', 'cancelled')),
  result jsonb,
  created_at timestamptz not null default now(),
  completed_at timestamptz
);

create table if not exists public.events (
  id bigint generated always as identity primary key,
  user_id uuid not null references auth.users(id) on delete cascade,
  device_id uuid references public.devices(id) on delete set null,
  event_type text not null,
  payload jsonb not null default '{}'::jsonb,
  created_at timestamptz not null default now()
);

alter table public.devices enable row level security;
alter table public.device_permissions enable row level security;
alter table public.tasks enable row level security;
alter table public.events enable row level security;

create policy "users can manage their devices"
on public.devices for all
using (auth.uid() = user_id)
with check (auth.uid() = user_id);

create policy "users can manage permissions for their devices"
on public.device_permissions for all
using (exists (select 1 from public.devices d where d.id = device_id and d.user_id = auth.uid()))
with check (exists (select 1 from public.devices d where d.id = device_id and d.user_id = auth.uid()));

create policy "users can manage their tasks"
on public.tasks for all
using (auth.uid() = user_id)
with check (auth.uid() = user_id);

create policy "users can read their events"
on public.events for select
using (auth.uid() = user_id);

create policy "users can create their events"
on public.events for insert
with check (auth.uid() = user_id);
