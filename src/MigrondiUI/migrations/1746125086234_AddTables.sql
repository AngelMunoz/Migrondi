-- MIGRONDI:NAME=1746125086234_AddTables.sql
-- MIGRONDI:TIMESTAMP=1746125086234
-- ---------- MIGRONDI:UP ----------
-- Add your SQLite migration code below. You can delete this line but do not delete the comments above.

create table if not exists "projects"(
    "id" text primary key,
    "name" text not null,
    "description" text
);

create table if not exists "local_projects"(
    "id" text primary key,
    "config_path" text not null unique,
    "project_id" text not null unique,
    foreign key("project_id") references "projects"("id") on delete cascade
);

create table if not exists "virtual_projects"(
    "id" text primary key,
    "connection" text not null,
    "table_name" text not null,
    "driver" text not null,
    "project_id" text not null unique,
    foreign key("project_id") references "projects"("id") on delete cascade
);

create table if not exists "migrations"(
    "id" integer primary key,
    "name" text not null,
    "timestamp" integer not null,
    "up_content" text not null,
    "down_content" text not null,
    "virtual_project_id" text not null,
    "manual_transaction" integer not null,
    foreign key("virtual_project_id") references "virtual_projects"("id") on delete cascade
);

-- ---------- MIGRONDI:DOWN ----------
-- Add your SQLite rollback code below. You can delete this line but do not delete the comment above.

-- This migration does not support rollback.
-- Format: SQLite
raise fail 'Rollback is not supported for this migration';

